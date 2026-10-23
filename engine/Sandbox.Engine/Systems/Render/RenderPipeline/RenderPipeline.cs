using NativeEngine;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox.Rendering;

/// <summary>
/// Builds the layers of native's rendering pipelines: <see cref="RenderPipeline"/> hands it each view native set up.
/// Sandbox.SceneRenderer's <c>NativeRenderPipeline</c> is the one, installed at startup.
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
	internal static bool OcclusionCulling { get; set; } = true;

	static readonly Lock occlusionLock = new();
	static readonly Stack<OcclusionFrame> freeOcclusionFrames = new();
	static OcclusionFrame occlusionFrame;
	static ulong completedOcclusionFrame;
	static int occlusionCulled;

	internal static void ReadOcclusionCulled( IRenderContext context, GpuBuffer stats )
	{
		AsyncGPUReadback.BufferReadDelegate callback;
		lock ( occlusionLock )
		{
			var frame = Application.FrameCount;
			if ( occlusionFrame is null || occlusionFrame.Frame + 1 < frame )
			{
				completedOcclusionFrame = frame;
				occlusionCulled = 0;
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

		context.ReadBufferAsync( stats, callback, 0, 3 * sizeof( uint ) );
	}

	internal static int GetOcclusionCulled()
	{
		lock ( occlusionLock )
		{
			if ( occlusionFrame is { } frame && frame.Frame < Application.FrameCount ) frame.Close();

			if ( !OcclusionCulling || occlusionFrame is null || occlusionFrame.Frame + 1 < Application.FrameCount )
				return 0;

			return occlusionCulled;
		}
	}

	// Close registration at the next frame, then publish once every view's readback completes.
	sealed class OcclusionFrame
	{
		public ulong Frame { get; private set; }
		public int Pending;
		public AsyncGPUReadback.BufferReadDelegate OnRead { get; }
		int culled;
		bool closed;

		public OcclusionFrame() => OnRead = Read;

		public void Begin( ulong frame )
		{
			Frame = frame;
			Pending = 0;
			culled = 0;
			closed = false;
		}

		public void Close()
		{
			closed = true;
			Publish();
		}

		void Read( Span<byte> bytes )
		{
			// Visible instances, recorded instances, instances recorded for the final phase.
			var counts = MemoryMarshal.Cast<byte, uint>( bytes );
			lock ( occlusionLock )
			{
				culled += (int)(counts[2] - counts[0]);
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
				occlusionCulled = culled;
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

	internal static void AddLayersToView( CRenderPipelineSetup setup ) => Get()?.AddLayersToView( setup );

	internal static void AddSkybox3DLayersToView( CRenderPipelineSetup setup ) => Get()?.AddSkybox3DLayersToView( setup );

	internal static void OnSceneViewSubmitted( ISceneView view ) => Current?.OnSceneViewSubmitted( view );

	internal static ISceneLayer AddUIHook( string name, Stage stage, ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle color )
		=> Get()?.AddUIHook( name, stage, view, viewport, color ) ?? default;

	internal static void WarmUp() => Current?.WarmUp();

	internal static void Shutdown() => Current?.Shutdown();
}
