using System.Collections.Concurrent;
using System.Diagnostics;

namespace Sandbox.ContentBuild;

/// <summary>
/// One contentworker process: jobs with one set of mounts, several at once, each on a thread of its own
/// in the process. The process starts each job as a fresh one would (see ContentCompilerHost.Serve).
/// </summary>
sealed class CompilerWorker
{
	readonly Process process;
	readonly ConcurrentDictionary<string, TaskCompletionSource<Answer>> pending = new();
	readonly Queue<string> tail = new();
	readonly object input = new();
	readonly List<string> published = [];
	bool exited;
	volatile bool gaveUp;
	static int requests;

	public string Key { get; }

	public int ProcessId => process.Id;

	/// <summary>
	/// Jobs the pool gave it that haven't come back yet.
	/// </summary>
	public int Busy { get; set; }

	/// <summary>
	/// It said it's ready for jobs, so it started.
	/// </summary>
	public bool Started { get; private set; }

	/// <summary>
	/// How a job ended. Answered: it ran and succeeded or failed; or the worker refused it (it has an older output
	/// loaded than what's published, the job never ran) or couldn't run it (an error), and takes no more. Not answered:
	/// the process was lost, or ended for another of its jobs that timed out or was cancelled or before the job was sent
	/// (interrupted), or the job timed out or was cancelled.
	/// </summary>
	public enum Outcome { Succeeded, Failed, Refused, Error, Lost, Interrupted, TimedOut, Cancelled }

	/// <summary>
	/// How a job ended, why if it didn't run, and for one not answered the last lines the process logged.
	/// </summary>
	public sealed record Answer( Outcome Outcome, string Reason, string Log );

	/// <summary>
	/// Outputs just published, which its next job checks it has no older one of loaded before it starts.
	/// </summary>
	public void Published( IReadOnlyList<string> paths )
	{
		lock ( published ) published.AddRange( paths );
	}

	public CompilerWorker( string executable, string key, IEnumerable<string> args, string workingDirectory )
	{
		Key = key;
		var info = new ProcessStartInfo( executable )
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		foreach ( var arg in args ) info.ArgumentList.Add( arg );

		process = new Process { StartInfo = info };
		process.Start();

		// Both pipes are read on threads of their own: a process whose pipe is full stops, and the thread pool can be
		// taken up entirely by jobs waiting for their answers
		var readers = 2;
		void End()
		{
			if ( Interlocked.Decrement( ref readers ) == 0 ) Exited();
		}
		Read( process.StandardOutput, Line, End );
		Read( process.StandardError, Keep, End );
	}

	static void Read( StreamReader reader, Action<string> line, Action end )
	{
		new Thread( () =>
		{
			try
			{
				while ( reader.ReadLine() is { } text ) line( text );
			}
			catch ( IOException ) { }
			catch ( ObjectDisposedException ) { }
			finally { end(); }
		} )
		{ IsBackground = true, Name = "contentworker output" }.Start();
	}

	/// <summary>
	/// A line of standard output: an answer, or what the process logs, kept for when it dies. An answer can follow
	/// a partial line of native output, so it's found anywhere in a line.
	/// </summary>
	void Line( string line )
	{
		if ( line.Contains( "\u0001ready" ) )
		{
			Started = true;
			return;
		}

		var at = line.IndexOf( "\u0001done\t", StringComparison.Ordinal );
		if ( at < 0 )
		{
			Keep( line );
			return;
		}

		if ( at > 0 ) Keep( line[..at] );

		var fields = line[(at + 1)..].Split( '\t' );
		Outcome? outcome = fields.Length < 3 ? null : fields[2] switch
		{
			"succeeded" => Outcome.Succeeded,
			"failed" => Outcome.Failed,
			"refused" => Outcome.Refused,
			"error" => Outcome.Error,
			_ => null,
		};
		if ( outcome is null || !pending.TryRemove( fields[1], out var job ) )
		{
			Keep( $"answered no request: {line[(at + 1)..]}" );
			Kill();
			return;
		}

		job.TrySetResult( new Answer( outcome.Value, fields.Length > 3 ? fields[3] : null, "" ) );
	}

	/// <summary>
	/// The last lines the process logged, for a job it never answered.
	/// </summary>
	void Keep( string line )
	{
		// Shutdown spews a line per leaked convar
		if ( line.StartsWith( "***", StringComparison.Ordinal ) ) return;

		lock ( tail )
		{
			tail.Enqueue( line );
			if ( tail.Count > 200 ) tail.Dequeue();
		}
	}

	string Tail()
	{
		lock ( tail ) return string.Join( "\n", tail ).Trim();
	}

	/// <summary>
	/// Its output ended, so the process did: every job it hasn't answered never will be.
	/// </summary>
	void Exited()
	{
		lock ( input ) exited = true;
		foreach ( var id in pending.Keys )
		{
			if ( pending.TryRemove( id, out var job ) ) job.TrySetResult( gaveUp ? Unanswered( Outcome.Interrupted, "ended for another job that timed out or was cancelled" ) : Unanswered( Outcome.Lost, "exited during a job" ) );
		}
	}

	/// <summary>
	/// Runs one job with its compiler arguments and waits for its answer, while the process runs others. A job that times
	/// out or is cancelled ends the process, so the jobs it runs besides are interrupted.
	/// </summary>
	public Answer Run( string mode, string source, string staging, string report, IReadOnlyList<string> compilerArgs, TimeSpan timeout, CancellationToken token )
	{
		var id = Interlocked.Increment( ref requests ).ToString();
		var job = new TaskCompletionSource<Answer>( TaskCreationOptions.RunContinuationsAsynchronously );
		pending[id] = job;

		// Ends it at once, even while its request waits on a process that stopped reading them
		using var cancel = token.Register( GiveUp );

		lock ( input )
		{
			try
			{
				// Taken from the pool after it ended, before its jobs gave it back: never sent, so not this job's loss
				if ( exited )
				{
					pending.TryRemove( id, out _ );
					return Unanswered( Outcome.Interrupted, "the process had ended" );
				}
				string[] outputs;
				lock ( published )
				{
					outputs = [$"{published.Count}", .. published];
					published.Clear();
				}
				process.StandardInput.WriteLine( string.Join( '\t', [id, mode, source, staging, report, .. outputs, .. compilerArgs] ) );
				process.StandardInput.Flush();
			}
			catch ( IOException e )
			{
				pending.TryRemove( id, out _ );
				Kill();
				return token.IsCancellationRequested ? Unanswered( Outcome.Cancelled, "cancelled" ) : Unanswered( Outcome.Lost, $"lost: {e.Message}" );
			}
		}

		try
		{
			// Cancelling ended the process, which may have answered it interrupted first
			if ( job.Task.Wait( timeout, token ) && !token.IsCancellationRequested ) return job.Task.Result;
		}
		catch ( OperationCanceledException ) { }

		pending.TryRemove( id, out _ );
		GiveUp();
		return token.IsCancellationRequested ? Unanswered( Outcome.Cancelled, "cancelled" ) : Unanswered( Outcome.TimedOut, $"timed out after {timeout}" );
	}

	/// <summary>
	/// Ends the process for a job it gave up on: the jobs it runs besides aren't to blame.
	/// </summary>
	void GiveUp()
	{
		gaveUp = true;
		Kill();
	}

	Answer Unanswered( Outcome outcome, string reason ) => new( outcome, reason, Tail() );

	void Kill()
	{
		try { process.Kill( true ); } catch ( InvalidOperationException ) { }
	}

	/// <summary>
	/// End of input ends the process once it has answered what it was given; one that doesn't go is killed, and waited
	/// for a while too, so that it has normally let go of the files it loaded (the compilers its caller may delete next)
	/// when this returns.
	/// </summary>
	public void Stop()
	{
		lock ( input )
		{
			try { process.StandardInput.Close(); } catch ( IOException ) { }
		}
		if ( !process.WaitForExit( 10_000 ) )
		{
			Kill();
			process.WaitForExit( 10_000 );
		}
		process.Dispose();
	}
}
