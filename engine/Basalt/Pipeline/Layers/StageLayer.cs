using Sandbox.Rendering;

namespace Basalt;

/// <summary>
/// Runs <see cref="IRenderStages"/> at a native hook point, then restores frame targets.
/// </summary>
internal sealed class StageLayer : RenderLayer
{
	public RenderStage Stage { get; }

	/// <summary>
	/// Include colour; the post-prepass stage receives depth only.
	/// </summary>
	public bool Color { get; }

	/// <summary>
	/// Draw into the final output instead of HDR.
	/// </summary>
	public bool Final { get; }

	public StageLayer( RenderStage stage, bool color = true, bool final = false ) : base( ScopeName( stage ) )
	{
		Stage = stage;
		Color = color;
		Final = final;
	}

	internal static string ScopeName( RenderStage stage ) => stage switch
	{
		RenderStage.AfterViewmodel => "Managed: AfterViewModel",
		RenderStage.BeforePostProcess => "Managed: BeforePostProcessing",
		RenderStage.AfterPostProcess => "Managed: AfterPostProcessing",
		_ => $"Managed: {stage}"
	};

	public override bool IsNeeded( RenderFrame frame ) => frame.Stages is not null;

	// A camera's command lists and effects read what native's hook layers could: the depth chain (Depth::Get), and bloom's
	// objects (the bloom effect)
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain | FrameResources.BloomObjects
		| FrameResources.AmbientOcclusion | FrameResources.Reflections | FrameResources.ContactShadows | FrameResources.Untracked;

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
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
	public RenderStage Stage { get; }
	public StageQueue Queue { get; }

	public EarlyStageLayer( RenderStage stage, StageQueue queue )
		: base( $"{StageLayer.ScopeName( stage )} ({(queue == StageQueue.AsyncCompute ? "async compute" : "early graphics")})" )
	{
		Stage = stage;
		Queue = queue;
	}

	public override bool IsNeeded( RenderFrame frame ) => RenderHost.Current.AsyncCompute && frame.Stages is not null && frame.Stages.HasEarlyPostProcess( Stage, Queue );

	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain | FrameResources.Normals;
	public override FrameResources Writes => Queue == StageQueue.AsyncCompute ? FrameResources.AmbientOcclusion : FrameResources.Reflections;
	public override bool SupportsAsyncCompute => Queue == StageQueue.AsyncCompute;

	public override void HandOff( RenderFrame frame, GpuContext rc )
	{
		if ( frame.NormalsGBuffer is { } gbuffer ) rc.BarrierColorToComputeRead( gbuffer );
	}

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		if ( Queue == StageQueue.AsyncCompute ? rc.AsyncCompute : frame.AsyncCompute )
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
	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
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

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		rc.ResolveScratch( frame.Output );
	}
}
