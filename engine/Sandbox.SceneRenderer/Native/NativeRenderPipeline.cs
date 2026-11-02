using NativeEngine;
using Sandbox.Rendering;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// The layers of native's rendering pipelines. Native sets up each view's render targets and attributes
/// (renderingpipeline_standard.cpp) and hands it here, through <see cref="RenderPipeline"/>, to add every layer:
/// <see cref="StandardPipeline"/> for cameras, <see cref="Skybox3DPipeline"/> for the 3D skybox views they embed.
/// A pipeline's procedural layers keep their view's state until it renders, so each view borrows one from a pool and
/// gives it back once it's submitted.
/// </summary>
internal sealed class NativeRenderPipeline : INativeRenderPipeline
{
	/// <summary>
	/// No render target (<c>SCENE_VIEW_RENDER_TARGET_INVALID</c>).
	/// </summary>
	public static readonly SceneViewRenderTargetHandle NoTarget = -1;

	/// <summary>
	/// A layer attribute bound to a render target the layer reads (<c>SL_RENDER_TARGET_INPUT</c>).
	/// </summary>
	public const uint RenderTargetInput = 1;

	/// <summary>
	/// A layer attribute bound to a render target the layer writes outside its outputs, like a UAV (<c>SL_RENDER_TARGET_OUTPUT</c>).
	/// </summary>
	public const uint RenderTargetOutput = 2;

	readonly ConcurrentQueue<StandardPipeline> standardPool = new();
	readonly ConcurrentQueue<Skybox3DPipeline> skyboxPool = new();
	readonly ConcurrentDictionary<IntPtr, object> active = new();

	public void AddLayersToView( CRenderPipelineSetup setup )
	{
		if ( !standardPool.TryDequeue( out var pipeline ) ) pipeline = new();

		active.TryAdd( setup.m_pView.self, pipeline );
		pipeline.AddLayersToView( setup );
	}

	public void AddSkybox3DLayersToView( CRenderPipelineSetup setup )
	{
		if ( !skyboxPool.TryDequeue( out var pipeline ) ) pipeline = new();

		active.TryAdd( setup.m_pView.self, pipeline );
		pipeline.AddLayersToView( setup );
	}

	public void OnSceneViewSubmitted( ISceneView view )
	{
		// Not every view is a pipeline's (shadow and other dependent views aren't)
		if ( !active.TryRemove( view.self, out var pipeline ) ) return;

		switch ( pipeline )
		{
			case StandardPipeline standard:
				standard.OnSubmitted( view );
				standardPool.Enqueue( standard );
				break;
			case Skybox3DPipeline skybox:
				skyboxPool.Enqueue( skybox );
				break;
		}
	}

	public ISceneLayer AddUIHook( string name, Stage stage, ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle color )
		=> HookLayers.AddUI( view, name, stage, viewport, color );

	public void Shutdown()
	{
		foreach ( var pipeline in standardPool ) pipeline.Dispose();

		standardPool.Clear();
		skyboxPool.Clear();
		active.Clear();
	}

	/// <summary>
	/// Make the pipelines' shaders and materials now, on the main thread. They're static, and their layers would otherwise
	/// make them the first time they render, on a render job, where that throws.
	/// </summary>
	public void WarmUp()
	{
		RuntimeHelpers.RunClassConstructor( typeof( SharedPasses ).TypeHandle );

		foreach ( var type in typeof( NativeRenderPipeline ).Assembly.GetTypes() )
		{
			if ( type.Namespace == typeof( NativeRenderPipeline ).Namespace )
				RuntimeHelpers.RunClassConstructor( type.TypeHandle );
		}
	}
}
