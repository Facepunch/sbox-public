namespace Borealis;

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
		using var zone = Zones.Stage.Start();
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

	public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain | FrameResources.IdsNormals;
	public override FrameResources Writes => Queue == StageQueue.AsyncCompute ? FrameResources.AmbientOcclusion : FrameResources.Reflections;
	public override bool SupportsAsyncCompute => Queue == StageQueue.AsyncCompute;

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

/// <summary>
/// Copies the finished frame mirrored into its destination (<see cref="RenderOutput.FlipInto"/>), as native's texture render
/// blits a flipped view (<c>CCameraRenderer::RenderToTexture</c>, <c>copytexture.shader</c>'s <c>FlipX</c> and <c>FlipY</c>).
/// Depth stays as drawn.
/// </summary>
internal sealed class OutputFlipLayer : RenderLayer
{
	static readonly StringToken ForwardMode = new( "Forward" );
	static readonly StringToken SourceName = new( "CustomTexture1" );
	static readonly StringToken FlipXName = new( "FlipX" );
	static readonly StringToken FlipYName = new( "FlipY" );
	static readonly StringToken MsaaCombo = new( "D_MSAA" );
	static readonly StringToken MsaaColorCombo = new( "D_MSAA_COLOR" );
	static readonly StringToken VolumeCombo = new( "D_VOLUME_TEXTURE" );
	static readonly StringToken SrgbTransferCombo = new( "D_SRGB_TRANSFER" );

	static GpuMaterial material;
	static GpuDevice materialDevice;

	public OutputFlipLayer() : base( "Output Flip" ) { }

	/// <summary>
	/// Make the copy's material on the device rendering, on the main thread before any frame records
	/// (<see cref="RenderSystem.WarmUp"/>): the engine makes no materials while a frame draws, and a flipped child view (a
	/// reflection) records inside its parent's frame.
	/// </summary>
	internal static void WarmUp()
	{
		var device = RenderHost.Current.Device;
		if ( material is not null && materialDevice == device ) return;

		material = device.MaterialFromShader( "shaders/copytexture.shader" );
		materialDevice = device;
	}

	public override bool IsNeeded( RenderFrame frame ) => frame.Output.FlipInto is not null;

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		var output = frame.Output;
		var into = output.FlipInto;
		var source = output.Texture ?? output.Target.Resolved;

		// The bytes as they are: the shader reads without sRGB, so it writes without it too
		var size = new Vector2( into.Width, into.Height );
		rc.Bind( new StageTarget
		{
			Context = rc,
			Attributes = rc.Attributes,
			Color = into,
			Size = size,
			Viewport = new Rect( 0, 0, size.x, size.y ),
			MaxZ = 1,
			ColorFormat = into.Format,
			Samples = 1,
		} );

		var parameters = ParameterPool.Get();
		parameters.Set( SourceName, source );
		parameters.Set( FlipXName, output.FlipX );
		parameters.Set( FlipYName, output.FlipY );
		parameters.SetCombo( MsaaCombo, 0 );
		parameters.SetCombo( MsaaColorCombo, 0 );
		parameters.SetCombo( VolumeCombo, 0 );
		parameters.SetCombo( SrgbTransferCombo, 0 );
		rc.DrawFullscreen( material, parameters, ForwardMode );
		ParameterPool.Return( parameters );
	}
}
