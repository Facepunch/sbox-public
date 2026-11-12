using Sandbox.Rendering;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// A camera rendering into a game window on Basalt.Gpu (<c>-basaltgpu</c>, docs/managed/basalt-gpu-path.md stage 5).
/// </summary>
internal sealed partial class SceneBridge
{
	static readonly StringToken UIGammaOutput = new( "UIGammaOutput" );
	static readonly StringToken CopySource = new( "CustomTexture1" );
	static readonly StringToken SrgbTransfer = new( "D_SRGB_TRANSFER" );
	static readonly StringToken FlipXName = new( "FlipX" );
	static readonly StringToken FlipYName = new( "FlipY" );

	// Where a camera's frame in a Basalt.Gpu window goes, in the dev tools' timings
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings PrepareTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Basalt.Gpu camera prepare" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings SceneTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Basalt.Gpu camera scene" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings PostTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Basalt.Gpu camera post" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings OutputTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Basalt.Gpu camera output" );

	// What a camera's stages on Basalt.Gpu ask of their frame; stages run one camera at a time, so one does
	BasaltGpuView gpuView;
	Material copyMaterial;
	RenderAttributes copyAttributes;

	/// <summary>
	/// Render a camera into the frame <paramref name="window"/> is drawing, as a swap chain render does on Source 2's device:
	/// <list type="number">
	/// <item>its scene, by Basalt on the Basalt.Gpu device, into a float target of its own (with no stages inside it yet);</item>
	/// <item>that frame handed to the engine's drawing (<see cref="BasaltGpuFrame"/>), where the stages after the scene and
	/// post processing run, auto exposure counts its bucket, and tonemapping;</item>
	/// <item>the frame copied into the camera's part of the window, encoded as an sRGB swap chain stores it;</item>
	/// <item>the stages that draw into the output: after post processing, the UI, after the UI.</item>
	/// </list>
	/// A camera without a scene (UI only) clears its part of the window instead.
	/// </summary>
	void RenderInto( BasaltGpuWindow window, SceneCamera camera, Vector2? size, in ViewSetup setup )
	{
		var renderSize = size ?? window.Size;
		if ( renderSize.x < 1 || renderSize.y < 1 ) return;

		camera.OnPreRender( renderSize );
		var rect = camera.Rect;
		var viewport = new Rect( rect.Left * renderSize.x, rect.Top * renderSize.y, rect.Width * renderSize.x, rect.Height * renderSize.y );

		var backend = (BasaltGpuRenderBackend)RenderBackend.Default;
		gpuView ??= new BasaltGpuView( backend );
		gpuView.Viewport = viewport;
		gpuView.SrgbWrite = false;
		gpuView.PostProcessEnabled = setup.EnablePostprocessing ?? camera.EnablePostProcessing;
		gpuView.SetCamera( camera, viewport.Size );

		var frame = camera.UIOnly || NotYetInBasalt( camera, setup ) is not null ? null : RenderScene( backend, camera, renderSize, window.Samples, setup );

		// Made outside any block: materials can't be made while drawing
		copyMaterial ??= Material.FromShader( "shaders/copytexture.shader" );

		var ui = setup.EnableUI ?? camera.RenderUI;
		gpuView.Target = RenderTarget.From( window.Target );
		gpuView.Viewport = viewport;
		using var _ = OutputTiming.Scope();
		window.Draw( gpuView, () => DrawOutput( camera, frame, ui, encode: true, false, false ) );
		frame?.Release();
	}

	/// <summary>
	/// Render a camera into an engine texture on Basalt.Gpu (a scene panel's), as native's texture render: its scene and stages
	/// as into a window, without multisampling, then the frame copied into the texture (mirrored as the setup asks) and the
	/// stages that draw into the output. An 8-bit texture is written through its sRGB view, as Source 2 writes one: encoded,
	/// with the frame's alpha kept (a scene panel's transparent background).
	/// </summary>
	void RenderIntoTexture( BasaltGpuRenderBackend backend, SceneCamera camera, Texture target, Vector2 size, in ViewSetup setup )
	{
		if ( size.x < 1 || size.y < 1 || NotYetInBasalt( camera, setup ) is not null ) return;

		camera.ConfigureView( setup );
		var viewport = new Rect( 0, 0, size.x, size.y );
		gpuView ??= new BasaltGpuView( backend );
		gpuView.Viewport = viewport;
		gpuView.SrgbWrite = false;
		gpuView.PostProcessEnabled = setup.EnablePostprocessing ?? camera.EnablePostProcessing;
		gpuView.SetCamera( camera, size );

		var frame = RenderScene( backend, camera, size, 1, setup );
		copyMaterial ??= Material.FromShader( "shaders/copytexture.shader" );

		var ui = setup.EnableUI ?? camera.RenderUI;
		var flipX = setup.FlipX == true;
		var flipY = setup.FlipY == true;
		gpuView.Target = RenderTarget.From( target );
		gpuView.Viewport = viewport;
		gpuView.SrgbWrite = !Source2Formats.From( target.ImageFormat ).IsFloat();
		using var _ = OutputTiming.Scope();
		using ( Graphics.Scope.CreateOnView( backend, gpuView ) )
		{
			gpuView.Bind();
			DrawOutput( camera, frame, ui, encode: false, flipX, flipY );
		}

		gpuView.SrgbWrite = false;
		frame.Release();
	}

	/// <summary>
	/// A camera's output in the block being recorded, its target bound: the frame copied in (or the background, without one),
	/// then the stages that draw over the finished frame. <paramref name="encode"/> is for an 8-bit target, bound through a
	/// linear view: the copy and the UI write gamma-encoded output into it, as native's UI hook layers do over an sRGB swap
	/// chain (ui_gamma_blend).
	/// </summary>
	void DrawOutput( SceneCamera camera, BasaltGpuFrame frame, bool ui, bool encode, bool flipX, bool flipY )
	{
		if ( frame is not null ) CopyFrame( frame, encode, flipX, flipY );
		else if ( (camera.ClearFlags & ClearFlags.Color) != 0 ) Graphics.Clear( camera.BackgroundColor, true, false, false );

		// Early UI draws here too, over the finished frame, not before post processing
		Graphics.Attributes.Set( UIGammaOutput, encode );
		if ( ui && camera.Attributes.GetBool( "enableEarlyUI" ) ) RunStage( camera, Stage.EarlyUI );

		Graphics.Attributes.Set( UIGammaOutput, false );
		RunStage( camera, Stage.AfterPostProcess );

		Graphics.Attributes.Set( UIGammaOutput, encode );
		if ( ui ) RunStage( camera, Stage.UI );
		RunStage( camera, Stage.AfterUI );
	}

	/// <summary>
	/// The camera's scene rendered by Basalt on the Basalt.Gpu device, then its stages after the scene and its post processing
	/// over that frame. Null if it couldn't render. With <paramref name="samples"/> over one the scene is multisampled, as the
	/// game's anti-aliasing setting asks, and the stages get its resolved colour.
	/// </summary>
	BasaltGpuFrame RenderScene( BasaltGpuRenderBackend backend, SceneCamera camera, Vector2 size, int samples, in ViewSetup setup )
	{
		SceneMirror mirror;
		CameraState state;
		using ( PrepareTiming.Scope() )
			(mirror, state) = Prepare( camera, setup, size );

		// Auto exposure is the port on this device: its frame update here, where native's runs, its scale this frame's
		var exposure = camera.Tonemap?.GpuExposure( backend );
		exposure?.FrameUpdate( RealTime.NowDouble );
		state.View.ToneMapScalar = exposure?.Scale ?? 1.0f;

		var targetSize = new Vector2Int( (int)size.x, (int)size.y );
		samples = Math.Max( samples, 1 );
		if ( state.GpuTarget is null || state.GpuTarget.Size != targetSize || state.GpuTarget.Samples != samples )
		{
			state.GpuTarget?.Dispose();
			state.GpuTarget = ViewTarget.Create( targetSize, samples, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Basalt.Gpu camera" );
		}

		// The stages before the scene is finished draw inside its frame; ambient occlusion runs in its stage, not early
		var frame = state.GpuFrame ??= new BasaltGpuFrame( backend );
		var stages = state.GpuStages ??= new GpuCameraStages { Run = RunStage };
		stages.Camera = camera;
		stages.System = state.System;
		stages.Frame = frame;
		stages.View = gpuView;
		camera.ScheduleEarlyPostProcess( Stage.AfterDepthPrepass, false );

		using ( SceneTiming.Scope() )
		using ( new Drawing( this, state ) )
			state.System.Render( mirror.World, state.View, state.GpuTarget, stages );

		frame.Adopt( state.GpuTarget, state.System.Frame.Constants );

		gpuView.LayerType = SceneLayerType.Translucent;
		using var _ = PostTiming.Scope();
		using ( frame.Begin( gpuView ) )
		{
			RunStage( camera, Stage.AfterTransparent );
			RunStage( camera, Stage.AfterViewmodel );
			RunStage( camera, Stage.BeforePostProcess );
			if ( exposure is not null ) frame.CountExposure( exposure );
			RunStage( camera, Stage.Tonemapping );
		}

		frame.End();
		return frame;
	}

	/// <summary>
	/// The frame's colour into the bound target's viewport (<c>copytexture.shader</c>), encoded to sRGB when
	/// <paramref name="encode"/>, mirrored as asked.
	/// </summary>
	void CopyFrame( BasaltGpuFrame frame, bool encode, bool flipX, bool flipY )
	{
		copyAttributes ??= RenderAttributes.CreateManaged();
		copyAttributes.Set( CopySource, frame.Color );
		copyAttributes.Set( FlipXName, flipX );
		copyAttributes.Set( FlipYName, flipY );
		copyAttributes.SetCombo( SrgbTransfer, encode ? 1 : 0 );
		Graphics.Blit( copyMaterial, copyAttributes );
	}

	readonly HashSet<(int, Stage)> stageFailures = new();

	/// <summary>A camera's stage, in the block being recorded. One that throws is logged once, and the frame goes on.</summary>
	void RunStage( SceneCamera camera, Stage stage )
	{
		try
		{
			((IManagedCamera)camera).OnRenderStage( stage );
		}
		catch ( Exception e )
		{
			if ( stageFailures.Add( (camera._cameraId, stage) ) ) Log.Warning( e, $"Basalt.Gpu: {camera.Name}'s {stage} stage: {e.Message}" );
		}
	}
}
