using NativeEngine;

namespace Sandbox;

/// <summary>
/// Essentially wraps a couple of textures that we're going to render to. The color texture and the depth texture.
/// </summary>
[Expose]
public sealed partial class RenderTarget : IDisposable
{
	/// <summary>
	/// Is this currently loaned out (Active)
	/// </summary>
	internal bool Loaned { get; set; }

	/// <summary>
	/// The amount of time since this texture was last used
	/// </summary>
	internal int FramesSinceUsed { get; set; }

	/// <summary>
	/// The hash of the parameters used to create this
	/// </summary>
	internal int CreationHash { get; set; }

	/// <summary>
	/// Width of the render target
	/// </summary>
	public int Width { get; internal set; }

	/// <summary>
	/// Height of the render target
	/// </summary>
	public int Height { get; internal set; }

	/// <summary>
	/// The target colour texture
	/// </summary>
	public Texture ColorTarget { get; internal set; }

	/// <summary>
	/// The target depth texture
	/// </summary>
	public Texture DepthTarget { get; internal set; }

	// Private - Only way to get a valid render target should be with RenderTarget.From
	private RenderTarget()
	{

	}

	/// <summary>
	/// Stop using this texture, return it to the pool
	/// </summary>
	public void Dispose()
	{
		if ( !Loaned ) return;

		Return( this );
	}

	/// <summary>
	/// Destroy this buffer. It shouldn't be used anymore after this.
	/// </summary>
	internal void Destroy()
	{
		//Log.Info( $"Freeing RT {this}" );

		ColorTarget?.Dispose();
		ColorTarget = null;

		DepthTarget?.Dispose();
		DepthTarget = null;
	}

	public override string ToString()
	{
		return $"RenderTarget_{Width}x{Height}";
	}

	/// <summary>
	/// Create a render target from these textures
	/// </summary>
	public static RenderTarget From( Texture color, Texture depth = null )
	{
		if ( color is not null && !color.IsRenderTarget )
		{
			throw new ArgumentException( "Texture was not created as a render target (Texture.CreateRenderTarget)", nameof( color ) );
		}

		if ( depth is not null && !depth.IsRenderTarget )
		{
			throw new ArgumentException( "Texture was not created as a render target (Texture.CreateRenderTarget)", nameof( depth ) );
		}

		return new RenderTarget
		{
			ColorTarget = color,
			DepthTarget = depth,
			Width = color.Width,
			Height = color.Height,
		};
	}

	/// <summary>
	/// A render target of a depth texture alone, with no colour: what a shadow map or a depth prepass draws into.
	/// </summary>
	internal static RenderTarget FromDepth( Texture depth )
	{
		ArgumentNullException.ThrowIfNull( depth );
		if ( !depth.IsRenderTarget ) throw new ArgumentException( "Texture was not created as a render target (Texture.CreateRenderTarget)", nameof( depth ) );
		return new RenderTarget { DepthTarget = depth, Width = depth.Width, Height = depth.Height };
	}
}
