using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.SceneRenderer.Bridge;

/// <summary>
/// Dispatches camera stages through <c>IManagedCamera.OnRenderStage</c> using a managed Graphics view.
/// Handles auto exposure and gamma-space UI blending (<c>CManagedRenderPipeline::OnUIHook</c>).
/// </summary>
internal sealed class CameraStages : IRenderStages, IDisposable
{
	static readonly StringToken UIGammaOutput = new( "UIGammaOutput" );
	static readonly StringToken EnableEarlyUI = new( "enableEarlyUI" );
	static readonly StringToken UIFrameGrabEncoded = new( "UIFrameGrabEncoded" );

	readonly Graphics.ManagedView view = new();

	/// <summary>
	/// What <c>Graphics.Render( SceneObject )</c> draws with in the camera's frame (<see cref="Graphics.ManagedView.RenderSceneObject"/>).
	/// </summary>
	public Action<SceneObject, Transform, Color, Material, RenderAttributes> RenderSceneObject
	{
		set => view.RenderSceneObject = value;
	}

	/// <summary>
	/// The camera whose stages these are.
	/// </summary>
	public SceneCamera Camera { get; private set; }

	/// <summary>
	/// Optional auto-exposure system (<c>SceneCamera.GatherTonemapper</c>).
	/// </summary>
	ITonemapSystem tonemap;

	/// <summary>
	/// Prepare camera state and return exposure (<c>CCameraRenderer</c>).
	/// Disable <paramref name="updateExposure"/> for comparisons where native already advanced it.
	/// </summary>
	public float BeginFrame( SceneCamera camera, RenderView renderView, bool updateExposure )
	{
		Camera = camera;
		view.PostProcessEnabled = camera.EnablePostProcessing;
		view.ToolsVisMode = renderView.ToolsVisMode;

		mainView = renderView;
		SetMainCamera();

		// Custom objects draw through the same view
		renderView.GraphicsView = view;

		tonemap = camera.Tonemap?.Enabled == true && camera.ToneMapping is { } toneMapping ? toneMapping.GetNative() : default;
		return tonemap.IsValid ? RenderTools.TonemapFrameUpdate( tonemap, updateExposure ) : 1.0f;
	}

	RenderView mainView;

	/// <summary>
	/// Restore the main camera and forward layer for stage rendering.
	/// </summary>
	void SetMainCamera()
	{
		var viewport = mainView.Viewport;
		var aspect = viewport.Height > 0 ? viewport.Width / viewport.Height : 1;
		view.SetCamera( Camera.Position, Camera.Rotation, Camera.FieldOfView, Camera.ZNear, Camera.ZFar, aspect, Camera.Ortho, mainView.OrthoSize.x, mainView.OrthoSize.y );
		view.ShaderMode = ForwardMode;
		view.LayerType = SceneLayerType.Translucent;
		view.DepthSlice = 0;
	}

	static readonly StringToken ForwardMode = new( "Forward" );

	public bool HasAsyncCompute( Stage stage ) => Camera?.HasAsyncCompute( stage ) ?? false;

	/// <summary>
	/// The camera's compute-only effects at <paramref name="stage"/> (AO), on the async compute queue: a view with the frame's
	/// viewport and format but no targets, whose barriers name compute stages (<see cref="Graphics.ManagedView.ComputeQueue"/>).
	/// </summary>
	public void RenderAsyncCompute( Stage stage, in StageTarget target )
	{
		if ( Camera is null ) return;

		SetMainCamera();

		var context = target.Context;
		context.GetAttributesPtrForModify().SetParent( target.Attributes.Get() );

		view.Color = default;
		view.Depth = default;
		view.SrgbWrite = false;
		view.Viewport = target.Viewport;
		view.MinZ = target.MinZ;
		view.MaxZ = target.MaxZ;
		view.ColorFormat = target.ColorFormat;
		view.Msaa = SampleCount( target.Samples );
		view.ComputeQueue = true;

		try
		{
			using ( new Graphics.Scope( context, view ) )
			{
				Camera.RenderAsyncCompute( stage );
			}
		}
		finally
		{
			view.ComputeQueue = false;
		}
	}

	public void Render( Stage stage, in StageTarget target )
	{
		if ( Camera is null ) return;

		SetMainCamera();

		// Skip empty early UI (CameraComponent.HasEarlyUI).
		if ( stage == Stage.EarlyUI && !Camera.Attributes.GetBool( EnableEarlyUI ) ) return;

		var context = target.Context;

		// Inherit frame constants, lighting and bindless descriptors.
		var contextAttributes = context.GetAttributesPtrForModify();
		contextAttributes.SetParent( target.Attributes.Get() );

		// Query HDR luminance before tonemapping, with HDR colour unbound.
		if ( stage == Stage.Tonemapping && tonemap.IsValid )
		{
			RenderTools.BindColorAndDepthTarget( context, default, target.Depth, false );
			var rect = target.Viewport;
			RenderTools.RenderAutoExposure( context, tonemap, target.HdrColor, (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height );
		}

		view.Color = target.Color;
		view.Depth = target.Depth;
		view.SrgbWrite = target.SrgbWrite;
		view.Viewport = target.Viewport;
		view.MinZ = target.MinZ;
		view.MaxZ = target.MaxZ;
		view.ColorFormat = target.ColorFormat;
		view.Msaa = SampleCount( target.Samples );

		// Gamma UI uses a linear view of sRGB bytes (CManagedRenderPipeline::OnUIHook).
		// Float targets need encoded scratch, decoded back after UI drawing.
		var ui = stage is Stage.UI or Stage.EarlyUI;
		var gamma = ui && ConsoleSystem.GetValue( "ui_gamma_blend", "1" ) != "0";
		RenderTarget scratch = null;

		if ( gamma && ViewTarget.IsFloatFormat( target.ColorFormat ) )
		{
			scratch = RenderTarget.GetTemporary( (int)target.Size.x, (int)target.Size.y, ImageFormat.RGBA16161616F, ImageFormat.None );
			var rect = target.Viewport;
			RenderTools.BindColorAndDepthTarget( context, scratch.ColorTarget.native, default, false );
			RenderTools.BlitTexture( context, target.Color, (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height, 1 );

			view.Color = scratch.ColorTarget.native;
			view.Depth = default;
			view.Msaa = MultisampleAmount.MultisampleNone;
			contextAttributes.SetIntValue( UIFrameGrabEncoded, 1 );
		}

		// Native's UI hook layers have no depth target
		if ( ui ) view.Depth = default;
		if ( gamma ) view.SrgbWrite = false;
		if ( ui ) contextAttributes.SetIntValue( UIGammaOutput, gamma ? 1 : 0 );

		using ( new Graphics.Scope( context, view ) )
		{
			((IManagedCamera)Camera).OnRenderStage( stage );
		}

		// AO and SSR name their results in pipeline texture slots after the prepass, which native uploads at frame end
		// (UpdateGlobalPerFrameDescriptorBindings) - after this frame is submitted. Upload them here so its forward draws read
		// this frame's, as native's views do.
		if ( stage == Stage.AfterDepthPrepass ) CSceneSystem.UploadPipelineTextureIndices( context );

		if ( scratch is not null )
		{
			var rect = target.Viewport;
			RenderTools.BindColorAndDepthTarget( context, target.Color, default, target.SrgbWrite );
			RenderTools.BlitTexture( context, scratch.ColorTarget.native, (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height, 2 );
			contextAttributes.SetIntValue( UIFrameGrabEncoded, 0 );
			scratch.Dispose();
		}

		if ( ui ) contextAttributes.SetIntValue( UIGammaOutput, 0 );
	}

	static MultisampleAmount SampleCount( int samples ) => samples switch
	{
		2 => MultisampleAmount.Multisample2x,
		4 => MultisampleAmount.Multisample4x,
		8 => MultisampleAmount.Multisample8x,
		16 => MultisampleAmount.Multisample16x,
		_ => MultisampleAmount.MultisampleNone,
	};

	public void Dispose() => view.Dispose();
}
