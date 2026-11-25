namespace Sandbox.Diagnostics;

/// <summary>
/// Managed bytes allocated by the main thread and by the render thread in each of the last <see cref="Frames"/> frames, for
/// the steady frame's zero-allocation checks (a probe reads a window of frames and leaves out those it ran in). Each
/// thread reads its own allocation counter once a frame; nothing here allocates.
/// </summary>
internal static class FrameAllocations
{
	/// <summary>How many frames each thread's ring keeps.</summary>
	internal const int Frames = 4096;

	static readonly long[] main = new long[Frames];
	static readonly ulong[] mainFrame = new ulong[Frames];
	static long mainLast = -1;

	static readonly long[] render = new long[Frames];
	static readonly ulong[] renderFrame = new ulong[Frames];
	static long renderLast = -1;

	/// <summary>Bytes the render thread has allocated in its frames so far, and the frames it has run.</summary>
	internal static long RenderTotal, RenderCount;

	/// <summary>On the main thread at a frame's start: what the frame before it (<paramref name="frame"/> - 1) allocated.</summary>
	internal static void MainFrameStart( ulong frame )
	{
		var bytes = GC.GetAllocatedBytesForCurrentThread();
		if ( mainLast >= 0 && frame > 0 )
		{
			var slot = (int)((frame - 1) % Frames);
			main[slot] = bytes - mainLast;
			mainFrame[slot] = frame - 1;
		}

		mainLast = bytes;

		// A couple of frames after the last, which the render thread may still be drawing
		if ( reportAt != 0 && frame > reportAt + 2 )
		{
			var (from, to) = (reportFrom, reportAt);
			reportAt = 0;
			Log.Info( Report( from, to ) );
		}
	}

	static ulong reportFrom, reportAt;

	/// <summary>
	/// Measure the next <paramref name="frames"/> frames (default 1000, at most <see cref="Frames"/>) and log what the main
	/// and render threads allocated in them once they've run: nothing runs in the window but the per-frame counts, so the
	/// command (or a tool that sent it) doesn't count itself.
	/// </summary>
	[ConCmd( "frame_allocations", Help = "Log the managed bytes the main and render threads allocate over the next N frames (default 1000)" )]
	internal static void Measure( int frames = 1000 )
	{
		frames = Math.Clamp( frames, 1, Frames - 64 );

		// From the frame after next: this one ran the command, and the next answers whoever sent it
		reportFrom = Application.FrameCount + 2;
		reportAt = reportFrom + (ulong)frames - 1;
	}

	/// <summary>
	/// What the main and render threads allocated in frames <paramref name="from"/> to <paramref name="to"/>: their bytes per
	/// frame, the frames that allocated anything, and the first of those (frame:main/render bytes).
	/// </summary>
	internal static string Report( ulong from, ulong to )
	{
		long mainSum = 0, renderSum = 0;
		int mainN = 0, renderN = 0, mainHit = 0, renderHit = 0;
		var hits = new System.Text.StringBuilder();
		int listed = 0;
		for ( var f = from; f <= to; f++ )
		{
			var m = Main( f );
			var r = Render( f );
			if ( m >= 0 ) { mainN++; mainSum += m; if ( m > 0 ) mainHit++; }
			if ( r >= 0 ) { renderN++; renderSum += r; if ( r > 0 ) renderHit++; }
			if ( (m > 0 || r > 0) && listed++ < 48 ) hits.Append( $" {f - from}:{Math.Max( m, 0 )}/{Math.Max( r, 0 )}" );
		}

		return $"frame allocations over {to - from + 1} frames: main {mainSum / Math.Max( 1.0, mainN ):F1} B/frame ({mainSum} B in {mainHit} of {mainN} frames), "
			+ $"render {renderSum / Math.Max( 1.0, renderN ):F1} B/frame ({renderSum} B in {renderHit} of {renderN} frames)"
			+ (hits.Length > 0 ? $"; frames with bytes (frame:main/render):{hits}" : "");
	}

	/// <summary>
	/// On the render thread after its work for a frame (drawing <paramref name="frame"/>, or 0 for a device frame start
	/// alone): what it allocated since its last.
	/// </summary>
	internal static void RenderFrameDone( ulong frame )
	{
		var bytes = GC.GetAllocatedBytesForCurrentThread();
		if ( renderLast >= 0 )
		{
			var slot = (int)(RenderCount % Frames);
			render[slot] = bytes - renderLast;
			renderFrame[slot] = frame;
			RenderTotal += bytes - renderLast;
			RenderCount++;
		}

		renderLast = bytes;
	}

	/// <summary>
	/// The main thread's bytes in frame <paramref name="frame"/>, or -1 when the ring no longer has it.
	/// </summary>
	internal static long Main( ulong frame )
	{
		var slot = (int)(frame % Frames);
		return mainFrame[slot] == frame ? main[slot] : -1;
	}

	/// <summary>
	/// The render thread's bytes in the frame that drew <paramref name="frame"/>, or -1 when the ring doesn't have it.
	/// </summary>
	internal static long Render( ulong frame )
	{
		for ( int i = 0; i < Frames; i++ )
		{
			if ( renderFrame[i] == frame ) return render[i];
		}

		return -1;
	}
}
