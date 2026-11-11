using Basalt;
using Basalt.Backend;
using GpuViewConstants = Basalt.Gpu.ViewConstants;

namespace Sandbox;

/// <summary>
/// A finished Basalt frame on a Basalt.Gpu device, handed to the engine's drawing on that device with no copy: its colour
/// and depth targets as engine <see cref="Texture"/>s whose backings are the frame's own, and what a camera's view gives
/// the drawing after the scene - the frame's view constants, the blue noise texture and bloom's quarter-size input.
/// What a camera frame on Basalt.Gpu is after the scene: post processing and the camera's later stages draw in a block
/// from <see cref="Begin"/>, then the frame is presented.
/// </summary>
/// <remarks>
/// The textures keep their engine-side identity from frame to frame; <see cref="Adopt"/> points them at the targets the
/// frame drew into. Layouts are handed over at both ends: the block starts where Basalt left each target, and Basalt's
/// next frame starts where the block left it.
/// </remarks>
internal sealed class BasaltGpuFrame : IDisposable
{
	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );

	readonly BasaltGpuRenderBackend backend;
	ViewTarget target;
	byte[] constants = [];

	/// <summary>The frame's colour target (its resolved one when multisampled).</summary>
	public Texture Color { get; private set; }

	/// <summary>The frame's depth target.</summary>
	public Texture Depth { get; private set; }

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
	/// A render block on the frame: its targets bound, and what the camera's view gives its drawing set on the block.
	/// Dispose it to submit; then call <see cref="End"/>.
	/// </summary>
	public Graphics.Scope Begin()
	{
		var scope = Graphics.Scope.CreateOn( backend );
		try
		{
			Graphics.RenderTarget = RenderTarget.From( Color, Depth );
			var attributes = Graphics.Attributes;
			attributes.SetData( ViewConstantsName, constants.AsSpan() );
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
		Color = Depth = null;
	}
}
