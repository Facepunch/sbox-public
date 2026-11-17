using NativeEngine;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox.Rendering;

/// <summary>
/// Builds the layers of native's rendering pipelines: <see cref="RenderPipeline"/> hands it each view native set up.
/// Sandbox.Rendering's <c>NativeRenderPipeline</c> is the one, installed at startup.
/// </summary>
internal interface INativeRenderPipeline
{
	/// <summary>
	/// Add every layer of a view the standard pipeline set up.
	/// </summary>
	void AddLayersToView( CRenderPipelineSetup setup );

	/// <summary>
	/// Add every layer of a 3D skybox's view.
	/// </summary>
	void AddSkybox3DLayersToView( CRenderPipelineSetup setup );

	/// <summary>
	/// A view has been submitted: whatever its layers needed while it rendered can be used again.
	/// </summary>
	void OnSceneViewSubmitted( ISceneView view );

	/// <summary>
	/// The screen-space UI layers for a stage over <paramref name="color"/>, for views outside the pipeline (the camera
	/// renderer's panel UI and platform overlay). Returns the layer that writes it.
	/// </summary>
	ISceneLayer AddUIHook( string name, Stage stage, ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle color );

	/// <summary>
	/// Make its shaders and materials, on the main thread at startup. Its layers would otherwise make them the first time
	/// they render, on a render job, where that isn't allowed.
	/// </summary>
	void WarmUp();

	/// <summary>
	/// Free what it holds, at engine shutdown.
	/// </summary>
	void Shutdown();
}

/// <summary>
/// What native's rendering pipelines (renderingpipeline_standard.cpp) call once they've set up a view's render targets and
/// attributes. The layers come from <see cref="Current"/>.
/// </summary>
internal static class RenderPipeline
{
	/// <summary>
	/// Blend screen-space UI in gamma space, like a browser: the UI shaders write sRGB-encoded colour and the blend works on
	/// that, rather than on linear colour.
	/// </summary>
	[ConVar( "ui_gamma_blend", Help = "Blend screen-space UI in gamma space like a browser" )]
	public static bool UIGammaBlend { get; set; } = true;

	[ConVar( "r_native_ssr_overlap", Help = "Overlap GTAO on async compute with SSR on graphics in the native renderer." )]
	internal static bool OverlapReflections { get; set; }

	[ConVar( "r_occlusion_culling", Help = "Skip opaque objects hidden by the depth prepass in the native renderer" )]
	internal static bool OcclusionCulling { get; set; }

	/// <summary>
	/// How occlusion culling handles aggregate fragments, in both renderers. Must match <c>CSceneViewOcclusionCull::AggregateMode_t</c>.
	/// </summary>
	internal enum AggregateOcclusion
	{
		Off,

		/// <summary>Hidden fragments are drawn with no instances.</summary>
		Empty,

		/// <summary>Hidden fragments are removed from the draw. Same as <see cref="Empty"/> without drawIndirectCount.</summary>
		Compact,
	}

	[ConVar( "r_occlusion_aggregates", Min = 0, Max = 2, Help = "Occlusion cull aggregate fragments (0: off, 1: draw hidden ones empty, 2: compact, needs drawIndirectCount)" )]
	internal static int AggregateMode { get; set; } = (int)AggregateOcclusion.Compact;

	internal static AggregateOcclusion OcclusionAggregates => (AggregateOcclusion)AggregateMode;

	/// <summary>
	/// The native cull's stats buffer. Must match <c>CSceneViewOcclusionCull::Stat_t</c>.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct OcclusionStats
	{
		public uint VisibleInstances, Instances, FinalInstances;
		public uint Fragments, HiddenFragments, Triangles, HiddenTriangles, MissedTriangles, UntestedTriangles;
	}

	internal static readonly int OcclusionStatCount = Marshal.SizeOf<OcclusionStats>() / sizeof( uint );

	/// <summary>
	/// Native occlusion culling results for a frame, summed over its views.
	/// </summary>
	internal struct OcclusionCounts
	{
		public int HiddenInstances;
		public int Fragments, HiddenFragments;
		public long Triangles, HiddenTriangles;

		/// <summary>Estimated hidden triangles in fragments that were still drawn.</summary>
		public long MissedTriangles;

		/// <summary>Triangles in fragments too close or too big to test.</summary>
		public long UntestedTriangles;

		internal void Add( in OcclusionStats stats )
		{
			HiddenInstances += (int)(stats.FinalInstances - stats.VisibleInstances);
			Fragments += (int)stats.Fragments;
			HiddenFragments += (int)stats.HiddenFragments;
			Triangles += stats.Triangles;
			HiddenTriangles += stats.HiddenTriangles;
			MissedTriangles += stats.MissedTriangles;
			UntestedTriangles += stats.UntestedTriangles;
		}
	}

	static readonly Lock occlusionLock = new();
	static readonly Stack<OcclusionFrame> freeOcclusionFrames = new();
	static OcclusionFrame occlusionFrame;
	static ulong completedOcclusionFrame;
	static OcclusionCounts occlusionCounts;

	internal static void ReadOcclusionStats( IRenderContext context, GpuBuffer stats )
	{
		AsyncGPUReadback.BufferReadDelegate callback;
		lock ( occlusionLock )
		{
			var frame = Application.FrameCount;
			if ( occlusionFrame is null || occlusionFrame.Frame + 1 < frame )
			{
				completedOcclusionFrame = frame;
				occlusionCounts = default;
			}

			if ( occlusionFrame is null || occlusionFrame.Frame != frame )
			{
				occlusionFrame?.Close();
				if ( occlusionFrame is { Pending: 0 } ) freeOcclusionFrames.Push( occlusionFrame );
				occlusionFrame = freeOcclusionFrames.TryPop( out var available ) ? available : new OcclusionFrame();
				occlusionFrame.Begin( frame );
			}

			occlusionFrame.Pending++;
			callback = occlusionFrame.OnRead;
		}

		context.ReadBufferAsync( stats, callback, 0, Marshal.SizeOf<OcclusionStats>() );
	}

	internal static int GetOcclusionCulled() => GetOcclusionCounts().HiddenInstances;

	internal static OcclusionCounts GetOcclusionCounts()
	{
		lock ( occlusionLock )
		{
			if ( occlusionFrame is { } frame && frame.Frame < Application.FrameCount ) frame.Close();

			if ( !OcclusionCulling || occlusionFrame is null || occlusionFrame.Frame + 1 < Application.FrameCount )
				return default;

			return occlusionCounts;
		}
	}

	// Close registration at the next frame, then publish once every view's readback completes.
	sealed class OcclusionFrame
	{
		public ulong Frame { get; private set; }
		public int Pending;
		public AsyncGPUReadback.BufferReadDelegate OnRead { get; }
		OcclusionCounts counts;
		bool closed;

		public OcclusionFrame() => OnRead = Read;

		public void Begin( ulong frame )
		{
			Frame = frame;
			Pending = 0;
			counts = default;
			closed = false;
		}

		public void Close()
		{
			closed = true;
			Publish();
		}

		void Read( Span<byte> bytes )
		{
			var stats = MemoryMarshal.Read<OcclusionStats>( bytes );
			lock ( occlusionLock )
			{
				counts.Add( stats );
				Pending--;
				Publish();
			}
		}

		void Publish()
		{
			if ( !closed || Pending != 0 )
				return;

			if ( Frame >= completedOcclusionFrame )
			{
				completedOcclusionFrame = Frame;
				occlusionCounts = counts;
			}

			if ( this != occlusionFrame ) freeOcclusionFrames.Push( this );
		}
	}

	/// <summary>
	/// The pipeline that adds the layers.
	/// </summary>
	internal static INativeRenderPipeline Current { get; set; }

	static bool warned;

	static INativeRenderPipeline Get()
	{
		if ( Current is null && !warned )
		{
			warned = true;
			Log.Error( "No native render pipeline is installed - views will render nothing" );
		}

		return Current;
	}

	// Native's views read Source 2's indices and buffers, also beside another device (Graphics.RecordOnSource2)
	internal static void AddLayersToView( CRenderPipelineSetup setup )
	{
		using var _ = Graphics.RecordOnSource2();
		CountToolView();
		Get()?.AddLayersToView( setup );
	}

	// Views of native's own (a tool's) drawn lately: the frame the last one was set up, not counting captures
	static ulong lastToolViewFrame;
	static int capturing;

	/// <summary>Frames after its last view that native counts as still drawing views of its own.</summary>
	const ulong ToolViewFrames = 60;

	/// <summary>
	/// Whether native has drawn views of its own in the last <see cref="ToolViewFrames"/> frames, other than captures: in a
	/// process drawing on another device with Source 2's kept (the editor), a native tool's (Hammer's) is open.
	/// </summary>
	internal static bool ToolViewsDrawn => lastToolViewFrame != 0 && Application.FrameCount - lastToolViewFrame < ToolViewFrames;

	/// <summary>Moves on each time native starts drawing views of its own again (<see cref="ToolViewsDrawn"/> turns true).</summary>
	internal static int ToolViewsStarted { get; private set; }

	/// <summary>Views set up until disposed are a capture's (a cube's faces), not a tool's.</summary>
	internal static CaptureScope Capturing()
	{
		Interlocked.Increment( ref capturing );
		return default;
	}

	internal readonly struct CaptureScope : IDisposable
	{
		public void Dispose() => Interlocked.Decrement( ref capturing );
	}

	static void CountToolView()
	{
		if ( Volatile.Read( ref capturing ) > 0 ) return;
		if ( !ToolViewsDrawn ) ToolViewsStarted++;
		lastToolViewFrame = Application.FrameCount;
	}

	internal static void AddSkybox3DLayersToView( CRenderPipelineSetup setup )
	{
		using var _ = Graphics.RecordOnSource2();
		Get()?.AddSkybox3DLayersToView( setup );
	}

	internal static void OnSceneViewSubmitted( ISceneView view )
	{
		using var _ = Graphics.RecordOnSource2();
		Current?.OnSceneViewSubmitted( view );
	}

	internal static ISceneLayer AddUIHook( string name, Stage stage, ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle color )
	{
		using var _ = Graphics.RecordOnSource2();
		return Get()?.AddUIHook( name, stage, view, viewport, color ) ?? default;
	}

	internal static void WarmUp() => Current?.WarmUp();

	internal static void Shutdown() => Current?.Shutdown();
}
