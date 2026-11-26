namespace Sandbox;

/// <summary>Completes work awaiting the next frame before that frame's first task queue drain.</summary>
internal static class FrameStartWaiters
{
	static readonly Queue<(ulong Frame, TaskCompletionSource Completion)> waiting = new();
	static (ulong Frame, TaskCompletionSource Completion) latest;

	internal static Task Next()
	{
		ThreadSafe.AssertIsMainThread();
		var frame = Application.FrameCount;
		if ( latest.Completion is null || latest.Frame != frame )
		{
			latest = (frame, new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously ));
			waiting.Enqueue( latest );
		}

		return latest.Completion.Task;
	}

	internal static void Complete()
	{
		ThreadSafe.AssertIsMainThread();
		var frame = Application.FrameCount;
		while ( waiting.TryPeek( out var batch ) && batch.Frame != frame )
		{
			waiting.Dequeue();
			if ( latest.Completion == batch.Completion ) latest = default;
			batch.Completion.SetResult();
		}
	}
}
