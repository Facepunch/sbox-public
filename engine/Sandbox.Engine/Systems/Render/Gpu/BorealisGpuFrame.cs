using Borealis;
using Borealis.Backend;
using GpuViewConstants = Borealis.Gpu.ViewConstants;

namespace Sandbox;

/// <summary>
/// A finished Borealis frame on a Borealis.Gpu device, handed to the engine's drawing on that device with no copy: its colour
/// and depth targets as engine <see cref="Texture"/>s whose backings are the frame's own, and what a camera's view gives
/// the drawing after the scene - the frame's view constants, the blue noise texture and bloom's quarter-size input.
/// What a camera frame on Borealis.Gpu is after the scene: post processing and the camera's later stages draw in a block
/// from <see cref="Begin()"/>, which also builds the view's depth chain from the frame's depth, then the frame is presented.
/// </summary>
/// <remarks>
/// The textures keep their engine-side identity from frame to frame; <see cref="Adopt"/> points them at the targets the
/// frame drew into. Layouts are handed over at both ends: the block starts where Borealis left each target, and Borealis's
/// next frame starts where the block left it.
/// </remarks>
internal sealed class BorealisGpuFrame : IDisposable
{
	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );
	static readonly StringToken DepthChainName = new( "DepthChainDownsample" );
	static readonly StringToken DepthChainPrevFrameName = new( "DepthChainDownsamplePrevFrame" );

	readonly BorealisGpuRenderBackend backend;
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

	public BorealisGpuFrame( BorealisGpuRenderBackend backend ) => this.backend = backend;

	/// <summary>Whether the frame was drawn multisampled: <see cref="Color"/> is its resolved colour, <see cref="Depth"/> keeps its samples.</summary>
	public bool Multisampled => target is { Samples: > 1 };

	/// <summary>
	/// Take a frame Borealis rendered into <paramref name="target"/> on the backend's device, with the view constants it drew
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

		// Made here, outside any block: materials can't be made while drawing
		if ( Multisampled ) depthResolve ??= Material.FromShader( "shaders/depthresolve.shader" );
	}

	static Material depthResolve;
	Texture resolvedDepth, resolveScratch;
	RenderAttributes resolveAttributes;

	/// <summary>
	/// The frame's depth with one sample, in the block being recorded: <see cref="Depth"/>, or for a multisampled frame its
	/// first sample written into a depth of its own (<c>depthresolve.shader</c>, as native resolves depth), for drawing depth
	/// tested over a single-sampled output. Leaves no target bound that the caller should keep.
	/// </summary>
	public Texture ResolvedDepth()
	{
		if ( Depth is null || !Multisampled ) return Depth;
		if ( depthResolve is null ) return null;

		if ( resolvedDepth is null || resolvedDepth.Width != Depth.Width || resolvedDepth.Height != Depth.Height )
		{
			resolvedDepth?.Dispose();
			resolveScratch?.Dispose();
			resolvedDepth = Texture.CreateRenderTarget().WithSize( Depth.Width, Depth.Height ).WithDepthFormat().Create( "Borealis.Gpu frame resolved depth" );
			// The resolve writes depth alone; the engine's drawing always has a colour target
			resolveScratch = Texture.CreateRenderTarget().WithSize( Depth.Width, Depth.Height ).WithFormat( ImageFormat.RGBA8888 ).Create( "Borealis.Gpu depth resolve scratch" );
		}

		resolveAttributes ??= RenderAttributes.CreateManaged();
		resolveAttributes.Set( "SourceDepth", Depth );
		resolveAttributes.SetCombo( "D_MSAA", 1 );
		resolveAttributes.SetCombo( "D_UPSCALE", 0 );
		resolveAttributes.Set( "DownsampleFactor", 1 );

		using var target = RenderTarget.From( resolveScratch, resolvedDepth );
		backend.BindRenderTarget( target );
		Graphics.Viewport = new Rect( 0, 0, Depth.Width, Depth.Height );
		Graphics.Clear( global::Color.Transparent, false, true, true );
		Graphics.Blit( depthResolve, resolveAttributes );
		return resolvedDepth;
	}

	byte[] outputConstants = [];

	/// <summary>
	/// Set the frame's view constants on the block being recorded for drawing over its output: the viewport and target sizes
	/// become <paramref name="viewport"/> in a target of <paramref name="targetSize"/>. The stages after post processing and the
	/// UI draw there, as native's draw with the view's constants.
	/// </summary>
	public void SetOutputConstants( Rect viewport, Vector2 targetSize )
	{
		if ( constants.Length == 0 || targetSize.x < 1 || targetSize.y < 1 ) return;

		var c = System.Runtime.InteropServices.MemoryMarshal.Read<GpuViewConstants>( constants );
		c.ViewportOffset = new( viewport.Left, viewport.Top );
		c.ViewportSize = new( viewport.Width, viewport.Height );
		c.InvViewportSize = new( 1.0f / viewport.Width, 1.0f / viewport.Height );
		c.RenderTargetSize = new( targetSize.x, targetSize.y );
		c.ViewportToGBufferRatio = new( viewport.Width / targetSize.x, viewport.Height / targetSize.y );
		c.InvGBufferSize = new( 1.0f / targetSize.x, 1.0f / targetSize.y, 1.0f, 1.0f );

		if ( outputConstants.Length != constants.Length ) outputConstants = new byte[constants.Length];
		System.Runtime.InteropServices.MemoryMarshal.Write( outputConstants, in c );
		Graphics.Attributes.SetData( ViewConstantsName, outputConstants.AsSpan() );
	}

	/// <summary>An engine texture of the target's size whose backing on the device is <paramref name="gpu"/>.</summary>
	Texture Engine( Texture existing, GpuTexture gpu, Vector2Int size, ImageFormat format, bool depth )
	{
		var (handle, layout) = BackendDevice.Unwrap( gpu );
		return Engine( existing, handle, layout, size, format, depth );
	}

	Texture Engine( Texture existing, Borealis.Gpu.TextureHandle handle, Borealis.Gpu.ImageLayout layout, Vector2Int size, ImageFormat format, bool depth )
	{
		if ( existing is null || existing.Width != size.x || existing.Height != size.y || (!depth && existing.ImageFormat != format) )
		{
			existing?.Dispose();
			var builder = Texture.CreateRenderTarget().WithSize( size.x, size.y );
			builder = depth ? builder.WithDepthFormat() : builder.WithFormat( format );
			existing = builder.Create( depth ? "Borealis.Gpu frame depth" : "Borealis.Gpu frame colour" );
		}

		backend.Borrow( existing, handle, layout );
		return existing;
	}

	// A stage's targets inside the frame (RunStage): engine textures kept from frame to frame, borrowed for each stage
	Texture stageColor, stageDepth, stageChain;

	/// <summary>
	/// Draw a camera's stage inside Borealis's frame while it records it (<c>AfterDepthPrepass</c>, <c>AfterOpaque</c>,
	/// <c>AfterSkybox</c>): <paramref name="draw"/> runs in a block that records into the frame's own list at this point of
	/// it, on the stage's targets (the frame's colour, multisampled as the frame is, none for the depth prepass's stage, and
	/// its depth), with the frame's view constants and depth chain, through <paramref name="view"/>. Layouts are handed over
	/// at both ends and the frame binds what it draws with again after. <paramref name="first"/> (the frame's first stage)
	/// clears the frame's pipeline textures before the stage, so ambient occlusion's result is this frame's or none.
	/// </summary>
	internal void RunStage( in StageTarget target, in GpuViewConstants viewConstants, BorealisGpuView view, bool first, Action draw )
	{
		if ( stageConstants.Length != System.Runtime.CompilerServices.Unsafe.SizeOf<GpuViewConstants>() ) stageConstants = new byte[System.Runtime.CompilerServices.Unsafe.SizeOf<GpuViewConstants>()];
		System.Runtime.InteropServices.MemoryMarshal.Write( stageConstants, in viewConstants );
		RunInFrame( target, stageConstants, view, first, draw );
	}

	byte[] stageConstants = [];

	/// <summary>
	/// As <see cref="RunStage"/>, with the view constants as <c>PerViewConstantBuffer_t</c>'s bytes: also what custom objects
	/// draw through (<see cref="BorealisGpuCustomDraw"/>). <paramref name="depthAlone"/> draws into the target's depth with no
	/// colour, at its slice (a shadow map's, or the depth prepass's), where a stage with no colour binds nothing.
	/// </summary>
	internal void RunInFrame( in StageTarget target, byte[] viewConstants, BorealisGpuView view, bool first, Action draw, bool depthAlone = false )
	{
		if ( depthAlone )
		{
			RunInDepth( target, viewConstants, view, draw );
			return;
		}

		var context = (BackendContext)target.Context;
		var list = context.Lend();

		// In a segment recorded beside others, each starts where the stage uses it; the frame moves it there at the list's start
		(Texture Engine, BackendTexture Gpu) Borrow( ref Texture existing, GpuTexture gpu, ImageFormat format, bool depth, Borealis.Gpu.ImageLayout start )
		{
			if ( gpu is not BackendTexture texture ) return default;
			existing = Engine( existing, texture.Handle, context.LendLayout( texture, start ), new Vector2Int( texture.Width, texture.Height ), format, depth );
			return (existing, texture);
		}

		var color = Borrow( ref stageColor, target.Color, ImageFormat.RGBA16161616F, depth: false, Borealis.Gpu.ImageLayout.ColorTarget );
		var depth = Borrow( ref stageDepth, target.Depth, ImageFormat.D32FS8, depth: true, Borealis.Gpu.ImageLayout.DepthTarget );
		var chain = Borrow( ref stageChain, ((BackendParameters)target.Attributes).Get( DepthChainName ) as GpuTexture, ImageFormat.RG3232F, depth: false, Borealis.Gpu.ImageLayout.ShaderRead );

		view.Target = color.Engine is null ? null : RenderTarget.From( color.Engine, depth.Engine );
		view.DepthSlice = 0;
		view.Viewport = target.Viewport;
		backend.LendNextBlock( list );
		backend.DepthRange = target.MaxZ > target.MinZ ? (target.MinZ, target.MaxZ) : (0, 1);
		try
		{
			using var scope = Graphics.Scope.CreateOnView( backend, view );
			view.Bind();
			if ( first ) backend.ClearPipelineTextures();

			var attributes = Graphics.Attributes;
			attributes.SetData( ViewConstantsName, viewConstants.AsSpan() );
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

			backend.DepthRange = (0, 1);
			context.Reclaim();
		}
	}

	// Depth targets drawn into alone (RunInDepth), by size: engine textures kept from frame to frame, each borrowed for a draw,
	// and a target of each
	readonly Dictionary<Vector2Int, (Texture Texture, RenderTarget Target)> depthAlone = new();

	/// <summary>
	/// <see cref="RunInFrame"/> into <paramref name="target"/>'s depth alone, at its slice: what a custom object in a shadow map
	/// or the depth prepass draws into. The frame's view constants for that view, no depth chain (depth passes don't read it).
	/// </summary>
	void RunInDepth( in StageTarget target, byte[] viewConstants, BorealisGpuView view, Action draw )
	{
		if ( target.Depth is not BackendTexture gpu ) return;

		var context = (BackendContext)target.Context;
		var list = context.Lend();
		var size = new Vector2Int( gpu.Width, gpu.Height );
		if ( !depthAlone.TryGetValue( size, out var kept ) )
		{
			var texture = Texture.CreateRenderTarget().WithSize( size.x, size.y ).WithDepthFormat().Create( "Borealis.Gpu depth drawn alone" );
			depthAlone[size] = kept = (texture, RenderTarget.FromDepth( texture ));
		}

		backend.Borrow( kept.Texture, gpu.Handle, context.LendLayout( gpu, Borealis.Gpu.ImageLayout.DepthTarget ) );
		view.Target = kept.Target;
		view.DepthSlice = target.DepthSlice;
		view.Viewport = target.Viewport;
		backend.LendNextBlock( list );
		backend.DepthRange = target.MaxZ > target.MinZ ? (target.MinZ, target.MaxZ) : (0, 1);
		try
		{
			using var scope = Graphics.Scope.CreateOnView( backend, view );
			view.Bind();
			Graphics.Attributes.SetData( ViewConstantsName, viewConstants.AsSpan() );
			draw();
		}
		finally
		{
			context.SetLayout( gpu, backend.LayoutOf( kept.Texture ) );
			backend.Release( kept.Texture );
			backend.DepthRange = (0, 1);
			view.Target = null;
			view.DepthSlice = 0;
			context.Reclaim();
		}
	}

	/// <summary>
	/// A render block on the frame: its targets bound, its depth chain built, and what the camera's view gives its drawing
	/// set on the block. Dispose it to submit; then call <see cref="End"/>.
	/// </summary>
	public Graphics.Scope Begin() => Begin( null );

	// The frame's targets as the block binds them; the textures are the frame's, so nothing to dispose
	RenderTarget frameTarget;

	/// <summary>
	/// A render block on the frame as <see cref="Begin()"/>'s, which a camera's stages draw in: <paramref name="view"/> is
	/// what they ask about the frame, and drawing into a render target of their own goes back to the frame's targets after.
	/// </summary>
	public Graphics.Scope Begin( BorealisGpuView view )
	{
		// A multisampled frame hands over its resolved colour: its depth keeps its samples, so it isn't bound with it, and
		// what reads depth reads the chain built from it
		frameTarget = Multisampled ? RenderTarget.From( Color ) : RenderTarget.From( Color, Depth );
		if ( view is not null ) view.Target = frameTarget;
		var scope = Graphics.Scope.CreateOnView( backend, view );
		try
		{
			if ( view is null ) Graphics.RenderTarget = frameTarget;
			else view.Bind();

			var attributes = Graphics.Attributes;
			attributes.SetData( ViewConstantsName, constants.AsSpan() );

			// What reads the view's depth through Depth::Get; the previous frame's aliases it, as Borealis's does
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
		return Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RG3232F ).WithMips( mips ).WithUAVBinding().Create( "Borealis.Gpu frame depth chain" );
	}

	/// <summary>Give the frame's targets back to Borealis, in the layouts the drawing left them in.</summary>
	public void End()
	{
		if ( target is null ) return;
		BackendDevice.SetLayout( target.Resolved ?? target.Color, backend.LayoutOf( Color ) );
		if ( target.Depth is not null ) BackendDevice.SetLayout( target.Depth, backend.LayoutOf( Depth ) );
	}

	/// <summary>
	/// The engine's drawing is done with the frame (its output is drawn): its targets go back to Borealis where that left them,
	/// and the engine no longer tracks them, so no later block moves them behind Borealis's back. The next frame borrows them
	/// again (<see cref="Adopt"/>).
	/// </summary>
	public void Release()
	{
		End();
		if ( Color is not null ) backend.Release( Color );
		if ( Depth is not null ) backend.Release( Depth );
		target = null;
	}

	public void Dispose()
	{
		Color?.Dispose();
		Depth?.Dispose();
		DepthChain?.Dispose();
		stageColor?.Dispose();
		stageDepth?.Dispose();
		stageChain?.Dispose();
		resolvedDepth?.Dispose();
		resolveScratch?.Dispose();
		foreach ( var (texture, _) in depthAlone.Values ) texture.Dispose();
		depthAlone.Clear();
		Color = Depth = DepthChain = stageColor = stageDepth = stageChain = resolvedDepth = resolveScratch = null;
	}
}
