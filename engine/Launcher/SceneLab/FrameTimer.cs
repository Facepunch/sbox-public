using System.Diagnostics;

namespace Sandbox.SceneLab;

/// <summary>
/// What a frame cost: the whole frame, the renderer's share of the main thread, the GPU, and the
/// managed memory the renderer allocated doing it. Collected per frame, summarised over a window.
/// </summary>
internal sealed class FrameTimer
{
	public readonly record struct Summary( int Frames, double Fps, double FrameMs, double FrameP95Ms, double FrameP99Ms, double RenderMs, double GpuMs, double AllocBytes )
	{
		public override string ToString()
			=> $"{Fps:0} FPS · frame {FrameMs:0.00} ms (p99 {FrameP99Ms:0.00}) · render {RenderMs:0.000} ms · gpu {(GpuMs > 0 ? $"{GpuMs:0.00} ms" : "n/a")} · alloc {AllocBytes:0} B";
	}

	readonly List<double> frameMs = new();
	readonly List<double> renderMs = new();
	readonly List<double> gpuMs = new();
	readonly List<double> allocBytes = new();

	long lastFrame;
	long renderStart;
	long allocStart;

	public int Count => frameMs.Count;

	public void Clear()
	{
		frameMs.Clear();
		renderMs.Clear();
		gpuMs.Clear();
		allocBytes.Clear();
		lastFrame = 0;
	}

	/// <summary>
	/// Call around the renderer's work for the frame.
	/// </summary>
	public void BeginRender()
	{
		allocStart = GC.GetAllocatedBytesForCurrentThread();
		renderStart = Stopwatch.GetTimestamp();
	}

	public void EndRender( SwapChainHandle_t swapChain )
	{
		var now = Stopwatch.GetTimestamp();
		renderMs.Add( Stopwatch.GetElapsedTime( renderStart, now ).TotalMilliseconds );
		allocBytes.Add( GC.GetAllocatedBytesForCurrentThread() - allocStart );

		// The GPU's time for this swap chain's most recently finished frame
		if ( g_pRenderDevice.GetGPUFrameTimeMS( swapChain, out var gpu, out _ ) && gpu > 0 )
			gpuMs.Add( gpu );

		// Frame to frame, so it's everything the app did - UI, present and waiting included
		if ( lastFrame != 0 ) frameMs.Add( Stopwatch.GetElapsedTime( lastFrame, now ).TotalMilliseconds );
		lastFrame = now;
	}

	public Summary Summarise()
	{
		if ( frameMs.Count == 0 ) return default;

		var sorted = frameMs.Order().ToArray();
		var mean = frameMs.Average();

		return new Summary(
			frameMs.Count,
			1000.0 / mean,
			mean,
			Percentile( sorted, 0.95 ),
			Percentile( sorted, 0.99 ),
			renderMs.Count > 0 ? renderMs.Average() : 0,
			gpuMs.Count > 0 ? gpuMs.Average() : 0,
			allocBytes.Count > 0 ? allocBytes.Average() : 0 );
	}

	static double Percentile( double[] sorted, double p ) => sorted[Math.Min( sorted.Length - 1, (int)(sorted.Length * p) )];
}
