namespace Borealis;

/// <summary>
/// Colour/depth target with optional MSAA resolve. Defaults to RGBA16F and D24S8, matching
/// <c>CCameraRenderer::RenderToTexture</c> and <c>RenderToBitmap</c>.
/// </summary>
public sealed class ViewTarget : IDisposable
{
	readonly bool owned;

	/// <summary>
	/// Colour target, multisampled when <see cref="Samples"/> exceeds one.
	/// </summary>
	public GpuTexture Color { get; private set; }

	/// <summary>
	/// Reverse-Z depth target matching colour size and samples. Near depth is 1.
	/// </summary>
	public GpuTexture Depth { get; private set; }

	/// <summary>
	/// Single-sample colour for sampling or readback. Aliases <see cref="Color"/> without MSAA.
	/// </summary>
	public GpuTexture Resolved { get; private set; }

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
	public ViewTarget( GpuTexture color, GpuTexture depth, GpuTexture resolved = null ) : this( color, depth, resolved, false )
	{
	}

	ViewTarget( GpuTexture color, GpuTexture depth, GpuTexture resolved, bool owned )
	{
		ArgumentNullException.ThrowIfNull( color );
		ArgumentNullException.ThrowIfNull( depth );

		if ( !depth.Format.IsDepth() ) throw new ArgumentException( $"{depth.Format} isn't a depth format", nameof( depth ) );
		if ( color.Size != depth.Size ) throw new ArgumentException( "Colour and depth must be the same size", nameof( depth ) );
		if ( color.Samples != depth.Samples ) throw new ArgumentException( "Colour and depth must have the same sample count", nameof( depth ) );

		Samples = color.Samples;
		if ( Samples > 1 )
		{
			ArgumentNullException.ThrowIfNull( resolved, nameof( resolved ) );
			if ( resolved.Size != color.Size || resolved.Format != color.Format || resolved.Samples != 1 )
				throw new ArgumentException( "A resolve texture must be the colour's size and format, with one sample", nameof( resolved ) );
		}

		Color = color;
		Depth = depth;
		Resolved = Samples > 1 ? resolved : color;
		Size = color.Size;
		SrgbWrite = !color.Format.IsFloat();
		this.owned = owned;
	}

	/// <summary>
	/// Create owned targets, defaulting to native scene formats.
	/// </summary>
	public static ViewTarget Create( Vector2Int size, int samples = 1, TextureFormat color = TextureFormat.RGBA16161616F, TextureFormat depth = TextureFormat.D24S8, string name = "Borealis" )
	{
		if ( size.x < 1 || size.y < 1 ) throw new ArgumentOutOfRangeException( nameof( size ) );

		var device = RenderHost.Current.Device;
		var colorTexture = device.CreateTexture( TextureDesc.Target( size.x, size.y, color, samples ), $"{name} color" );
		var depthTexture = device.CreateTexture( TextureDesc.Target( size.x, size.y, depth, samples ), $"{name} depth" );

		GpuTexture resolved = null;
		if ( colorTexture.Samples > 1 )
			resolved = device.CreateTexture( TextureDesc.Target( size.x, size.y, color ), $"{name} resolved" );

		return new ViewTarget( colorTexture, depthTexture, resolved, true );
	}

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
