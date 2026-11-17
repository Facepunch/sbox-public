using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.ShaderCompiler.Slang;

/// <summary>
/// The threads Slang compiles on: one per session (<see cref="SlangApi.MaxSessions"/>), below normal priority so a game's
/// own threads come first, made on first use. Each job is one Slang request; a job never waits for another, so callers
/// can wait for their jobs from any thread. Urgent jobs (a compile something waits for, <see cref="CompilePriority"/>)
/// run before background ones (warming), oldest first.
/// </summary>
internal static class SlangWorkers
{
	static readonly List<(Action Job, CompilePriority Priority)> queue = new();
	static int threads, waiting;

	/// <summary>Run <paramref name="job"/> on a compile thread, in the order <paramref name="priority"/> (null: urgent) puts it.</summary>
	public static Task<T> Run<T>( Func<T> job, CompilePriority priority )
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
		}, priority );
		return done.Task;
	}

	static void Enqueue( Action job, CompilePriority priority )
	{
		var start = false;
		lock ( queue )
		{
			queue.Add( (job, priority) );
			if ( waiting > 0 ) Monitor.Pulse( queue );
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
		while ( true )
		{
			Action job;
			lock ( queue )
			{
				while ( queue.Count == 0 )
				{
					waiting++;
					Monitor.Wait( queue );
					waiting--;
				}

				var next = queue.FindIndex( q => q.Priority is null || q.Priority.Urgent );
				if ( next < 0 ) next = 0;
				job = queue[next].Job;
				queue.RemoveAt( next );
			}

			job();
		}
	}
}
