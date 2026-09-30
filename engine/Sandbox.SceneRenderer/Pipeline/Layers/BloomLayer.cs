using Sandbox.Rendering;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Draws bloom-flagged objects into quarter-resolution colour and scene depth (<c>RenderPipeline.AddLayersToView</c>).
/// Excludes faded draws and translucent draws of mixed objects. Runs before <c>AfterDepthPrepass</c> only when needed;
/// otherwise the bloom input is black. Each pass needs quarter-resolution view constants for light clustering.
/// </summary>
internal sealed class BloomLayer : MeshLayer
{
	static readonly StringToken ForwardMode = new( "Forward" );
	static readonly StringToken BloomInput = new( "QuarterResEffectsBloomInputTexture" );

	/// <summary>
	/// Pooled target for this frame.
	/// </summary>
	internal RenderTarget Target { get; private set; }

	/// <summary>
	/// Quarter-resolution viewport in <see cref="Target"/>.
	/// </summary>
	internal Rect Viewport { get; private set; }

	/// <summary>
	/// Graphics view for the depth downsample blit.
	/// </summary>
	internal Graphics.ManagedView GraphicsView { get; private set; }

	public BloomLayer() : base( "Bloom Layer" )
	{
		ShaderMode = ForwardMode;
		Runs = MeshRuns.Bloom;
		LayerType = SceneLayerType.Opaque;
	}

	public override bool IsShared => false;

	public override bool IsNeeded( RenderFrame frame ) => frame.System.DepthPrepass && frame.RunCount( this ) > 0;

	public override FrameResources Writes => FrameResources.BloomObjects;

	// Its objects test against the quarter-resolution depth
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.QuarterDepth;

	/// <summary>
	/// Acquire and bind the bloom target before recording.
	/// </summary>
	public override void BeforeSegments( RenderFrame frame, RenderContext frameContext )
	{
		var viewport = frame.View.Viewport;
		Viewport = new Rect( (int)(viewport.Left / 4), (int)(viewport.Top / 4), (int)(viewport.Width / 4), (int)(viewport.Height / 4) );
		Target = Rendering.BloomLayer.GetTarget( (int)Viewport.Width, (int)Viewport.Height );
		frameContext.Attributes.Set( BloomInput, Target.ColorTarget );

		GraphicsView ??= new Graphics.ManagedView();
		GraphicsView.Color = Target.ColorTarget.native;
		GraphicsView.Depth = Target.DepthTarget.native;
		GraphicsView.SrgbWrite = false;
		GraphicsView.Viewport = Viewport;
		GraphicsView.MinZ = frame.DepthMin;
		GraphicsView.MaxZ = frame.DepthMax;
		GraphicsView.ColorFormat = ImageFormat.RGBA1010102;
		GraphicsView.Msaa = MultisampleAmount.MultisampleNone;
		GraphicsView.LayerType = SceneLayerType.Opaque;
		GraphicsView.ShaderMode = ForwardMode;
	}

	/// <summary>
	/// Single-sample target with quarter-resolution viewport constants.
	/// </summary>
	internal StageTarget TargetFor( RenderFrame frame, RenderContext rc ) => new()
	{
		Context = rc.Native,
		Attributes = rc.Attributes,
		Color = Target.ColorTarget.native,
		Depth = Target.DepthTarget.native,
		HdrColor = Target.ColorTarget.native,
		Size = new Vector2( Target.Width, Target.Height ),
		Viewport = Viewport,
		MinZ = frame.DepthMin,
		MaxZ = frame.DepthMax,
		ColorFormat = ImageFormat.RGBA1010102,
		Samples = 1,
	};

	/// <summary>
	/// Clear colour to transparent black, preserving downsampled depth.
	/// </summary>
	public override void Begin( RenderFrame frame, RenderContext rc, ViewPass view, bool start )
	{
		frame.BeginPass( rc, TargetFor( frame, rc ) );
		if ( start ) rc.Clear( Color.Transparent, clearColor: true, clearDepth: false );
	}

	/// <summary>
	/// Release the frame's target reference.
	/// </summary>
	internal void EndFrame() => Target = null;

	public override void Dispose()
	{
		GraphicsView?.Dispose();
		GraphicsView = null;
	}
}

/// <summary>
/// Downsamples scene depth for bloom occlusion using <c>QuarterDepthDownsampleLayer.Render</c>.
/// </summary>
internal sealed class QuarterDepthDownsampleLayer : RenderLayer
{
	readonly BloomLayer bloom;

	public QuarterDepthDownsampleLayer( BloomLayer bloom ) : base( "Quarter Depth Downsample" )
	{
		this.bloom = bloom;
	}

	public override bool IsNeeded( RenderFrame frame ) => bloom.IsNeeded( frame );

	public override FrameResources Writes => FrameResources.QuarterDepth;

	/// <summary>
	/// Clear and downsample depth using the bloom viewport's constants.
	/// </summary>
	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		frame.BeginPass( rc, bloom.TargetFor( frame, rc ) );
		rc.Clear( Color.Transparent, clearColor: false, clearDepth: true );

		var hdr = frame.Output.Target;
		rc.DownsampleQuarterDepth( bloom.GraphicsView, hdr.Depth, hdr.Samples > 1 );
	}
}

/// <summary>
/// Blurs bloom mips using <c>BloomDownsampleLayer.Render</c>; no view constants required.
/// </summary>
internal sealed class BloomBlurLayer : RenderLayer
{
	readonly BloomLayer bloom;

	public BloomBlurLayer( BloomLayer bloom ) : base( "Bloom Layer Gaussian Blur" )
	{
		this.bloom = bloom;
	}

	public override bool IsNeeded( RenderFrame frame ) => bloom.IsNeeded( frame );

	public override FrameResources Writes => FrameResources.BloomInput;

	public override FrameResources Reads( RenderFrame frame ) => FrameResources.BloomObjects;

	public override void Record( RenderFrame frame, RenderContext rc, ref RenderStats stats )
	{
		rc.BlurMips( bloom.Target.ColorTarget );
		bloom.EndFrame();
	}
}
