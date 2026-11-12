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

	/// <summary>Whether the frame was drawn multisampled: <see cref="Color"/> is its resolved colour, <see cref="Depth"/> keeps its samples.</summary>
	public bool Multisampled => target is { Samples: > 1 };

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
		var (handle, layout) = BackendDevice.Unwrap( gpu );
		return Engine( existing, handle, layout, size, format, depth );
	}

	Texture Engine( Texture existing, Basalt.Gpu.TextureHandle handle, Basalt.Gpu.ImageLayout layout, Vector2Int size, ImageFormat format, bool depth )
	{
		if ( existing is null || existing.Width != size.x || existing.Height != size.y || (!depth && existing.ImageFormat != format) )
		{
			existing?.Dispose();
			var builder = Texture.CreateRenderTarget().WithSize( size.x, size.y );
			builder = depth ? builder.WithDepthFormat() : builder.WithFormat( format );
			existing = builder.Create( depth ? "Basalt.Gpu frame depth" : "Basalt.Gpu frame colour" );
		}

		backend.Borrow( existing, handle, layout );
		return existing;
	}

	// A stage's targets inside the frame (RunStage): engine textures kept from frame to frame, borrowed for each stage
	Texture stageColor, stageDepth, stageChain;

	/// <summary>
	/// Draw a camera's stage inside Basalt's frame while it records it (<c>AfterDepthPrepass</c>, <c>AfterOpaque</c>,
	/// <c>AfterSkybox</c>): <paramref name="draw"/> runs in a block that records into the frame's own list at this point of
	/// it, on the stage's targets (the frame's colour, multisampled as the frame is, none for the depth prepass's stage, and
	/// its depth), with the frame's view constants and depth chain, through <paramref name="view"/>. Layouts are handed over
	/// at both ends and the frame binds what it draws with again after. <paramref name="first"/> (the frame's first stage)
	/// clears the frame's pipeline textures before the stage, so ambient occlusion's result is this frame's or none.
	/// </summary>
	internal void RunStage( in StageTarget target, in GpuViewConstants viewConstants, BasaltGpuView view, bool first, Action draw )
	{
		var context = (BackendContext)target.Context;
		var list = context.Lend();

		// In a segment recorded beside others, each starts where the stage uses it; the frame moves it there at the list's start
		(Texture Engine, BackendTexture Gpu) Borrow( ref Texture existing, GpuTexture gpu, ImageFormat format, bool depth, Basalt.Gpu.ImageLayout start )
		{
			if ( gpu is not BackendTexture texture ) return default;
			existing = Engine( existing, texture.Handle, context.LendLayout( texture, start ), new Vector2Int( texture.Width, texture.Height ), format, depth );
			return (existing, texture);
		}

		var color = Borrow( ref stageColor, target.Color, ImageFormat.RGBA16161616F, depth: false, Basalt.Gpu.ImageLayout.ColorTarget );
		var depth = Borrow( ref stageDepth, target.Depth, ImageFormat.D32FS8, depth: true, Basalt.Gpu.ImageLayout.DepthTarget );
		var chain = Borrow( ref stageChain, ((BackendParameters)target.Attributes).Get( DepthChainName ) as GpuTexture, ImageFormat.RG3232F, depth: false, Basalt.Gpu.ImageLayout.ShaderRead );

		var bytes = new byte[System.Runtime.CompilerServices.Unsafe.SizeOf<GpuViewConstants>()];
		System.Runtime.InteropServices.MemoryMarshal.Write( bytes, in viewConstants );

		view.Target = color.Engine is null ? null : RenderTarget.From( color.Engine, depth.Engine );
		view.Viewport = target.Viewport;
		backend.LendNextBlock( list );
		try
		{
			using var scope = Graphics.Scope.CreateOnView( backend, view );
			view.Bind();
			if ( first ) backend.ClearPipelineTextures();

			var attributes = Graphics.Attributes;
			attributes.SetData( ViewConstantsName, bytes.AsSpan() );
			if ( chain.Engine is not null )
			{
				attributes.Set( DepthChainName, chain.Engine );
				attributes.Set( DepthChainPrevFrameName, chain.Engine );
			}

			attributes.Set( "BlueNoise", Texture.Load( "textures/dev/blue_noise_256.vtex" ) );
			attributes.Set( "QuarterResEffectsBloomInputTexture", Texture.Black );
			draw();
		}
		finally
		{
			// Back to the frame, where the block left them, and no longer the engine's to track
			foreach ( var (engine, gpu) in (ReadOnlySpan<(Texture, BackendTexture)>)[color, depth, chain] )
			{
				if ( engine is null ) continue;
				context.SetLayout( gpu, backend.LayoutOf( engine ) );
				backend.Release( engine );
			}

			context.Reclaim();
		}
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
		// A multisampled frame hands over its resolved colour: its depth keeps its samples, so it isn't bound with it, and
		// what reads depth reads the chain built from it
		var frameTarget = Multisampled ? RenderTarget.From( Color ) : RenderTarget.From( Color, Depth );
		if ( view is not null ) view.Target = frameTarget;
		var scope = Graphics.Scope.CreateOnView( backend, view );
		try
		{
			if ( view is null ) Graphics.RenderTarget = frameTarget;
			else view.Bind();

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
		stageColor?.Dispose();
		stageDepth?.Dispose();
		stageChain?.Dispose();
		Color = Depth = DepthChain = stageColor = stageDepth = stageChain = null;
	}
}
