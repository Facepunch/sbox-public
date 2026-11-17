using System.Threading;

namespace Borealis;

/// <summary>
/// The renderer's managed allocations, which in steady state should be none: bytes allocated on a thread while it does
/// the renderer's work (a camera's frame on the main thread, a recording segment or culling job on a worker), counted
/// with <see cref="GC.GetAllocatedBytesForCurrentThread"/>. Scopes nest on a thread and only the outermost counts, so a
/// child view inside a camera's frame isn't counted twice. <see cref="TakeFrame"/> reads and resets the total, once a frame.
/// </summary>
public static class RendererAllocations
{
	[ThreadStatic] static int depth;
	[ThreadStatic] static long start;
	static long total;

	/// <summary>Count what this thread allocates until the scope is disposed.</summary>
	public static Scope Measure()
	{
		if ( depth++ == 0 ) start = GC.GetAllocatedBytesForCurrentThread();
		return default;
	}

	/// <summary>Bytes counted since the last call, on every thread; resets the count.</summary>
	public static long TakeFrame() => Interlocked.Exchange( ref total, 0 );

	/// <summary>Bytes counted since the last <see cref="TakeFrame"/>, without resetting.</summary>
	public static long Pending => Interlocked.Read( ref total );

	static void Exit()
	{
		if ( --depth == 0 ) Interlocked.Add( ref total, GC.GetAllocatedBytesForCurrentThread() - start );
	}

	/// <summary>A measured stretch of this thread's work.</summary>
	public readonly struct Scope : IDisposable
	{
		/// <summary>Stop counting this stretch.</summary>
		public void Dispose() => Exit();
	}
}
