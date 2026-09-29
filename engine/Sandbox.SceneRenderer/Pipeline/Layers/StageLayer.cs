using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Runs <see cref="IRenderStages"/> at a native hook point, then restores frame targets.
/// </summary>
internal sealed class StageLayer : RenderLayer
{
	public Stage Stage { get; }

	/// <summary>
	/// Include colour; the post-prepass stage receives depth only.
	/// </summary>
	public bool Color { get; }

	/// <summary>
	/// Draw into the final output instead of HDR.
	/// </summary>
	public bool Final { get; }

	public StageLayer( Stage stage, bool color = true, bool final = false ) : base( $"Managed: {stage}" )
	{
		Stage = stage;
		Color = color;
		Final = final;
	}

	public override bool IsNeeded( RenderFrame frame ) => frame.Stages is not null;

	// A camera's command lists and effects read what native's hook layers could: the depth chain (Depth::Get), and bloom's
	// input (the bloom effect)
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain | FrameResources.BloomInput;

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		frame.RunStage( rc, Stage, Color, Final );
	}
}

/// <summary>
/// A stage's compute-only effects (<c>BasePostProcess.AsyncCompute</c>: AO at <c>AfterDepthPrepass</c>), ahead of the stage on the
/// async compute queue, beside the shadow maps. Only with <c>r_managed_async_compute</c>; recorded on the graphics queue (a frame
/// too small to record in parallel) it does nothing, and the stage runs them where it always has.
/// </summary>
internal sealed class AsyncStageLayer : RenderLayer
{
	public Stage Stage { get; }

	public AsyncStageLayer( Stage stage ) : base( $"Managed: {stage} (async compute)" )
	{
		Stage = stage;
	}

	public override bool IsNeeded( RenderFrame frame ) => ManagedSceneRendering.AsyncCompute && frame.Stages is not null && frame.Stages.HasAsyncCompute( Stage );

	// GTAO reads the depth chain
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain;

	public override bool AsyncCompute => true;

	/// <summary>
	/// The normals G-buffer the prepass drew, made readable by compute on the graphics queue.
	/// </summary>
	public override void HandOff( RenderFrame frame, RenderContext rc )
	{
		if ( frame.NormalsGBuffer is { } gbuffer ) rc.BarrierColorToComputeRead( gbuffer );
	}

	// The effects' own barriers leave their results readable, and the segment that joins waits in every stage
	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		if ( rc.AsyncCompute ) frame.RunAsyncCompute( rc, Stage );
	}
}

/// <summary>
/// Resolves MSAA and copies to the output before <c>AfterPostProcess</c> and UI stages.
/// </summary>
internal sealed class OutputLayer : RenderLayer
{
	public OutputLayer() : base( "Output" ) { }

	/// <summary>
	/// Bind, resolve and copy the frame.
	/// </summary>
	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		frame.BindFrame( rc );
		rc.Resolve( frame.Output );
		rc.CopyToSwapChain( frame.Output, frame.View.Viewport );
	}
}

/// <summary>
/// Resolves the output's MSAA scratch after all drawing.
/// </summary>
internal sealed class OutputResolveLayer : RenderLayer
{
	public OutputResolveLayer() : base( "Output Resolve" ) { }

	public override bool IsNeeded( RenderFrame frame ) => frame.Output.Scratch is not null;

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		rc.ResolveScratch( frame.Output );
	}
}
