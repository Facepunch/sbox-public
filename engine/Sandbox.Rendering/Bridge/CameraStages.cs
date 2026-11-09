using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Dispatches camera stages through <c>IManagedCamera.OnRenderStage</c> using a managed Graphics view.
/// Handles auto exposure and gamma-space UI blending (<see cref="Pipeline.HookLayers.AddUI"/>).
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
		camera.ScheduleEarlyPostProcess( Stage.AfterDepthPrepass, false );
		view.PostProcessEnabled = camera.EnablePostProcessing;
		view.ToolsVisMode = renderView.ToolsVisMode;

		mainView = renderView;
		SetMainCamera();

		// Custom objects draw through the same view
		renderView.HostView = view;

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

	// What the context's attributes held before the stage running on this thread
	[ThreadStatic] static CRenderAttributes s_savedAttributes;

	/// <summary>
	/// Start a stage on the context's attributes, inheriting the frame's, and remember what they hold. Native renders each
	/// hook layer on a context of its own (<c>ProcessProceduralLayer</c>), so what a stage's command lists set on
	/// <see cref="Graphics.Attributes"/> stays in that stage. Here stages share their segment's context: without
	/// <see cref="EndStageAttributes"/>, one stage's attributes reached the next - Sandbox's HUD paint list sets
	/// <c>UIFrameGrabEncoded</c> after the viewmodel, and its composite in the UI stage then decoded a linear frame grab and
	/// darkened the scene under it.
	/// </summary>
	static CRenderAttributes BeginStageAttributes( IRenderContext context, in StageTarget target )
	{
		var attributes = context.GetAttributesPtrForModify();
		attributes.SetParent( target.Attributes.Engine().Get() );

		if ( s_savedAttributes.IsNull ) s_savedAttributes = CRenderAttributes.Create();
		s_savedAttributes.Clear( false, true );
		attributes.MergeToPtr( s_savedAttributes );
		return attributes;
	}

	/// <summary>
	/// Put the context's attributes back as they were before the stage, so what it set doesn't reach later stages or the
	/// frame's own passes recorded after it on the same context. Clearing keeps the parent pointer but empties each typed
	/// list's own (<c>CUtlAttributeList::Clear</c>), so the parent is set again: without that, the frame's later passes lost
	/// what they inherit, and the game overlay drew behind the world.
	/// </summary>
	static void EndStageAttributes( CRenderAttributes attributes, in StageTarget target )
	{
		attributes.Clear( false, false );
		attributes.SetParent( target.Attributes.Engine().Get() );
		s_savedAttributes.MergeToPtr( attributes );
	}

	public bool HasEarlyPostProcess( RenderStage stage, StageQueue queue ) => Camera?.HasEarlyPostProcess( (Stage)stage, (PostProcessQueue)queue ) ?? false;

	/// <summary>
	/// Independent camera effects on their selected queue, with the frame's viewport and format but no targets bound.
	/// Compute-queue barriers name compute stages only (<see cref="Graphics.ManagedView.ComputeQueue"/>).
	/// </summary>
	public void RenderEarlyPostProcess( RenderStage stage, StageQueue queue, in StageTarget target )
	{
		if ( Camera is null ) return;

		SetMainCamera();

		var context = target.Context.NativeContext();
		var attributes = BeginStageAttributes( context, target );

		view.Color = default;
		view.Depth = default;
		view.SrgbWrite = false;
		view.TargetSrgbWrite = true;
		view.Viewport = target.Viewport;
		view.MinZ = target.MinZ;
		view.MaxZ = target.MaxZ;
		view.ColorFormat = Source2Formats.ToEngineOrDefault( target.ColorFormat );
		view.Msaa = SampleCount( target.Samples );
		view.ComputeQueue = queue == StageQueue.AsyncCompute;

		try
		{
			using ( new Graphics.Scope( context, view ) )
			{
				Camera.RenderEarlyPostProcess( (Stage)stage, (PostProcessQueue)queue );
			}
		}
		finally
		{
			view.ComputeQueue = false;
			EndStageAttributes( attributes, target );
		}
	}

	public bool HasScreenSpaceReflections => Camera?.ScreenSpaceReflections is not null;

	public void RenderScreenSpaceReflections( bool history, in StageTarget target )
	{
		if ( Camera?.ScreenSpaceReflections is not { } reflections ) return;

		SetMainCamera();

		var context = target.Context.NativeContext();
		var attributes = BeginStageAttributes( context, target );

		view.Color = history ? target.Color.Native() : default;
		view.Depth = history ? target.Depth.Native() : default;
		view.SrgbWrite = false;
		view.TargetSrgbWrite = true;
		view.Viewport = target.Viewport;
		view.MinZ = target.MinZ;
		view.MaxZ = target.MaxZ;
		view.ColorFormat = Source2Formats.ToEngineOrDefault( target.ColorFormat );
		view.Msaa = SampleCount( target.Samples );

		try
		{
			using ( new Graphics.Scope( context, view ) )
			{
				if ( history ) reflections.CaptureHistory();
				else reflections.Trace();
			}
		}
		finally
		{
			EndStageAttributes( attributes, target );
		}
	}

	public void Render( RenderStage stage, in StageTarget target )
	{
		if ( Camera is null ) return;

		SetMainCamera();

		// Skip empty early UI (CameraComponent.HasEarlyUI).
		if ( stage == RenderStage.EarlyUI && !Camera.Attributes.GetBool( EnableEarlyUI ) ) return;

		var context = target.Context.NativeContext();

		// Inherit frame constants, lighting and bindless descriptors.
		var contextAttributes = BeginStageAttributes( context, target );

		// Query HDR luminance before tonemapping, with HDR colour unbound.
		if ( stage == RenderStage.Tonemapping && tonemap.IsValid )
		{
			RenderTools.BindColorAndDepthTarget( context, default, target.Depth.Native(), false );
			var rect = target.Viewport;
			RenderTools.RenderAutoExposure( context, tonemap, target.HdrColor.Native(), (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height );
		}

		view.Color = target.Color.Native();
		view.Depth = target.Depth.Native();
		view.SrgbWrite = target.SrgbWrite;
		view.Viewport = target.Viewport;
		view.MinZ = target.MinZ;
		view.MaxZ = target.MaxZ;
		view.ColorFormat = Source2Formats.ToEngineOrDefault( target.ColorFormat );
		view.Msaa = SampleCount( target.Samples );

		// Gamma UI uses a linear view of sRGB bytes (Native.HookLayers.AddUI).
		// Float targets need encoded scratch, decoded back after UI drawing.
		var ui = stage is RenderStage.UI or RenderStage.EarlyUI;
		var gamma = ui && RenderPipeline.UIGammaBlend;
		RenderTarget scratch = null;

		if ( gamma && target.ColorFormat.IsFloat() )
		{
			scratch = RenderTarget.GetTemporary( (int)target.Size.x, (int)target.Size.y, ImageFormat.RGBA16161616F, ImageFormat.None );
			var rect = target.Viewport;
			RenderTools.BindColorAndDepthTarget( context, scratch.ColorTarget.native, default, false );
			RenderTools.BlitTexture( context, target.Color.Native(), (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height, 1 );

			view.Color = scratch.ColorTarget.native;
			view.Depth = default;
			view.Msaa = MultisampleAmount.MultisampleNone;
			contextAttributes.SetIntValue( UIFrameGrabEncoded, 1 );
		}

		// Native's UI hook layers have no depth target
		if ( ui ) view.Depth = default;
		if ( gamma ) view.SrgbWrite = false;
		view.TargetSrgbWrite = !gamma;
		if ( ui ) contextAttributes.SetIntValue( UIGammaOutput, gamma ? 1 : 0 );

		using ( new Graphics.Scope( context, view ) )
		{
			((IManagedCamera)Camera).OnRenderStage( (Stage)stage );
		}

		// Before the pipeline texture upload below, which the frame's later passes read from the context's attributes
		EndStageAttributes( contextAttributes, target );

		// AO and SSR name their results in pipeline texture slots after the prepass, which native uploads at frame end
		// (UpdateGlobalPerFrameDescriptorBindings) - after this frame is submitted. Upload them here so its forward draws read
		// this frame's, as native's views do.
		if ( stage == RenderStage.AfterDepthPrepass ) CSceneSystem.UploadPipelineTextureIndices( context );

		if ( scratch is not null )
		{
			var rect = target.Viewport;
			RenderTools.BindColorAndDepthTarget( context, target.Color.Native(), default, target.SrgbWrite );
			RenderTools.BlitTexture( context, scratch.ColorTarget.native, (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height, 2 );
			scratch.Dispose();
		}
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
