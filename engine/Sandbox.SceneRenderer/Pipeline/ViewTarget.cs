using NativeEngine;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Colour/depth target with optional MSAA resolve. Defaults to RGBA16F and D24S8,
/// matching <c>CCameraRenderer::RenderToTexture</c> and <c>RenderToBitmap</c>.
/// </summary>
public sealed class ViewTarget : IDisposable
{
	readonly bool owned;

	/// <summary>
	/// Colour target, multisampled when <see cref="Samples"/> exceeds one.
	/// </summary>
	public Texture Color { get; private set; }

	/// <summary>
	/// Reverse-Z depth target matching colour size and samples. Near depth is 1.
	/// </summary>
	public Texture Depth { get; private set; }

	/// <summary>
	/// Single-sample colour for sampling or readback. Aliases <see cref="Color"/> without MSAA.
	/// </summary>
	public Texture Resolved { get; private set; }

	/// <summary>
	/// Width and height in pixels.
	/// </summary>
	public Vector2Int Size { get; }

	/// <summary>
	/// Samples per pixel: 1 without MSAA.
	/// </summary>
	public int Samples { get; }

	/// <summary>
	/// Encode linear output through an sRGB view for non-float targets.
	/// </summary>
	internal bool SrgbWrite { get; }

	/// <summary>
	/// Wrap caller-owned render targets with matching size and samples. MSAA requires a single-sample resolve
	/// texture matching colour size and format. Disposal leaves the textures owned by the caller.
	/// </summary>
	public ViewTarget( Texture color, Texture depth, Texture resolved = null ) : this( color, depth, resolved, false )
	{
	}

	ViewTarget( Texture color, Texture depth, Texture resolved, bool owned )
	{
		ArgumentNullException.ThrowIfNull( color );
		ArgumentNullException.ThrowIfNull( depth );

		if ( !depth.ImageFormat.IsDepthFormat() ) throw new ArgumentException( $"{depth.ImageFormat} isn't a depth format", nameof( depth ) );
		if ( color.Size != depth.Size ) throw new ArgumentException( "Colour and depth must be the same size", nameof( depth ) );
		if ( color.MultisampleType != depth.MultisampleType ) throw new ArgumentException( "Colour and depth must have the same sample count", nameof( depth ) );

		Samples = SampleCount( color.MultisampleType );
		if ( Samples > 1 )
		{
			ArgumentNullException.ThrowIfNull( resolved, nameof( resolved ) );
			if ( resolved.Size != color.Size || resolved.ImageFormat != color.ImageFormat || resolved.MultisampleType != RenderMultisampleType.RENDER_MULTISAMPLE_NONE )
				throw new ArgumentException( "A resolve texture must be the colour's size and format, with one sample", nameof( resolved ) );
		}

		Color = color;
		Depth = depth;
		Resolved = Samples > 1 ? resolved : color;
		Size = new Vector2Int( color.Width, color.Height );
		SrgbWrite = !IsFloatFormat( color.ImageFormat );
		this.owned = owned;
	}

	/// <summary>
	/// Create owned targets, defaulting to native scene formats.
	/// </summary>
	public static ViewTarget Create( Vector2Int size, MultisampleAmount msaa = MultisampleAmount.MultisampleNone, ImageFormat color = ImageFormat.RGBA16161616F, ImageFormat depth = ImageFormat.D24S8, string name = "SceneRenderer" )
	{
		if ( size.x < 1 || size.y < 1 ) throw new ArgumentOutOfRangeException( nameof( size ) );

		var colorTexture = Texture.CreateRenderTarget().WithSize( size.x, size.y ).WithFormat( color ).WithMSAA( msaa ).Create( $"{name} color" );
		var depthTexture = Texture.CreateRenderTarget().WithSize( size.x, size.y ).WithFormat( depth ).WithMSAA( msaa ).Create( $"{name} depth" );

		Texture resolved = null;
		if ( colorTexture.MultisampleType != RenderMultisampleType.RENDER_MULTISAMPLE_NONE )
			resolved = Texture.CreateRenderTarget().WithSize( size.x, size.y ).WithFormat( color ).Create( $"{name} resolved" );

		return new ViewTarget( colorTexture, depthTexture, resolved, true );
	}

	/// <summary>
	/// Samples per pixel for a native multisample type, native's <c>RenderMultisampleTypeNumSamples</c>.
	/// </summary>
	internal static int SampleCount( RenderMultisampleType type ) => type switch
	{
		RenderMultisampleType.RENDER_MULTISAMPLE_2X => 2,
		RenderMultisampleType.RENDER_MULTISAMPLE_4X => 4,
		RenderMultisampleType.RENDER_MULTISAMPLE_6X => 6,
		RenderMultisampleType.RENDER_MULTISAMPLE_8X => 8,
		RenderMultisampleType.RENDER_MULTISAMPLE_16X => 16,
		_ => 1,
	};

	/// <summary>
	/// Linear float formats that must bypass sRGB encoding (<c>ImageLoader::IsFloatFormat</c>).
	/// </summary>
	internal static bool IsFloatFormat( ImageFormat format ) => format is ImageFormat.RGBA16161616F or ImageFormat.RGBA32323232F
		or ImageFormat.RGB323232F or ImageFormat.RG1616F or ImageFormat.RG3232F or ImageFormat.R16F or ImageFormat.R32F;

	/// <summary>
	/// Release owned textures. The device retains resources used by in-flight frames.
	/// </summary>
	public void Dispose()
	{
		if ( !owned ) return;

		if ( Resolved != Color ) Resolved?.Dispose();
		Color?.Dispose();
		Depth?.Dispose();
		Color = Depth = Resolved = null;
	}
}
