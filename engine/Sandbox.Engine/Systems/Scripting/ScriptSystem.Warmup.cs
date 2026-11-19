using System.Threading;
using System.Threading.Tasks;

namespace Sandbox;

public sealed partial class ScriptSystem
{
	/// <summary>
	/// Overlaps immutable method compilation with scene setup. The worker executes no script or resolver
	/// code; ordinary callbacks can still compile on demand. One shared worker bounds background CPU use.
	/// </summary>
	private void WarmMethods()
	{
		var warming = Module.WarmupAsync();
		if ( warming.IsCompletedSuccessfully || warming.IsCanceled )
		{
			return;
		}

		// This observer retains no game context, instance or generation. Normal execution still reports
		// the original compilation failure if the affected method is called.
		using ( ExecutionContext.SuppressFlow() )
		{
			_ = warming.ContinueWith( static task => Log.Warning( $"Script method warmup failed: {task.Exception}" ),
				CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default );
		}
	}
}
