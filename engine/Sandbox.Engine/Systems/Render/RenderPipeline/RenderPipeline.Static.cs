using NativeEngine;
using System.Collections.Concurrent;

namespace Sandbox.Rendering;

internal partial class RenderPipeline
{
	static ConcurrentQueue<RenderPipeline> Pool = new();
	static ConcurrentDictionary<IntPtr, RenderPipeline> ActivePipelines = new();

	internal static void InternalAddLayersToView( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle rtColor, SceneViewRenderTargetHandle rtDepth, RenderMultisampleType nMSAA, CRenderAttributes pipelineAttrs, RenderViewport screenSize )
	{
		// Grab a pooled RenderPipeline, it just needs to be a unique one per render view
		if ( !Pool.TryDequeue( out var renderPipeline ) )
			renderPipeline = new();

		ActivePipelines.TryAdd( view.self, renderPipeline );
		renderPipeline.AddLayersToView( view, viewport, rtColor, rtDepth, nMSAA, pipelineAttrs, screenSize );
	}

	// Invoked after native side is done adding layers
	// This can be moved to AddLayersToView once the entire pipeline is in C#
	internal static void InternalPipelineEnd( ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle rtColor, SceneViewRenderTargetHandle rtDepth, RenderMultisampleType nMSAA, CRenderAttributes pipelineAttrs, RenderViewport screenSize )
	{
		// At this point a pipeline for this view should already exist, grab it
		if ( !ActivePipelines.TryGetValue( view.self, out var renderPipeline ) )
			return;

		renderPipeline.PipelineEnd( view, viewport, rtColor, rtDepth, nMSAA, pipelineAttrs, screenSize );
	}

	/// <summary>
	/// Called once a view has been submitted, which means the entire render pipeline has executed and we don't need the object anymore.
	/// </summary>
	internal static void OnSceneViewSubmitted( ISceneView view )
	{
		// Try, it's okay if it doesn't have anything because it could be a non pipeline (dependent) view
		if ( !ActivePipelines.TryRemove( view.self, out var renderPipeline ) )
		{
			return;
		}

		// Return to pool
		Pool.Enqueue( renderPipeline );
	}

	internal static void ClearPool()
	{
		Pool.Clear();
		ActivePipelines.Clear();
	}
}
