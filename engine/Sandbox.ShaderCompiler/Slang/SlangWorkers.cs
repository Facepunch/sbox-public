using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.ShaderCompiler.Slang;

/// <summary>
/// The threads Slang compiles on: one per session (<see cref="SlangApi.MaxSessions"/>), below normal priority so a game's
/// own threads come first, made on first use. Each job is one Slang request; a job never waits for another, so callers
/// can wait for their jobs from any thread. Urgent jobs (a compile something waits for) run before background ones (warming).
/// </summary>
internal static class SlangWorkers
{
	static readonly Queue<Action> urgent = new();
	static readonly Queue<Action> background = new();
	static int threads, waiting;

	/// <summary>Run <paramref name="job"/> on a compile thread.</summary>
	public static Task<T> Run<T>( Func<T> job, bool urgentJob = true )
	{
		var done = new TaskCompletionSource<T>( TaskCreationOptions.RunContinuationsAsynchronously );
		Enqueue( () =>
		{
			try
			{
				done.SetResult( job() );
			}
			catch ( Exception e )
			{
				done.SetException( e );
			}
		}, urgentJob );
		return done.Task;
	}

	/// <summary>Whether the current thread is a compile thread.</summary>
	public static bool OnWorker => onWorker;

	[ThreadStatic] static bool onWorker;

	static void Enqueue( Action job, bool urgentJob )
	{
		var start = false;
		lock ( urgent )
		{
			(urgentJob ? urgent : background).Enqueue( job );
			if ( waiting > 0 ) Monitor.Pulse( urgent );
			else if ( threads < SlangApi.MaxSessions )
			{
				threads++;
				start = true;
			}
		}

		if ( start ) new Thread( Work ) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Slang compile" }.Start();
	}

	static void Work()
	{
		onWorker = true;
		while ( true )
		{
			Action job;
			lock ( urgent )
			{
				while ( urgent.Count == 0 && background.Count == 0 )
				{
					waiting++;
					Monitor.Wait( urgent );
					waiting--;
				}

				job = urgent.Count > 0 ? urgent.Dequeue() : background.Dequeue();
			}

			job();
		}
	}
}
