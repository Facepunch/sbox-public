using Basalt;
using Basalt.Backend;
using GpuViewConstants = Basalt.Gpu.ViewConstants;

namespace Sandbox;

/// <summary>
/// A finished Basalt frame on a Basalt.Gpu device, handed to the engine's drawing on that device with no copy: its colour
/// and depth targets as engine <see cref="Texture"/>s whose backings are the frame's own, and what a camera's view gives
/// the drawing after the scene - the frame's view constants, the blue noise texture and bloom's quarter-size input.
/// What a camera frame on Basalt.Gpu is after the scene: post processing and the camera's later stages draw in a block
/// from <see cref="Begin()"/>, which also builds the view's depth chain from the frame's depth, then the frame is presented.
/// </summary>
/// <remarks>
/// The textures keep their engine-side identity from frame to frame; <see cref="Adopt"/> points them at the targets the
/// frame drew into. Layouts are handed over at both ends: the block starts where Basalt left each target, and Basalt's
/// next frame starts where the block left it.
/// </remarks>
internal sealed class BasaltGpuFrame : IDisposable
{
	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );
	static readonly StringToken DepthChainName = new( "DepthChainDownsample" );
	static readonly StringToken DepthChainPrevFrameName = new( "DepthChainDownsamplePrevFrame" );

	readonly BasaltGpuRenderBackend backend;
	ViewTarget target;
	byte[] constants = [];

	/// <summary>The frame's colour target (its resolved one when multisampled).</summary>
	public Texture Color { get; private set; }

	/// <summary>The frame's depth target.</summary>
	public Texture Depth { get; private set; }

	/// <summary>
	/// The view's depth chain (<c>DepthChainDownsample</c>, which <c>Depth::Get</c> reads): the min and max of the frame's
	/// depth, mipped, built by <see cref="Begin()"/>. Null for a frame with no depth.
	/// </summary>
	public Texture DepthChain { get; private set; }

	/// <summary>The view constants the frame drew with, as <c>PerViewConstantBuffer_t</c>'s bytes.</summary>
	public byte[] Constants => constants;

	public BasaltGpuFrame( BasaltGpuRenderBackend backend ) => this.backend = backend;

	/// <summary>
	/// Take a frame Basalt rendered into <paramref name="target"/> on the backend's device, with the view constants it drew
	/// with.
	/// </summary>
	public void Adopt( ViewTarget target, in GpuViewConstants viewConstants )
	{
		this.target = target;
		var color = target.Resolved ?? target.Color;
		Color = Engine( Color, color, target.Size, ImageFormat.RGBA16161616F, depth: false );
		Depth = target.Depth is null ? null : Engine( Depth, target.Depth, target.Size, ImageFormat.D32FS8, depth: true );

		constants = new byte[System.Runtime.CompilerServices.Unsafe.SizeOf<GpuViewConstants>()];
		System.Runtime.InteropServices.MemoryMarshal.Write( constants, in viewConstants );
	}

	/// <summary>An engine texture of the target's size whose backing on the device is <paramref name="gpu"/>.</summary>
	Texture Engine( Texture existing, GpuTexture gpu, Vector2Int size, ImageFormat format, bool depth )
	{
		if ( existing is null || existing.Width != size.x || existing.Height != size.y )
		{
			existing?.Dispose();
			var builder = Texture.CreateRenderTarget().WithSize( size.x, size.y );
			builder = depth ? builder.WithDepthFormat() : builder.WithFormat( format );
			existing = builder.Create( depth ? "Basalt.Gpu frame depth" : "Basalt.Gpu frame colour" );
		}

		var (handle, layout) = BackendDevice.Unwrap( gpu );
		backend.Borrow( existing, handle, layout );
		return existing;
	}

	/// <summary>
	/// A render block on the frame: its targets bound, its depth chain built, and what the camera's view gives its drawing
	/// set on the block. Dispose it to submit; then call <see cref="End"/>.
	/// </summary>
	public Graphics.Scope Begin() => Begin( null );

	/// <summary>
	/// A render block on the frame as <see cref="Begin()"/>'s, which a camera's stages draw in: <paramref name="view"/> is
	/// what they ask about the frame, and drawing into a render target of their own goes back to the frame's targets after.
	/// </summary>
	public Graphics.Scope Begin( BasaltGpuView view )
	{
		var scope = Graphics.Scope.CreateOnView( backend, view );
		try
		{
			if ( view is null )
			{
				Graphics.RenderTarget = RenderTarget.From( Color, Depth );
			}
			else
			{
				view.Target = RenderTarget.From( Color, Depth );
				view.Bind();
			}

			var attributes = Graphics.Attributes;
			attributes.SetData( ViewConstantsName, constants.AsSpan() );

			// What reads the view's depth through Depth::Get; the previous frame's aliases it, as Basalt's does
			if ( Depth is not null )
			{
				backend.BuildDepthChain( Depth, DepthChain = Chain( DepthChain, Depth.Width, Depth.Height ) );
				attributes.Set( DepthChainName, DepthChain );
				attributes.Set( DepthChainPrevFrameName, DepthChain );
			}

			attributes.Set( "BlueNoise", Texture.Load( "textures/dev/blue_noise_256.vtex" ) );
			attributes.Set( "QuarterResEffectsBloomInputTexture", Texture.Black );
			return scope;
		}
		catch
		{
			scope.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Count <paramref name="exposure"/>'s luminance bucket for this frame over its colour, in the block from
	/// <see cref="Begin()"/> before tonemapping, as a camera's stages do on Source 2. The exposure it moves to
	/// (<see cref="AutoExposure.FrameUpdate"/>, before the scene) is the next frames' <c>ToneMapScalar</c>.
	/// </summary>
	public void CountExposure( AutoExposure exposure ) => backend.CountExposure( exposure, Color, new Rect( 0, 0, Color.Width, Color.Height ) );

	/// <summary>An RG32F depth chain of this size with every mip down to the smaller side's last texel.</summary>
	static Texture Chain( Texture existing, int width, int height )
	{
		if ( existing is not null && existing.Width == width && existing.Height == height ) return existing;
		existing?.Dispose();
		var mips = System.Numerics.BitOperations.Log2( (uint)Math.Min( width, height ) ) + 1;
		return Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RG3232F ).WithMips( mips ).WithUAVBinding().Create( "Basalt.Gpu frame depth chain" );
	}

	/// <summary>Give the frame's targets back to Basalt, in the layouts the drawing left them in.</summary>
	public void End()
	{
		if ( target is null ) return;
		BackendDevice.SetLayout( target.Resolved ?? target.Color, backend.LayoutOf( Color ) );
		if ( target.Depth is not null ) BackendDevice.SetLayout( target.Depth, backend.LayoutOf( Depth ) );
	}

	public void Dispose()
	{
		Color?.Dispose();
		Depth?.Dispose();
		DepthChain?.Dispose();
		Color = Depth = DepthChain = null;
	}
}
