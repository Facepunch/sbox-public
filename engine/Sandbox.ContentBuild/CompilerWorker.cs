using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Sandbox.ContentBuild;

/// <summary>
/// One contentworker process: jobs with one set of mounts and arguments, one at a time. The process starts each job as
/// a fresh one would (see ContentCompilerHost.Serve).
/// </summary>
sealed class CompilerWorker
{
	readonly Process process;
	readonly BlockingCollection<(bool Error, string Line)> output = new();
	static int requests;

	public string Key { get; }

	public int ProcessId => process.Id;

	/// <summary>
	/// It said it's ready for jobs, so it started.
	/// </summary>
	public bool Started { get; private set; }

	/// <summary>
	/// How a job ended: answered (with its result, and whether the worker can take another), or not, and why.
	/// </summary>
	public sealed record Answer( bool Answered, bool Result, bool TakesAnother, string Reason, string Log );

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
		void End() { if ( Interlocked.Decrement( ref readers ) == 0 ) output.CompleteAdding(); }
		Read( process.StandardOutput, line => output.Add( (false, line) ), End );
		Read( process.StandardError, line => output.Add( (true, line) ), End );
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
			finally { end?.Invoke(); }
		} )
		{ IsBackground = true, Name = "resourcecompiler output" }.Start();
	}

	/// <summary>
	/// Runs one job and waits for its answer. Everything the process prints before the answer is the job's log; an
	/// answer is matched to its request, so nothing from an earlier job can be taken for this one's.
	/// </summary>
	public Answer Run( string mode, string source, string staging, string report, TimeSpan timeout, CancellationToken token )
	{
		var id = Interlocked.Increment( ref requests ).ToString();
		var log = new StringBuilder();
		var deadline = Stopwatch.StartNew();
		Answer answer = null;
		var errorsDone = false;

		using var cancel = token.Register( Kill );
		try
		{
			process.StandardInput.WriteLine( string.Join( '\t', id, mode, source, staging, report ) );
			process.StandardInput.Flush();

			while ( true )
			{
				var left = timeout - deadline.Elapsed;
				if ( !output.TryTake( out var entry, left > TimeSpan.Zero ? left : TimeSpan.Zero ) )
				{
					if ( output.IsCompleted ) return Unanswered( token.IsCancellationRequested ? "cancelled" : "exited during a job", log );

					Kill();
					return Unanswered( $"timed out after {timeout}", log );
				}

				var (error, line) = entry;
				if ( !error && line == "\u0001ready" )
				{
					Started = true;
					continue;
				}

				if ( !line.StartsWith( "\u0001done\t", StringComparison.Ordinal ) )
				{
					// Shutdown spews a line per leaked convar
					if ( error || !line.StartsWith( "***", StringComparison.Ordinal ) ) log.AppendLine( line );
					continue;
				}

				var fields = line[1..].Split( '\t' );
				if ( fields.Length < (error ? 2 : 4) || fields[1] != id )
				{
					Kill();
					return Unanswered( $"answered another request: {line[1..]}", log );
				}

				// The pipes arrive independently. Don't give the worker another job until both have reached this one.
				if ( error ) errorsDone = true;
				else answer = new Answer( true, fields[2] == "1", fields[3] == "1", fields.Length > 4 ? fields[4] : null, "" );
				if ( answer is not null && errorsDone ) return answer with { Log = log.ToString().Trim() };
			}
		}
		catch ( IOException e )
		{
			Kill();
			return Unanswered( token.IsCancellationRequested ? "cancelled" : $"lost: {e.Message}", log );
		}
	}

	static Answer Unanswered( string reason, StringBuilder log ) => new( false, false, false, reason, log.ToString().Trim() );

	void Kill()
	{
		try { process.Kill( true ); } catch ( InvalidOperationException ) { }
	}

	/// <summary>
	/// End of input ends the process; one that doesn't go is killed, and waited for a while too, so that it has normally
	/// let go of the files it loaded (the compilers its caller may delete next) when this returns.
	/// </summary>
	public void Stop()
	{
		try { process.StandardInput.Close(); } catch ( IOException ) { }
		if ( !process.WaitForExit( 10_000 ) )
		{
			Kill();
			process.WaitForExit( 10_000 );
		}
		process.Dispose();
	}
}
