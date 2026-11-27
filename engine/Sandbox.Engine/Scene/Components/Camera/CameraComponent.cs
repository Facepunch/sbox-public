
using NativeEngine;
using Sandbox.Engine;
using Sandbox.Rendering;
using Sandbox.UI;
using System.Drawing;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Every scene should have at least one Camera.
/// </summary>
[Expose]
[Title( "Camera" )]
[Category( "Rendering" )]
[Icon( "videocam" )]
[EditorHandle( "materials/gizmo/camera.png" )]
public sealed partial class CameraComponent : Component, Component.ExecuteInEditor, Component.ISceneStage
{
	SceneCamera sceneCamera;
	SceneCamera _projectionCamera;

	/// <summary>
	/// The clear flags for this camera.
	/// </summary>
	[Property]
	public ClearFlags ClearFlags { get; set; } = ClearFlags.Color | ClearFlags.Stencil | ClearFlags.Depth;

	/// <summary>
	/// The background color of this camera's view if there's no 2D Sky in the scene.
	/// </summary>
	[Property]
	public Color BackgroundColor { get; set; } = "#557685";

	bool _isMainCamera = true;
	int _priority = 1;

	/// <summary>
	/// Returns true if this is the main game camera.
	/// </summary>
	[Property]
	public bool IsMainCamera
	{
		get => _isMainCamera;
		set
		{
			if ( _isMainCamera == value )
				return;

			_isMainCamera = value;
			Scene.UpdateMainCamera();
		}
	}

	/// <summary>
	/// The axis to use for the field of view.
	/// </summary>
	[Property, Title( "FOV Axis" ), HideIf( nameof( Orthographic ), true )]
	public Axis FovAxis { get; set; } = Axis.Horizontal;

	/// <summary>
	/// The field of view of this camera.
	/// </summary>
	[Property, Range( 1, 179 ), HideIf( nameof( Orthographic ), true )]
	public float FieldOfView { get; set; } = 60;

	/// <summary>
	/// The camera's near clip plane distance. This is the closest distance this camera will be able to render.
	/// A good value for this is about 5. Below 5 and particularly below 1 you're going to start to see
	/// a lot of artifacts like z-fighting.
	/// </summary>
	[Property, Range( 1, 1000 ), Step( 1 )]
	public float ZNear { get; set; } = 10;

	/// <summary>
	/// The camera's far clip plane distance. This is the furthest distance this camera will be able to render.
	/// This value totally depends on the game you're making. Shorter the better, sensible ranges would be
	/// between about 1000 and 30000, but if you want it to be further out you can balance that out by making
	/// ZNear larger.
	/// </summary>
	[Property, Range( 1, 100000 ), Step( 1 )]
	public float ZFar { get; set; } = 10000;

	/// <summary>
	/// The priority of this camera. Dictates which camera gets rendered on top of another. Higher means it'll be rendered on top.
	/// </summary>
	[Property, Range( 1, 16 )]
	public int Priority
	{
		get => _priority;
		set
		{
			if ( _priority == value ) return;

			_priority = value;
			Scene.UpdateMainCamera();
		}
	}

	/// <summary>
	/// Whether or not to use orthographic projection instead of perspective.
	/// </summary>
	[Property]
	public bool Orthographic { get; set; }

	/// <summary>
	/// The orthographic size for this camera while <see cref="Orthographic"/> is set to true.
	/// </summary>
	[Property]
	public float OrthographicHeight { get; set; } = 1204;

	/// <summary>
	/// The HMD eye that this camera is targeting.
	/// Use <see cref="StereoTargetEye.None"/> for the user's monitor (i.e. the companion window).
	/// </summary>
	[Property]
	public StereoTargetEye TargetEye { get; set; } = StereoTargetEye.None;

	/// <summary>
	/// A list of tags that will be checked to include specific game objects when rendering this camera.
	/// If none are set, it will include everything.
	/// </summary>
	[Property]
	public TagSet RenderTags { get; set; } = new();

	/// <summary>
	/// A list of tags that will be checked to exclude specific game objects when rendering this camera.
	/// </summary>
	[Property]
	public TagSet RenderExcludeTags { get; set; } = new();

	/// <summary>
	/// The size of the camera represented on the screen. Normalized between 0 and 1.
	/// </summary>
	[Property, Range( 0, 1, slider: false )]
	public Vector4 Viewport { get; set; } = new( 0.0f, 0.0f, 1.0f, 1.0f ); // todo this should use a rect

	/// <summary>
	/// Render this camera without anti-aliasing, whatever the player's graphics settings. Their render resolution and FSR1
	/// upscaling still apply.
	/// </summary>
	[Property]
	public bool DisableAntiAliasing { get; set; }

	/// <summary>
	/// If specified, this camera will render to this RenderTexture instead of the screen.
	/// This can then be used in other stuff like materials.
	/// </summary>
	[Property]
	public RenderTextureAsset RenderTexture;

	private Texture _renderTarget;

	/// <summary>
	/// The texture to draw this camera to.
	/// Requires <see cref="Texture.CreateRenderTarget()"/>
	/// </summary>
	[JsonIgnore, Hide]
	public Texture RenderTarget
	{
		get => RenderTexture?.Texture ?? _renderTarget;
		set
		{
			if ( _renderTarget == value )
				return;

			if ( value is not null )
			{
				if ( !value.Desc.m_nFlags.HasFlag( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_RENDER_TARGET ) )
					throw new Exception( $"{nameof( RenderTarget )} texture needs to be a render target" );
			}

			_renderTarget = value;
		}
	}

	/// <summary>
	/// Render this camera using a different render mode
	/// </summary>
	[JsonIgnore, Hide]
	public SceneCameraDebugMode DebugMode { get; set; }

	/// <summary>
	/// Render this camera using a wireframe view.
	/// </summary>
	[JsonIgnore, Hide]
	public bool WireframeMode { get; set; }

	/// <summary>
	/// Accessor for getting this Camera Component's SceneCamera
	/// </summary>
	internal SceneCamera SceneCamera
	{
		get => sceneCamera;
	}

	public override void Reset()
	{
		base.Reset();

		RenderTags = new();
		RenderExcludeTags = new();
		Viewport = new( 0.0f, 0.0f, 1.0f, 1.0f );
		ClearFlags = ClearFlags.Color | ClearFlags.Stencil | ClearFlags.Depth;
	}

	private void EnsureSceneCameraCreated()
	{
		if ( sceneCamera is not null ) return;

		// scene should be scoped in!
		Assert.NotNull( Scene );
		if ( Game.IsPlaying )
		{
			// Only assert active scene in play mode, since we want to create cameras anywhere in the editor
			Assert.AreEqual( Game.ActiveScene, Scene, "Camera scene is not active scene" );
		}

		// Use the GameObject name so we can identify the camera when debugging
		sceneCamera = new( GameObject.Name );

		SetRenderHooks( sceneCamera );
	}

	protected override void OnAwake()
	{
		EnsureSceneCameraCreated();
		Scene.Cameras.Add( this );
	}

	protected override void OnDestroy()
	{
		Scene.Cameras.Remove( this );
		sceneCamera?.Dispose();
		sceneCamera = null;
		_projectionCamera?.Dispose();
		_projectionCamera = null;
		_hudCommandList.DisposePainter();
		_overlayCommandList.DisposePainter();
	}

	protected override void DrawGizmos()
	{
		if ( sceneCamera is null )
			return;

		using var scope = Gizmo.PushScope( "camera" );

		Gizmo.Transform = global::Transform.Zero;

		var frustum = GetFrustum( new Rect( 0f, 0f, 1920f, 1080f ), new Vector2( 1920, 1080 ) );

		Gizmo.Draw.Color = Color.White.WithAlpha( 0.4f );
		Gizmo.Draw.LineFrustum( frustum );
	}

	/// <summary>
	/// The view this camera composed this frame - the base transform reshaped by every
	/// <see cref="ICameraModifier"/>, before transient render effects. Query
	/// <see cref="CameraView.FieldOfView"/> from here for zoom-aware logic like scaling look
	/// sensitivity while scoped. Final from the camera stage of the tick (after Update, before
	/// PreRender) - reading it during Update gives the previous frame's composition.
	/// </summary>
	public CameraView View { get; private set; }

	bool _composed;
	CameraView _composedBase;
	CameraView _renderView;

	/// <summary>
	/// Compose this camera's view - the base transform reshaped by every <see cref="ICameraModifier"/>
	/// (published on <see cref="View"/>), then transient effects baked on top for the render. The
	/// scene composes every camera once per tick, after Update and bone merging and before PreRender,
	/// so anything in PreRender positions against a settled camera. Call it yourself only when you're rendering
	/// without ticking the scene and want the composition - otherwise a camera moved after composing
	/// just renders from its raw transform.
	/// </summary>
	public void ComposeView()
	{
		ComposeView( Scene.IsEditor ? null : GatherModifiers( Scene, new() ) );
	}

	// The scene-wide modifier set, sorted - gathered once per tick and shared by every camera
	// composing that tick (see Scene.UpdateCameraViews). Null when modifiers don't apply (editor).
	// Equal orders keep scene order, like OrderBy.
	internal static List<ICameraModifier> GatherModifiers( Scene scene, List<ICameraModifier> modifiers )
	{
		modifiers.Clear();

		foreach ( var modifier in scene.Query<ICameraModifier>() )
		{
			var order = modifier.CameraOrder;
			int i = modifiers.Count;
			while ( i > 0 && modifiers[i - 1].CameraOrder > order ) i--;
			modifiers.Insert( i, modifier );
		}

		return modifiers;
	}

	internal void ComposeView( List<ICameraModifier> modifiers )
	{
		var view = RawView;

		// The modifier chain reshapes the view in order (scope zoom, vehicle roll), then gets the
		// final view to place things against (view models).
		if ( modifiers is not null )
		{
			foreach ( var modifier in modifiers )
			{
				modifier.ModifyCamera( this, ref view );
			}

			View = view;

			foreach ( var modifier in modifiers )
			{
				modifier.PostCameraSetup( this, view );
			}
		}
		else
		{
			View = view;
		}

		_renderView = view;

		// Transient camera effects (screen shake, punches) bake into the render view only - the
		// composed view and component transform are never touched, so they can't accumulate or move
		// the player's aim.
		if ( CameraEffectSystem.Get( Scene ) is { } effects )
		{
			effects.QueryOffsets( this, out var shakeOffset, out var shakeAngles, out var shakeFov );

			_renderView.Position += _renderView.Rotation * shakeOffset;
			_renderView.Rotation *= shakeAngles;
			_renderView.FieldOfView = (_renderView.FieldOfView + shakeFov).Clamp( 1f, 179f );
		}

		// What the component looked like when composition finished. A camera changed after this
		// stomps the composition when its projection is updated.
		_composedBase = RawView;

		_composed = true;
	}

	// The camera's own values, untouched by modifiers or effects.
	CameraView RawView => new()
	{
		Position = WorldPosition,
		Rotation = WorldRotation,
		FieldOfView = FieldOfView,
		ZNear = ZNear,
		ZFar = ZFar
	};

	internal void UpdateSceneCameraTransform( SceneCamera camera )
	{
		UpdateCameraProjection( camera, ScreenRect.Size );
		camera.Rect = new Rect( Viewport.x, Viewport.y, Viewport.z, Viewport.w );
	}

	// Queries use the same unjittered projection as SceneCamera without changing the camera the render thread may be drawing.
	// Reuse its frustum across queries.
	SceneCamera GetProjectionCamera( Vector2 size )
	{
		var camera = _projectionCamera ??= new( "Camera projection queries" );
		UpdateCameraProjection( camera, size );
		return camera;
	}

	void UpdateCameraProjection( SceneCamera camera, Vector2 size )
	{
		// The view composes once, at the camera stage of the tick - that's the only time modifiers
		// and effects apply. A camera that never composed, or was changed since composing (tools,
		// paused scenes, moviemakers - place a camera, render, done), stomps the composition and
		// renders from its raw values.
		var raw = RawView;

		var view = _composed && raw == _composedBase ? _renderView : raw;
		camera.Position = view.Position;
		camera.Rotation = view.Rotation;
		camera.ZNear = view.ZNear;
		camera.ZFar = view.ZFar;
		camera.Size = size;
		camera.Ortho = Orthographic;
		camera.OrthoHeight = OrthographicHeight;
		camera.SetFieldOfView( view.FieldOfView, FovAxis == Axis.Vertical );
	}

	internal void UpdateSceneCameraStereo( SceneCamera camera )
	{
		camera.TargetEye = TargetEye;
		camera.WantsStereoSubmit = !Scene.IsEditor;
	}

	/// <summary>
	/// Update a SceneCamera with the settings from this component
	/// </summary>
	Action<Stage, SceneCamera> executeCommandListsHook;
	Func<Stage, PostProcessQueue, bool> hasEarlyPostProcessHook;
	Func<Stage, bool> hasCommandListsHook;
	Func<bool> readsMotionVectorsHook;
	Action<Stage, PostProcessQueue> renderEarlyPostProcessHook;
	Func<bool, bool> deferTonemapHook;
	Func<bool, RenderAttributes, UberPostEffect> outputPostProcessHook;
	Action endOutputPostProcessHook;

	public void UpdateSceneCamera( SceneCamera camera, bool includeTags = true )
	{
		if ( Scene is null )
		{
			Log.Warning( $"Trying to update camera from {this} but has no scene" );
			return;
		}

		UpdateSceneCameraTransform( camera );

		camera.World = Scene.SceneWorld;
		camera.ClearFlags = ClearFlags;
		camera.Worlds.Clear();
		camera.DebugWorlds.Clear();
		camera.DebugWorlds.Add( Scene.DebugSceneWorld );

		// Also render any active gizmos. What the gizmo frame has drawn so far is written to its scene objects now: they're
		// otherwise written when it ends, which in the editor is after the camera has drawn, so they show a frame late
		if ( Gizmo.Active is { } gizmo )
		{
			gizmo._renderer?.Flush();
			camera.DebugWorlds.Add( gizmo.World );
		}

		// A camera (such as the editor viewport) might not want to take render tags into account
		// from an existing camera source
		if ( includeTags )
		{
			camera.RenderTags.SetFrom( RenderTags );
			camera.ExcludeTags.SetFrom( RenderExcludeTags );
			if ( Scene.IsEditor && !IsSceneEditorCamera )
			{
				camera.ExcludeTags.Add( "editor_wireframe" );
				camera.ExcludeTags.Add( "editor_overlay" );
			}
		}

		// Merge our global Scene attributes into the camera
		Scene.RenderAttributes.MergeTo( camera.Attributes );

		camera.DebugMode = DebugMode;
		camera.WireframeMode = WireframeMode;
		camera.EnablePostProcessing = EnablePostProcessing;
		camera.DisableAntiAliasing = DisableAntiAliasing;
		camera.EnableScreenSpaceReflections = true;
		camera.BackgroundColor = ClearFlags.Contains( ClearFlags.Color ) ? BackgroundColor : Color.Transparent;

		CopyPostProcessing( camera, applyCameraEffects: false );
		if ( IsSceneEditorCamera )
			Scene.Camera?.CopyPostProcessing( camera, applyCameraEffects: false );

		ApplyCameraEffects( camera );
	}

	/// <summary>
	/// Volume position after camera modifiers, before transient shake. An explicit anchor takes precedence;
	/// raw transform edits after composition use the same fallback as rendering without recomposing the camera.
	/// </summary>
	internal Vector3 PostProcessPosition => PostProcessAnchor.IsValid() ? PostProcessAnchor.WorldPosition
		: _composed && RawView == _composedBase ? View.Position : WorldPosition;

	internal void CopyPostProcessing( SceneCamera camera, bool applyCameraEffects = true )
	{
		if ( Scene is null )
		{
			Log.Warning( $"Trying to update camera from {this} but has no scene" );
			return;
		}

		// defaults - let components override
		camera.CubemapFog.Enabled = false;
		camera.Bloom.Enabled = false;

		AutoExposure.Apply( camera );

		// Don't set the background colour if we don't clear color
		// Also don't hook into render overlays, nor volumetric fog stuff.
		if ( ClearFlags.Contains( ClearFlags.Color ) )
		{
			camera.VolumetricFog.Enabled = Scene.Get<VolumetricFogVolume>() is not null;
			camera.VolumetricFog.DrawDistance = 4096;
			camera.VolumetricFog.FadeInStart = 64;
			camera.VolumetricFog.FadeInEnd = 256;
			camera.VolumetricFog.IndirectStrength = 1.0f;
			camera.VolumetricFog.Anisotropy = 1;
			camera.VolumetricFog.Scattering = 1.0f;
			camera.VolumetricFog.BakedIndirectTexture = Scene.Get<VolumetricFogController>()?.BakedFogTexture;
		}

		SetRenderHooks( camera );

		// Set with the hooks: a view decides its G-buffer from the camera whose lists it runs (the editor viewport runs the
		// main camera's). Unknown post effects and custom command lists may sample normals, so only omit them for an empty
		// stack. Scene attributes can force the G-buffer for custom material consumers outside those stages.
		camera.Attributes.Set( "NeedsDepthNormals", WantsDepthNormals() || camera.TracesScreenSpaceReflections );

		//
		// Hack because I don't want this to have to be on a camera. This
		// is hidden from users, so we'll figure out how to square it later
		//
#pragma warning disable CS0618 // Retain legacy fog support for existing content.
		foreach ( var cubemapFog in Scene.Query<CubemapFog>() )
#pragma warning restore CS0618
		{
			if ( cubemapFog.Tags.HasAny( RenderExcludeTags ) )
				continue;

			cubemapFog.SetupCamera( this, camera );
		}

		if ( applyCameraEffects )
			ApplyCameraEffects( camera );
	}

	// The editor can borrow another camera's stage hooks; cache the same delegates for both uses.
	void SetRenderHooks( SceneCamera camera )
	{
		camera.OnRenderStageHook = executeCommandListsHook ??= ExecuteCommandLists;
		camera.HasEarlyPostProcessHook = hasEarlyPostProcessHook ??= HasEarlyPostProcess;
		camera.HasCommandListsHook = hasCommandListsHook ??= HasCommandLists;
		camera.ReadsMotionVectorsHook = readsMotionVectorsHook ??= ReadsMotionVectors;
		camera.RenderEarlyPostProcessHook = renderEarlyPostProcessHook ??= RenderEarlyPostProcess;
		camera.DeferTonemapHook = deferTonemapHook ??= DeferTonemap;
		camera.OutputPostProcessHook = outputPostProcessHook ??= OutputPostProcess;
		camera.EndOutputPostProcessHook = endOutputPostProcessHook ??= EndOutputPostProcess;
	}

	/// <summary>
	/// Reset component-driven fog to legacy inheritance before applying camera effects to the completed view defaults.
	/// </summary>
	void ApplyCameraEffects( SceneCamera camera )
	{
		camera.Fog = null;
		Scene.GetSystem<PostProcessSystem>().ApplyCameraEffects( this, camera, IsSceneEditorCamera );
	}

	/// <summary>
	/// Update the SceneCamera UI with the settings from this component
	/// </summary>
	private void UpdateSceneCameraUI( SceneCamera camera )
	{
		if ( Scene is null )
			return;

		// This runs every frame, so make the callbacks once per scene camera
		if ( _renderUICamera != camera )
			CreateRenderUICallbacks( camera );

		camera.OnRenderUI = _renderUI;
		camera.OnRenderUIBeforePostProcess = _renderUIBeforePostProcess;
	}

	// Separate so the closure is only allocated here, not on every UpdateSceneCameraUI
	void CreateRenderUICallbacks( SceneCamera camera )
	{
		_renderUICamera = camera;
		_renderUI = () => OnCameraRenderUI( camera, ScreenPanel.RenderTiming.AfterPostProcess );
		_renderUIBeforePostProcess = () => OnCameraRenderUI( camera, ScreenPanel.RenderTiming.BeforePostProcess );
	}

	SceneCamera _renderUICamera;
	Action _renderUI;
	Action _renderUIBeforePostProcess;

	/// <summary>Compatibility method. No hook is registered; use a command list at <see cref="Stage.AfterOpaque"/>.</summary>
	[Obsolete( "Use CommandList" )]
	public IDisposable AddHookAfterOpaque( string debugName, int order, Action<SceneCamera> renderEffect ) => null;

	/// <summary>Compatibility method. No hook is registered; use a command list at <see cref="Stage.AfterTransparent"/>.</summary>
	[Obsolete( "Use CommandList" )]
	public IDisposable AddHookAfterTransparent( string debugName, int order, Action<SceneCamera> renderEffect ) => null;

	/// <summary>Compatibility method. No hook is registered; use a command list at <see cref="Stage.AfterPostProcess"/>.</summary>
	[Obsolete( "Use CommandList" )]
	public IDisposable AddHookBeforeOverlay( string debugName, int order, Action<SceneCamera> renderEffect ) => null;

	/// <summary>Compatibility method. No hook is registered; use a command list at <see cref="Stage.AfterUI"/>.</summary>
	[Obsolete( "Use CommandList" )]
	public IDisposable AddHookAfterUI( string debugName, int order, Action<SceneCamera> renderEffect ) => null;

	/// <summary>
	/// Anything to draw at <see cref="Stage.EarlyUI"/>? The pipeline skips that layer's passes otherwise.
	/// </summary>
	private bool HasEarlyUI()
	{
		lock ( _commandListLock )
		{
			if ( commandlists.TryGetValue( Stage.EarlyUI, out var list ) && list.Count > 0 )
				return true;
		}

		foreach ( var c in Scene.renderScreenPanels )
		{
			if ( c.Active && c.Timing == ScreenPanel.RenderTiming.BeforePostProcess && (c.TargetCamera ?? (IsMainCamera ? this : null)) == this )
				return true;
		}

		return false;
	}

	private void OnCameraRenderUI( SceneCamera camera, ScreenPanel.RenderTiming timing )
	{
		if ( Scene is null )
			return;

		// Runs on the render thread, so iterate the snapshot PreRender() took rather than
		// the live object index - walking that while the main thread edits it blows up.
		foreach ( var c in Scene.renderScreenPanels )
		{
			if ( !c.Active ) continue;
			if ( c.Timing != timing ) continue;
			var target = c.TargetCamera ?? (IsMainCamera ? this : null);
			if ( target != this ) continue;
			if ( RenderExcludeTags.HasAny( c.GameObject.Tags ) ) continue;

			c.Render();
		}
	}

	internal bool IsSceneEditorCamera;

	internal void InitializeRendering()
	{
		using ( Scene.PushScope() )
		{
			EnsureSceneCameraCreated();

			UpdateSceneCamera( sceneCamera );

			UpdateSceneCameraUI( sceneCamera );
			sceneCamera.RenderUI = true;
			sceneCamera.Attributes.Set( "enableEarlyUI", HasEarlyUI() );
			UpdateSceneCameraStereo( sceneCamera );
		}
	}

	/// <summary>
	/// This should only be called when creating render lists!!
	/// </summary>
	internal void AddToRenderList( SwapChainHandle_t swapChain, Vector2? size )
	{
		if ( PrepareForRenderList() ) DrawToRenderList( swapChain, size );
	}

	/// <summary>
	/// The camera's part of the frame's extract (<see cref="Scene.ExtractRender"/>), on the main thread: whether it draws, and
	/// its scene camera prepared for it as <see cref="AddToRenderList"/> prepares it.
	/// </summary>
	internal bool PrepareForRenderList( bool initialize = true )
	{
		if ( !Active ) return false;
		if ( Viewport.z <= 0 ) return false;
		if ( Viewport.w <= 0 ) return false;

		if ( initialize ) InitializeRendering();

		// What the frame draws into: the game may replace the target (a resize) while the render thread draws
		_preparedTarget = RenderTarget is { } target && target.native.IsValid ? target : null;
		_preparedSize = CustomSize;
		return true;
	}

	// The target and size the extract prepared the camera with (PrepareForRenderList), for the frame's drawing
	Texture _preparedTarget;
	Vector2? _preparedSize;

	/// <summary>
	/// Whether <see cref="DrawToRenderList"/> draws the camera into the window rather than into its render target.
	/// </summary>
	internal bool DrawsIntoWindow => ThreadSafe.IsRenderThread ? _preparedTarget is null : RenderTarget is not { native.IsValid: true };

	/// <summary>
	/// The size <see cref="DrawToRenderList"/> draws the camera at, given the window's.
	/// </summary>
	internal Vector2? DrawSize( Vector2? size ) => ThreadSafe.IsRenderThread ? _preparedSize ?? size : CustomSize ?? size;

	/// <summary>
	/// Draw the camera <see cref="PrepareForRenderList"/> prepared: in the scene's scope on the main thread, without one on the
	/// render thread, which runs no game code (the scope sets the game's statics).
	/// </summary>
	internal void DrawToRenderList( SwapChainHandle_t swapChain, Vector2? size )
	{
		if ( ThreadSafe.IsRenderThread )
		{
			DrawPrepared();
			return;
		}

		using ( Scene.PushScope() ) DrawPrepared();

		void DrawPrepared()
		{
			var renderer = Rendering.SceneRenderer.Current;
			var drawSize = _preparedSize ?? size;
			// If the prepared target has since been disposed, don't draw into the window instead.
			if ( _preparedTarget is { } target )
			{
				if ( target.native.IsValid ) renderer.RenderToTexture( SceneCamera, target, drawSize, default );
				return;
			}

			SceneCamera.AddToRenderList( renderer, swapChain, drawSize );
		}
	}

	public Vector2 PointToScreenNormal( in Vector3 worldPosition )
	{
		return GetProjectionCamera( ScreenRect.Size ).ToScreenNormal( worldPosition );
	}

	public Vector2 PointToScreenPixels( in Vector3 worldPosition )
	{
		var size = ScreenRect.Size;
		return GetProjectionCamera( size ).ToScreenNormal( worldPosition ) * size;
	}

	/// <summary>
	/// The size of the viewport, in screen coordinates
	/// </summary>
	public Rect ScreenRect
	{
		get
		{
			var ss = CustomSize ?? Screen.Size;
			return new Rect( ss.x * Viewport.x, ss.y * Viewport.y, ss.x * Viewport.z, ss.y * Viewport.w );
		}
	}

	/// <summary>
	/// Given a BBox in world space, will return the screen space rect that totally contains the box.
	/// </summary>
	public Rect BBoxToScreenPixels( BBox bounds, out bool isBehind )
	{
		Span<Vector2> corners = stackalloc Vector2[8];
		Vector3 min = bounds.Mins;
		Vector3 max = bounds.Maxs;
		var size = ScreenRect.Size;
		var camera = GetProjectionCamera( size );

		Vector2 Project( Vector3 point ) => camera.ToScreenNormal( point ) * size;

		isBehind = true;

		corners[0] = Project( new Vector3( min.x, min.y, min.z ) );
		corners[1] = Project( new Vector3( max.x, min.y, min.z ) );
		corners[2] = Project( new Vector3( min.x, max.y, min.z ) );
		corners[3] = Project( new Vector3( min.x, min.y, max.z ) );
		corners[4] = Project( new Vector3( max.x, max.y, min.z ) );
		corners[5] = Project( new Vector3( min.x, max.y, max.z ) );
		corners[6] = Project( new Vector3( max.x, min.y, max.z ) );
		corners[7] = Project( new Vector3( max.x, max.y, max.z ) );

		Vector2 minScreen = corners[0];
		Vector2 maxScreen = corners[0];

		for ( int i = 1; i < 8; i++ )
		{
			minScreen = Vector2.Min( minScreen, corners[i] );
			maxScreen = Vector2.Max( maxScreen, corners[i] );
		}

		var ss = CustomSize ?? Screen.Size;

		// off screen
		if ( maxScreen.x < 0 ) isBehind = true;
		if ( maxScreen.y < 0 ) isBehind = true;
		if ( minScreen.x > ss.x ) isBehind = true;
		if ( minScreen.y > ss.y ) isBehind = true;

		return Rect.FromPoints( minScreen, maxScreen );
	}

	public Vector2 PointToScreenPixels( Vector3 worldPosition, out bool isBehind )
	{
		var size = ScreenRect.Size;
		var v = GetProjectionCamera( size ).ToScreenWithDirection( worldPosition );
		isBehind = v.z <= 0.0f;
		return new Vector2( v.x, v.y ) * size;
	}

	public Vector2 PointToScreenNormal( Vector3 worldPosition, out bool isBehind )
	{
		var v = GetProjectionCamera( ScreenRect.Size ).ToScreenWithDirection( worldPosition );
		isBehind = v.z <= 0.0f;
		return new Vector2( v.x, v.y );
	}

	/// <summary>
	/// Returns a ray created from the specified cursor position on the screen, by conversion into 3D space.
	/// </summary>
	public Ray ScreenPixelToRay( Vector2 pixelPosition )
	{
		var size = ScreenRect.Size;
		return GetProjectionCamera( size ).GetRay( pixelPosition, size );
	}

	public Ray ScreenNormalToRay( Vector3 normalPosition )
	{
		var size = ScreenRect.Size;
		var pixelPosition = new Vector2( normalPosition.x, normalPosition.y ) * size;
		return GetProjectionCamera( size ).GetRay( pixelPosition, size );
	}

	/// <summary>
	/// Convert from screen coords to world coords on the near frustum plane.
	/// </summary>
	public Vector3 ScreenToWorld( Vector2 screen )
	{
		return GetProjectionCamera( ScreenRect.Size ).ToWorld( screen );
	}

	protected override void OnEnabled()
	{
		Scene?.UpdateMainCamera();

		AddCommandList( _hudCommandList, Rendering.Stage.AfterPostProcess, 5000 );
		AddCommandList( _overlayCommandList, Rendering.Stage.AfterUI, 5000 );

	}

	protected override void OnDisabled()
	{
		Scene?.UpdateMainCamera();

		RemoveCommandList( _hudCommandList );
		RemoveCommandList( _overlayCommandList );

	}

	/// <summary>
	/// Returns the view frustum of the current screen rect.
	/// </summary>
	public Frustum GetFrustum()
	{
		var rect = ScreenRect;
		return GetProjectionCamera( rect.Size ).GetFrustum( rect );
	}

	/// <summary>
	/// Given a pixel rect return a frustum on the current camera.
	/// </summary>
	public Frustum GetFrustum( Rect screenRect )
	{
		return GetProjectionCamera( ScreenRect.Size ).GetFrustum( screenRect );
	}

	/// <summary>
	/// Given a pixel rect return a frustum on the current camera. Pass in 1 to ScreenSize to use normalized screen coords.
	/// </summary>
	public Frustum GetFrustum( Rect screenRect, Vector3 screenSize )
	{
		return GetProjectionCamera( ScreenRect.Size ).GetFrustum( screenRect, screenSize );
	}

	/// <summary>
	/// Render scene to a texture from this camera's point of view
	/// </summary>
	public bool RenderToTexture( Texture target, in ViewSetup config = default )
	{
		return RenderToTexture( target, null, config );
	}

	/// <summary>
	/// Render colour and depth into caller-owned textures without copying the depth buffer.
	/// The optional depth target must be a renderable 2D depth texture with eight stencil bits and the same dimensions
	/// as the colour target. Its sample count determines the scene's MSAA sample count, so it must have one sample when
	/// <see cref="DisableAntiAliasing"/> is set.
	/// Depth retains the camera's projection and orientation: <see cref="ViewSetup.FlipX"/> and
	/// <see cref="ViewSetup.FlipY"/> only affect the final colour blit. Keep both textures alive
	/// until rendering and any subsequent sampling have completed.
	/// </summary>
	public bool RenderToTexture( Texture target, Texture depthTarget, in ViewSetup config )
	{
		return RenderToTexture( target, depthTarget, config, out _ );
	}

	/// <summary>
	/// Submit a camera view and copy its resolved projection and viewport for deferred consumers.
	/// </summary>
	internal bool RenderToTexture( Texture target, Texture depthTarget, in ViewSetup config, out ViewData viewData )
	{
		viewData = default;

		if ( target is null || target.native.IsNull )
			return false;

		if ( depthTarget is not null )
		{
			if ( !depthTarget.IsValid || depthTarget.IsError || !depthTarget.IsRenderTarget
				|| !depthTarget.ImageFormat.HasCameraStencil() || depthTarget.Depth != 1 )
				throw new ArgumentException( "Depth must be a valid 2D depth render target with eight stencil bits.", nameof( depthTarget ) );

			if ( depthTarget.Size != target.Size )
				throw new ArgumentException( "Colour and depth targets must have matching dimensions.", nameof( depthTarget ) );

			if ( DisableAntiAliasing && depthTarget.MultisampleType != RenderMultisampleType.RENDER_MULTISAMPLE_NONE )
				throw new ArgumentException( "A camera without anti-aliasing can't render into a multisampled depth target.", nameof( depthTarget ) );
		}

		// Drawing blocks already have a prepared scene; don't run game preparation from their render jobs.
		if ( !Graphics.IsActive )
		{
			Scene.PrepareSceneForRendering();
			InitializeRendering();
		}

		return sceneCamera.RenderToTexture( target, depthTarget, config, out viewData );
	}

	/// <summary>
	/// Allows specifying a custom projection matrix for this camera
	/// </summary>
	public Matrix? CustomProjectionMatrix
	{
		get => SceneCamera.CustomProjectionMatrix;
		set => SceneCamera.CustomProjectionMatrix = value;
	}

	/// <summary>
	/// Allows specifying a custom aspect ratio for this camera.
	/// By default (or when null) the camera size is screen size or render target size.
	/// </summary>
	[JsonIgnore, Hide]
	public Vector2? CustomSize { get; set; }

	/// <summary>
	/// Get the frustum projection matrix for the component's current view.
	/// </summary>
	public Matrix ProjectionMatrix => GetProjectionCamera( ScreenRect.Size ).ProjectionMatrix;

	/// <summary>
	/// Calculates a projection matrix with an oblique clip-plane defined in world space.
	/// </summary>
	public Matrix CalculateObliqueMatrix( Plane clipPlane )
	{
		var tx = WorldTransform;
		var normal = tx.Rotation.Inverse * clipPlane.Normal;
		normal = new Vector3( normal.y, -normal.z, normal.x ).Normal;

		System.Numerics.Matrix4x4 m = ProjectionMatrix;

		Vector4 q = default;

		q.x = (MathF.Sign( normal.x ) - m.M13) / m.M11;
		q.y = (MathF.Sign( normal.y ) - m.M23) / m.M22;
		q.z = 1f;
		q.w = (1f - m.M33) / m.M34;

		var plane = new Vector4( normal, Vector3.Dot( tx.Position - clipPlane.Position, clipPlane.Normal ) );
		var c = plane * (1.0f / System.Numerics.Vector4.Dot( plane, q ));

		m.M31 = -c.x;
		m.M32 = -c.y;
		m.M33 = -c.z;
		m.M34 = c.w;

		return m;
	}

	public enum Axis
	{
		/// <summary>
		/// Fits the view within the x-axis.
		/// </summary>
		[Icon( "panorama_horizontal" )]
		Horizontal,

		/// <summary>
		/// Fits the view within the y-axis.
		/// </summary>
		[Icon( "panorama_vertical" )]
		Vertical
	}

	/// <summary>
	/// Render this camera to the target bitmap.
	/// </summary>
	public void RenderToBitmap( Bitmap targetBitmap ) => RenderToBitmap( targetBitmap, true );

	/// <summary>
	/// Render this camera to the target bitmap, with the option to include or exclude the drawn UI.
	/// </summary>
	public void RenderToBitmap( Bitmap targetBitmap, bool renderUI )
	{
		if ( targetBitmap == null || targetBitmap.Width <= 1 || targetBitmap.Height <= 1 )
			return;

		if ( !this.IsValid() )
			return;

		using ( Scene.PushScope() )
		{
			Scene.PrepareSceneForRendering();
			InitializeRendering();
			if ( renderUI ) ResizeUI( targetBitmap.Size );
			try
			{
				SceneCamera.RenderToBitmap( targetBitmap, new ViewSetup { EnableUI = renderUI } );
			}
			finally
			{
				if ( renderUI ) ResizeUI( new Vector2( Screen.Width, Screen.Height ) );
			}
		}
	}

	private void ResizeUI( Vector2 size )
	{
		if ( Scene is null )
			return;

		bool resized = false;

		foreach ( var panel in Scene.GetAll<ScreenPanel>() )
		{
			if ( !panel.IsValid() || !panel.Active )
				continue;

			var target = panel.TargetCamera ?? (IsMainCamera ? this : null);
			if ( target != this )
				continue;

			if ( RenderExcludeTags.HasAny( panel.GameObject.Tags ) )
				continue;

			if ( panel.GetPanel() is not RootPanel rootPanel || !rootPanel.IsValid() )
				continue;

			var screenRect = new Rect( 0, 0, size.x, size.y );

			rootPanel.PreLayout( screenRect );
			rootPanel.CalculateLayout();
			rootPanel.PostLayout();


			rootPanel.BuildCommandList();
			resized = true;
		}

		// The render consumes the combined list, not the per-root ones - without
		// recombining, it draws the last real frame's layout at the old size.
		if ( resized )
			GlobalContext.Current.UISystem.CombineCommandLists();
	}
}
