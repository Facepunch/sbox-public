using Borealis.Backend.Upscalers;
using GpuViewConstants = Borealis.Gpu.ViewConstants;

namespace Sandbox;

/// <summary>
/// A camera's upscaler on a Borealis.Gpu device, as native's standard pipeline has one for its main view: the scene renders at
/// a lower resolution, then before post processing it's upscaled into display-size colour and depth the later stages draw on
/// (<see cref="BorealisGpuFrame.Upscaled"/>). The mode and scale are the player's settings, read from the convars native reads
/// (<c>r_upscaling</c>, <c>r_render_scale</c>, <c>r_fsr3_quality</c>, <c>r_dlss_quality</c>):
/// <list type="bullet">
/// <item>off with a render scale under 1: a bilinear stretch;</item>
/// <item>FSR1: its two compute passes, EASU then RCAS (<c>fsr1_easu</c>, <c>fsr1_rcas</c>);</item>
/// <item>FSR3 and DLSS: the vendor libraries (<see cref="TemporalUpscaler"/>), with the projection jittered, camera motion
/// vectors from depth (<c>motion_vectors</c>) and a reactive mask over the overlays (viewmodels). These are the only
/// temporal techniques, and only while the player picks one: nothing temporal runs otherwise.</item>
/// </list>
/// DLSS where it can't run (any GPU but NVIDIA's) falls back to no upscaling with a warning, as native does.
/// </summary>
internal sealed class BorealisGpuUpscaler : IDisposable
{
	/// <summary>Native's <c>EUpscalerMode</c>.</summary>
	internal enum Mode { Off = 0, Stretch = 1, Fsr1 = 2, Fsr3 = 3, Dlss = 4 }

	static readonly StringToken FsrInput = "FsrInput";
	static readonly StringToken FsrOutput = "FsrOutput";
	static readonly StringToken FsrInputViewportSize = "FsrInputViewportSize";
	static readonly StringToken FsrInputSize = "FsrInputSize";
	static readonly StringToken FsrOutputSize = "FsrOutputSize";
	static readonly StringToken FsrRcasAttenuation = "FsrRcasAttenuation";
	static readonly StringToken SourceDepth = "SourceDepth";
	static readonly StringToken ReactiveDepth = "ReactiveDepth";
	static readonly StringToken ReactiveDepthThreshold = "ReactiveDepthThreshold";

	// FSR1 works in 16x16 tiles, 64 threads each
	const int Fsr1TileSize = 16;

	/// <summary>
	/// The mode the settings ask for and its render scale, as native's <c>GetActiveUpscalerMode</c> and
	/// <c>GetUpscalerViewportScale</c> work them out: off (and the old stretch setting) with a render scale under 1 stretches.
	/// </summary>
	internal static (Mode Mode, float Scale) Settings()
	{
		var mode = (Mode)Math.Clamp( ConVarSystem.GetInt( "r_upscaling", 0, true ), 0, (int)Mode.Dlss );
		if ( mode == Mode.Stretch ) mode = Mode.Off;

		var scale = mode switch
		{
			Mode.Fsr3 => QualityScale( ConVarSystem.GetInt( "r_fsr3_quality", 1, true ) ),
			Mode.Dlss => QualityScale( ConVarSystem.GetInt( "r_dlss_quality", 1, true ) ),
			_ => Math.Clamp( ConVarSystem.GetFloat( "r_render_scale", 1.0f, true ), 0.4f, 1.0f ),
		};

		if ( mode == Mode.Off && scale < 1.0f ) mode = Mode.Stretch;
		return (mode, scale);
	}

	/// <summary>A quality preset's render scale, shared by FSR3 and DLSS (native's <c>QualityPresetToScale</c>).</summary>
	internal static float QualityScale( int quality ) => quality switch
	{
		0 => 1.0f / 3.0f,
		1 => 1.0f / 2.0f,
		2 => 1.0f / 1.7f,
		3 => 1.0f / 1.5f,
		4 => 1.0f,
		_ => 1.0f / 2.0f,
	};

	readonly BorealisGpuRenderBackend backend;

	public BorealisGpuUpscaler( BorealisGpuRenderBackend backend ) => this.backend = backend;

	/// <summary>The mode this frame upscales with; <see cref="Mode.Off"/> renders at the display size.</summary>
	public Mode Active { get; private set; }

	/// <summary>The size the scene renders at this frame.</summary>
	public Vector2Int RenderSize { get; private set; }

	/// <summary>The size it's upscaled to.</summary>
	public Vector2Int DisplaySize { get; private set; }

	/// <summary>Whether the scene renders single-sampled: every mode but the stretch, as native turns MSAA off for them.</summary>
	public bool SingleSampled => Active is Mode.Fsr1 or Mode.Fsr3 or Mode.Dlss;

	/// <summary>This frame's jitter in pixels (temporal modes), and as a clip-space offset for the projection.</summary>
	public Vector2 Jitter { get; private set; }
	public Vector2 ClipJitter => RenderSize.x < 1 ? default : new Vector2( 2.0f * Jitter.x / RenderSize.x, -2.0f * Jitter.y / RenderSize.y );

	TemporalUpscaler temporal;
	int jitterIndex;
	double lastDispatch = -1;
	bool resetHistory;
	static bool warnedDlss;

	Texture output, intermediate, depth, motionVectors, reactiveMask, temporalDepth;
	RenderTarget depthTarget, motionTarget, reactiveTarget, temporalDepthTarget;
	Material motionMaterial, reactiveMaterial;
	ComputeShader easu, rcas;
	RenderAttributes easuAttributes, rcasAttributes, depthAttributes, reactiveAttributes;

	/// <summary>
	/// Before the camera's scene renders, outside any block: what this frame does at <paramref name="display"/>, its render size
	/// and jitter, the upscaler made (again) when the settings or size changed. <paramref name="allowed"/> is native's main
	/// view outside the tools: false renders at the display size. Returns whether the frame upscales.
	/// </summary>
	public bool Begin( Vector2Int display, bool allowed )
	{
		var (mode, scale) = allowed ? Settings() : (Mode.Off, 1.0f);
		DisplaySize = display;
		var render = new Vector2Int( Math.Max( 1, (int)(scale * display.x) ), Math.Max( 1, (int)(scale * display.y) ) );

		if ( mode is Mode.Fsr3 or Mode.Dlss && !EnsureTemporal( mode, render, display ) )
		{
			if ( mode == Mode.Dlss && !warnedDlss )
			{
				warnedDlss = true;
				Log.Warning( "DLSS (r_upscaling 4) is unavailable on this system (requires an NVIDIA GPU) - falling back to no upscaling." );
			}

			mode = Mode.Off;
		}

		if ( mode is not (Mode.Fsr3 or Mode.Dlss) ) DropTemporal();

		Active = mode;
		Jitter = default;
		if ( mode == Mode.Off )
		{
			RenderSize = display;
			ReleaseTargets();
			return false;
		}

		RenderSize = render;
		if ( temporal is not null )
		{
			var (x, y) = temporal.Jitter( jitterIndex );
			jitterIndex = (jitterIndex + 1) % temporal.JitterPhaseCount;
			Jitter = new Vector2( x, y );
		}

		MakeTargets();
		return true;
	}

	bool EnsureTemporal( Mode mode, Vector2Int render, Vector2Int display )
	{
		var kind = mode == Mode.Dlss ? TemporalUpscalerKind.Dlss : TemporalUpscalerKind.Fsr3;
		if ( temporal is not null && temporal.Kind == kind && temporal.RenderSize == (render.x, render.y) && temporal.DisplaySize == (display.x, display.y) ) return true;

		DropTemporal();
		TemporalUpscaler.Message ??= ( severity, text ) =>
		{
			if ( severity >= 1 ) Log.Warning( $"Borealis.Gpu: {text}" );
			else Log.Info( $"Borealis.Gpu: {text}" );
		};

		temporal = TemporalUpscaler.Create( backend.Device, kind, (render.x, render.y), (display.x, display.y) );
		jitterIndex = 0;
		lastDispatch = -1;
		resetHistory = true;
		return temporal is not null;
	}

	void DropTemporal()
	{
		if ( temporal is null ) return;

		// What it recorded may still be on the GPU: a settings change, so a wait is fine
		backend.WaitIdle();
		temporal.Dispose();
		temporal = null;
	}

	void MakeTargets()
	{
		output = Target( output, DisplaySize, ImageFormat.RGBA16161616F, "Borealis.Gpu upscaled colour" );
		depth = DepthTarget( depth, DisplaySize, "Borealis.Gpu upscaled depth" );
		if ( depthTarget is null || depthTarget.ColorTarget != output || depthTarget.DepthTarget != depth )
		{
			depthTarget?.Dispose();
			depthTarget = RenderTarget.From( output, depth );
		}

		depthAttributes ??= RenderAttributes.CreateManaged();

		if ( Active == Mode.Fsr1 )
		{
			intermediate = Target( intermediate, DisplaySize, ImageFormat.RGBA16161616F, "Borealis.Gpu FSR1 EASU" );
			easu ??= new ComputeShader( "shaders/fsr1_easu.shader" );
			rcas ??= new ComputeShader( "shaders/fsr1_rcas.shader" );
			easuAttributes ??= RenderAttributes.CreateManaged();
			rcasAttributes ??= RenderAttributes.CreateManaged();
		}

		if ( temporal is not null )
		{
			motionVectors = Target( motionVectors, RenderSize, ImageFormat.RGBA16161616F, "Borealis.Gpu motion vectors" );
			reactiveMask = Target( reactiveMask, RenderSize, ImageFormat.R16F, "Borealis.Gpu reactive mask" );

			// The upscalers read depth alone (FidelityFX views it as D32), where the frame's has stencil
			if ( temporalDepth is null || temporalDepth.Width != RenderSize.x || temporalDepth.Height != RenderSize.y )
			{
				temporalDepth?.Dispose();
				temporalDepth = Texture.CreateOnBorealisGpu( Texture.CreateRenderTarget().WithSize( RenderSize.x, RenderSize.y ).WithFormat( ImageFormat.D32 ), "Borealis.Gpu upscaler depth" );
			}

			if ( temporalDepthTarget is null || temporalDepthTarget.ColorTarget != motionVectors || temporalDepthTarget.DepthTarget != temporalDepth )
			{
				temporalDepthTarget?.Dispose();
				temporalDepthTarget = RenderTarget.From( motionVectors, temporalDepth );
			}

			if ( motionTarget is null || motionTarget.ColorTarget != motionVectors )
			{
				motionTarget?.Dispose();
				motionTarget = RenderTarget.From( motionVectors );
			}

			if ( reactiveTarget is null || reactiveTarget.ColorTarget != reactiveMask )
			{
				reactiveTarget?.Dispose();
				reactiveTarget = RenderTarget.From( reactiveMask );
			}

			motionMaterial ??= Material.FromShader( "shaders/motion_vectors.shader" );
			reactiveMaterial ??= Material.FromShader( "shaders/reactive_mask_generate.shader" );
			reactiveAttributes ??= RenderAttributes.CreateManaged();
		}
	}

	static Texture Target( Texture existing, Vector2Int size, ImageFormat format, string name )
	{
		if ( existing is not null && existing.Width == size.x && existing.Height == size.y ) return existing;
		existing?.Dispose();
		return Texture.CreateOnBorealisGpu( Texture.CreateRenderTarget().WithSize( size.x, size.y ).WithFormat( format ).WithUAVBinding(), name );
	}

	static Texture DepthTarget( Texture existing, Vector2Int size, string name )
	{
		if ( existing is not null && existing.Width == size.x && existing.Height == size.y ) return existing;
		existing?.Dispose();
		return Texture.CreateOnBorealisGpu( Texture.CreateRenderTarget().WithSize( size.x, size.y ).WithDepthFormat(), name );
	}

	/// <summary>
	/// In <paramref name="frame"/>'s block after the stages drawn at the render size (after the viewmodel): the upscale into
	/// display-size colour and depth, which the frame takes for the stages after (<see cref="BorealisGpuFrame.Upscaled"/>).
	/// <paramref name="view"/>'s viewport is the render-size one on the way in and <paramref name="displayViewport"/> after;
	/// <paramref name="upscaledConstants"/> are the frame's constants at the display size, unjittered. The camera's near plane
	/// and vertical field of view (radians) are for the temporal upscalers.
	/// </summary>
	public void Upscale( BorealisGpuFrame frame, BorealisGpuView view, Rect displayViewport, in GpuViewConstants upscaledConstants, float zNear, float verticalFov )
	{
		var renderViewport = view.Viewport;
		var color = frame.SceneColor;
		var sceneDepth = frame.SceneDepth;
		if ( color is null || output is null ) return;

		// The temporal inputs at the render size: camera motion from depth (through the frame's depth chain and last frame's
		// projection) and the overlays' depth range as the reactive mask, drawn with the scene's constants
		var temporalReady = false;
		if ( temporal is not null && sceneDepth is not null && motionTarget is not null )
		{
			// The depth copied first, into the motion vectors' target (the resolve writes depth alone)
			Graphics.RenderTarget = temporalDepthTarget;
			Graphics.Viewport = renderViewport;
			Graphics.Clear( Color.Transparent, false, true, true );
			depthAttributes.Set( SourceDepth, sceneDepth );
			depthAttributes.SetCombo( "D_MSAA", false );
			depthAttributes.SetCombo( "D_UPSCALE", false );
			depthAttributes.Set( "DownsampleFactor", 1 );
			Graphics.Blit( DepthResolve, depthAttributes );

			Graphics.RenderTarget = motionTarget;
			Graphics.Viewport = renderViewport;
			Graphics.Clear( Color.Transparent, true, false, false );
			Graphics.Blit( motionMaterial );

			reactiveAttributes.Set( ReactiveDepth, sceneDepth );
			reactiveAttributes.Set( ReactiveDepthThreshold, 1.0f - Borealis.GameOverlayLayer.DepthRange - 0.0001f );
			Graphics.RenderTarget = reactiveTarget;
			Graphics.Viewport = renderViewport;
			Graphics.Clear( Color.Transparent, true, false, false );
			Graphics.Blit( reactiveMaterial, reactiveAttributes );
			temporalReady = true;
		}

		// The depth first, into the output's target (the resolve writes depth alone), before the colour fills the output
		if ( sceneDepth is not null )
		{
			Graphics.RenderTarget = depthTarget;
			Graphics.Viewport = displayViewport;
			Graphics.Clear( Color.Transparent, false, true, true );
			depthAttributes.Set( SourceDepth, sceneDepth );
			depthAttributes.SetCombo( "D_MSAA", frame.Multisampled );
			depthAttributes.SetCombo( "D_UPSCALE", true );
			depthAttributes.Set( "OutputToSourceScale", new Vector2( (float)RenderSize.x / DisplaySize.x, (float)RenderSize.y / DisplaySize.y ) );
			Graphics.Blit( DepthResolve, depthAttributes );
		}

		Graphics.RenderTarget = null;
		switch ( Active )
		{
			case Mode.Fsr1:
				Fsr1( color, renderViewport, displayViewport );
				break;

			case Mode.Fsr3:
			case Mode.Dlss:
				if ( !temporalReady || !Temporal( color, zNear, verticalFov ) ) backend.StretchBlit( color, renderViewport, output, displayViewport );
				break;

			default:
				backend.StretchBlit( color, renderViewport, output, displayViewport );
				break;
		}

		view.Viewport = displayViewport;
		frame.Upscaled( output, depth, upscaledConstants, view );
	}

	static Material depthResolve;
	static Material DepthResolve => depthResolve ??= Material.FromShader( "shaders/depthresolve.shader" );

	/// <summary>Made outside any block, with the rest of a frame's materials (materials can't be made while drawing).</summary>
	public static void Prepare() => _ = DepthResolve;

	void Fsr1( Texture color, Rect renderViewport, Rect displayViewport )
	{
		var lowSize = new Vector2( renderViewport.Width, renderViewport.Height );
		var fullSize = new Vector2( displayViewport.Width, displayViewport.Height );
		var groups = new Vector2Int( ((int)fullSize.x + Fsr1TileSize - 1) / Fsr1TileSize, ((int)fullSize.y + Fsr1TileSize - 1) / Fsr1TileSize );
		if ( groups.x <= 0 || groups.y <= 0 ) return;

		easuAttributes.Set( FsrInput, color );
		easuAttributes.Set( FsrOutput, intermediate );
		easuAttributes.Set( FsrInputViewportSize, lowSize );
		easuAttributes.Set( FsrInputSize, lowSize );
		easuAttributes.Set( FsrOutputSize, fullSize );
		easu.DispatchWithAttributes( easuAttributes, groups.x * 64, groups.y, 1 );

		var sharpness = Math.Clamp( ConVarSystem.GetFloat( "r_fsr_rcas_sharpness", 0.25f, true ), 0.0f, 1.0f );
		rcasAttributes.Set( FsrInput, intermediate );
		rcasAttributes.Set( FsrOutput, output );
		rcasAttributes.Set( FsrInputViewportSize, fullSize );
		rcasAttributes.Set( FsrInputSize, fullSize );
		rcasAttributes.Set( FsrOutputSize, fullSize );
		rcasAttributes.Set( FsrRcasAttenuation, (1.0f - sharpness) * 2.0f );
		rcas.DispatchWithAttributes( rcasAttributes, groups.x * 64, groups.y, 1 );
	}

	bool Temporal( Texture color, float zNear, float verticalFov )
	{
		// Wall-clock time between dispatches, clamped as native's (a hitch decays the history hard for one frame otherwise)
		var now = RealTime.NowDouble;
		var delta = lastDispatch < 0 ? 16.6f : Math.Clamp( (float)((now - lastDispatch) * 1000.0), 1.0f, 100.0f );
		lastDispatch = now;

		var settings = new TemporalUpscaler.Frame
		{
			JitterX = Jitter.x,
			JitterY = Jitter.y,
			Sharpness = Active == Mode.Fsr3 ? Math.Clamp( ConVarSystem.GetFloat( "r_fsr3_sharpness", 0.5f, true ), 0.0f, 1.0f ) : 0,
			FrameTimeDeltaMs = delta,
			CameraNear = zNear,
			CameraFovVertical = verticalFov,
			Reset = resetHistory,
		};

		resetHistory = false;
		return backend.UpscaleTemporal( temporal, color, temporalDepth, motionVectors, reactiveMask, output, settings );
	}

	public void Dispose()
	{
		DropTemporal();
		ReleaseTargets();
	}

	// Nothing kept while the frame doesn't upscale
	void ReleaseTargets()
	{
		if ( output is null && motionVectors is null ) return;
		depthTarget?.Dispose();
		motionTarget?.Dispose();
		reactiveTarget?.Dispose();
		temporalDepthTarget?.Dispose();
		output?.Dispose();
		intermediate?.Dispose();
		depth?.Dispose();
		motionVectors?.Dispose();
		reactiveMask?.Dispose();
		temporalDepth?.Dispose();
		depthTarget = motionTarget = reactiveTarget = temporalDepthTarget = null;
		output = intermediate = depth = motionVectors = reactiveMask = temporalDepth = null;
	}
}
