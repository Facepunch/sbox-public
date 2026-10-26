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

	public StageLayer( Stage stage, bool color = true, bool final = false ) : base( ScopeName( stage ) )
	{
		Stage = stage;
		Color = color;
		Final = final;
	}

	internal static string ScopeName( Stage stage ) => stage switch
	{
		Stage.AfterViewmodel => "Managed: AfterViewModel",
		Stage.BeforePostProcess => "Managed: BeforePostProcessing",
		Stage.AfterPostProcess => "Managed: AfterPostProcessing",
		_ => $"Managed: {stage}"
	};

	public override bool IsNeeded( RenderFrame frame ) => frame.Stages is not null;

	// A camera's command lists and effects read what native's hook layers could: the depth chain (Depth::Get), and bloom's
	// objects (the bloom effect)
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain | FrameResources.BloomObjects
		| FrameResources.AmbientOcclusion | FrameResources.Reflections | FrameResources.ContactShadows | FrameResources.Untracked;

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		frame.RunStage( rc, Stage, Color, Final );
	}
}

/// <summary>
/// Independent prepass effects, recorded ahead of ordinary camera hooks. Async effects run on compute; graphics effects
/// wait only for the depth chain, so SSR can overlap AO. Serial frames leave both at their original stage.
/// </summary>
internal sealed class EarlyStageLayer : RenderLayer
{
	public Stage Stage { get; }
	public PostProcessQueue Queue { get; }

	public EarlyStageLayer( Stage stage, PostProcessQueue queue )
		: base( $"{StageLayer.ScopeName( stage )} ({(queue == PostProcessQueue.AsyncCompute ? "async compute" : "early graphics")})" )
	{
		Stage = stage;
		Queue = queue;
	}

	public override bool IsNeeded( RenderFrame frame ) => ManagedSceneRendering.AsyncCompute && frame.Stages is not null && frame.Stages.HasEarlyPostProcess( Stage, Queue );

	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain | FrameResources.Normals;
	public override FrameResources Writes => Queue == PostProcessQueue.AsyncCompute ? FrameResources.AmbientOcclusion : FrameResources.Reflections;
	public override bool SupportsAsyncCompute => Queue == PostProcessQueue.AsyncCompute;

	public override void HandOff( RenderFrame frame, RenderContext rc )
	{
		if ( frame.NormalsGBuffer is { } gbuffer ) rc.BarrierColorToComputeRead( gbuffer );
	}

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		if ( Queue == PostProcessQueue.AsyncCompute ? rc.AsyncCompute : frame.AsyncCompute )
			frame.RunEarlyPostProcess( rc, Stage, Queue );
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
