using NativeEngine;
using Sandbox.Engine;
using Sandbox.Engine.Settings;
using Sandbox.Rendering;
using Sandbox.VR;

namespace Sandbox;

/// <summary>
/// Represents a camera and holds render hooks. This camera can be used to draw tool windows and scene panels.
/// </summary>
[Expose]
public sealed partial class SceneCamera : IDisposable, IManagedCamera
{
	// Right now these are 1:1 from the engine. Our intention should be to convert
	// these systems into systems that are configurable per camera and from addon code.
	// For example, volumetricFog should hold the render state. The addon code should hold
	// the volumes. Tonemapping should hold the render state for this camera only.
	internal ToneMapping ToneMapping = Graphics.IsAvailable ? new ToneMapping() : null;
	internal VolumetricFog VolumetricFogImpl = null;
	private readonly Lazy<Cmaa2Renderer> _cmaa2Renderer = new( () => new Cmaa2Renderer(), isThreadSafe: true );

	/// <summary>
	/// Last frame's depth chain, for the native renderer's occlusion culling.
	/// </summary>
	internal IDisposable OcclusionHistory;

	/// <summary>
	/// This is a c++ object with a ton of useful shit.
	/// Don't access it directly because it might be dirty.
	/// </summary>
	CFrustum _frustum;

	internal Matrix ProjectionMatrix => Frustum.GetProj();

	/// <summary>
	/// World to projection matrix using the reverse-Z projection (matches the rendered depth buffer:
	/// far plane = 0, near plane = 1), transposed for row-vector use with <see cref="Matrix.Transform(Vector4)"/>.
	/// <para>
	/// The engine's native matrices are column-vector (GPU <c>mul( M, v )</c>) while
	/// <see cref="Matrix.Transform(Vector4)"/> is row-vector (<c>v · M</c>), so we hand back the transpose.
	/// Transform a homogeneous world point — <c>matrix.Transform( new Vector4( pos, 1 ) )</c> — to get its
	/// reverse-Z clip-space coordinate (before the perspective divide).
	/// </para>
	/// </summary>
	internal Matrix ReverseZViewProjectionMatrix => Frustum.GetReverseZViewProjTranspose();

	/// <summary>
	/// Returns the normalized screen coverage (0-1) of a sphere at the given origin and radius.
	/// </summary>
	internal float ComputeScreenSize( Vector3 origin, float radius ) => Frustum.ComputeScreenSize( origin, radius );

	/// <summary>
	/// Returns the screen width in pixels of a sphere at the given origin and radius.
	/// </summary>
	internal float ComputeScreenSizeInPixels( Vector3 origin, float radius ) => Frustum.ComputeScreenSize( origin, radius ) * Size.x;

	public RenderAttributes Attributes { get; }

	/// <summary>
	/// The name of this camera.. for debugging purposes.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Scene objects with any of these tags won't be rendered by this camera.
	/// </summary>
	public ITagSet ExcludeTags { get; private set; } = new TokenBasedTagSet();

	/// <summary>
	/// Only scene objects with one of these tags will be rendered by this camera.
	/// </summary>
	public ITagSet RenderTags { get; private set; } = new TokenBasedTagSet();

	/// <summary>
	/// Keep hidden! CommandBuffers only!!
	/// </summary>
	internal Action<Rendering.Stage, SceneCamera> OnRenderStageHook;

	/// <summary>
	/// Called after post-processing has completed, but before normal UI overlays.
	/// </summary>
	internal Action OnAfterPostProcess;

	/// <summary>
	/// Whether the camera component's post processing reads the depth-normals G-buffer.
	/// </summary>
	internal Func<bool> WantsDepthNormalsHook;

	/// <summary>
	/// Whether something in this camera's frame reads the normals and roughness G-buffer of the depth-normals prepass
	/// (AO, SSR). Native always draws it; the managed scene renderer only when this says so.
	/// </summary>
	internal bool WantsDepthNormals => (WantsDepthNormalsHook?.Invoke() ?? false) || ScreenSpaceReflections is not null;

	/// <summary>
	/// Queries and runs the camera component's independent effects ahead of their normal stage.
	/// </summary>
	internal Func<Rendering.Stage, PostProcessQueue, bool> HasEarlyPostProcessHook;
	internal Action<Rendering.Stage, bool> ScheduleEarlyPostProcessHook;
	internal Action<Rendering.Stage, PostProcessQueue> RenderEarlyPostProcessHook;

	/// <summary>
	/// Whether something at <paramref name="stage"/> can run early on <paramref name="queue"/>
	/// (<see cref="BasePostProcess.EarlyQueue"/>).
	/// </summary>
	internal bool HasEarlyPostProcess( Rendering.Stage stage, PostProcessQueue queue ) => HasEarlyPostProcessHook?.Invoke( stage, queue ) ?? false;

	internal void ScheduleEarlyPostProcess( Rendering.Stage stage, bool enabled ) => ScheduleEarlyPostProcessHook?.Invoke( stage, enabled );

	/// <summary>
	/// Run what <see cref="HasEarlyPostProcess"/> found, inside a <see cref="Graphics"/> block on the selected queue. The stage then
	/// skips it.
	/// </summary>
	internal void RenderEarlyPostProcess( Rendering.Stage stage, PostProcessQueue queue ) => RenderEarlyPostProcessHook?.Invoke( stage, queue );

	/// <summary>
	/// Called when rendering the transparent pass
	/// </summary>
	[Obsolete]
	public Action OnRenderTransparent { get; set; }

	internal Action OnRenderOverlay { get; set; }

	internal Action OnRenderUI { get; set; }

	/// <summary>
	/// Called before post processing, for UI that wants bloom and color grading applied to it.
	/// </summary>
	internal Action OnRenderUIBeforePostProcess { get; set; }

	/// <summary>
	/// The size of the screen. Allows us to work out aspect ratio.
	/// For now will get updated automatically on render.
	/// </summary>
	public Vector2 Size
	{
		get => _size;
		set
		{
			if ( _size == value )
				return;

			_size = value;
			FrustumDirty = true;
		}
	}

	Vector2 _size = new Vector2( 512, 512 );

	/// <summary>
	/// Control volumetric fog parameters, expect this to take 1-2ms of your GPU frame time.
	/// </summary>
	public VolumetricFogParameters VolumetricFog { get; init; } = new();

	/// <summary>
	/// Control fog based on an image.
	/// </summary>
	public CubemapFogController CubemapFog { get; init; } = new();

	/// <summary>
	/// Camera-local fog resolved from Fog components. Null preserves the legacy gradient and
	/// cubemap fog. A disabled setup with full weight explicitly clears those fog layers.
	/// CameraComponent refreshes this value during rendering setup; use its Fog components
	/// for persistent settings or ViewSetup.Fog for an individual capture override.
	/// </summary>
	public FogSetup? Fog { get; set; }

	/// <summary>
	/// Choose this view's fog without changing the camera or world. Explicit capture settings
	/// take precedence, including legacy gradient overrides authored before camera-local fog.
	/// </summary>
	internal FogSetup? ResolveFog( in ViewSetup setup )
	{
		if ( !CanInheritFog( setup ) )
			return setup.Fog;

		return Fog;
	}

	internal FogSetup? ResolveFogForSkybox( in ViewSetup setup )
	{
		if ( !CanInheritFog( setup ) )
			return setup.Fog;

		return World?.GetRenderingSkybox()?.ResolveFog( this ) ?? Fog;
	}

	// ViewSetup.EnablePostprocessing controls fullscreen effects only. The camera's switch
	// disables inherited camera fog, and explicit legacy captures keep their own fog settings.
	bool CanInheritFog( in ViewSetup setup ) =>
		!setup.Fog.HasValue && !setup.GradientFog.HasValue && EnablePostProcessing;

	/// <summary>
	/// Define the rotations for each of the 6 cube faces (right, left, up, down, front, back)
	/// </summary>
	internal static readonly Rotation[] CubeRotations =
	{
		Rotation.LookAt(Vector3.Backward, Vector3.Right), // Negative Z face
		Rotation.LookAt(Vector3.Forward, Vector3.Right),  // Positive Z face
		Rotation.LookAt(Vector3.Right, Vector3.Up),       // Positive X face
		Rotation.LookAt(Vector3.Left, Vector3.Down),      // Negative X face
		Rotation.LookAt(Vector3.Down, Vector3.Right),     // Negative Y face
		Rotation.LookAt(Vector3.Up, Vector3.Right)        // Positive Y face
	};

	public SceneCamera( string name = "Unnamed" )
	{
		Attributes = new RenderAttributes();
		Name = name;
		InitCommon();
	}

	~SceneCamera()
	{
		Dispose( disposing: false );
	}

	private bool disposedValue;

	private void Dispose( bool disposing )
	{
		if ( !disposedValue )
		{
			if ( _frustum.IsValid )
			{
				_frustum.Delete();
				_frustum = default;
			}

			if ( VolumetricFogImpl != null )
			{
				// We may have a view queued to render with this
				EngineLoop.DisposeAtFrameEnd( VolumetricFogImpl );
				VolumetricFogImpl = null;
			}

			if ( _cmaa2Renderer.IsValueCreated )
				EngineLoop.DisposeAtFrameEnd( _cmaa2Renderer.Value );

			if ( OcclusionHistory != null )
			{
				// A view queued to render may still read it
				EngineLoop.DisposeAtFrameEnd( OcclusionHistory );
				OcclusionHistory = null;
			}

			if ( _reflections != null )
			{
				// A view queued to render may still trace with it
				EngineLoop.DisposeAtFrameEnd( _reflections );
				_reflections = null;
			}

			// Release native CStrongHandle copies held by texture tracking
			// in our RenderAttributes. Without this, the handles survive until
			// the RenderAttributes finalizer runs which may be after the
			// resource system has already reported leaks.
			Attributes?.Clear();

			// What a renderer keeps for this camera's views. A finalizer can't touch it: the renderer sweeps collected cameras itself
			if ( disposing && ThreadSafe.IsMainThread ) SceneRenderer.Forget( this );

			// Its auto exposure's queries on a Borealis.Gpu device, if its frames were drawn there
			if ( disposing && ThreadSafe.IsMainThread ) Tonemap?.DisposeGpuExposure();

			disposedValue = true;
		}
	}

	/// <summary>
	/// Whether <see cref="Dispose()"/> has run.
	/// </summary>
	internal bool IsDisposed => disposedValue;

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}


	internal int _cameraId;

	ScreenSpaceReflectionsPass _reflections;

	/// <summary>
	/// The screen-space reflections this camera's views render: part of every camera, or null when the graphics settings
	/// turn them off (<see cref="ScreenSpaceReflectionsPass.IsEnabled"/>).
	/// </summary>
	internal ScreenSpaceReflectionsPass ScreenSpaceReflections => ScreenSpaceReflectionsPass.IsEnabled ? _reflections : null;

	/// <summary>
	/// Make the pass while reflections are on. On the main thread: it makes its shaders here, which a render job can't.
	/// </summary>
	internal void UpdateScreenSpaceReflections()
	{
		if ( ScreenSpaceReflectionsPass.IsEnabled )
			_reflections ??= new();
	}

	internal void InitCommon()
	{
		_cameraId = (this as IManagedCamera).AllocateCameraId();

		// TODO: lets expose these as properties
		Attributes.Set( "renderOpaque", true );
		Attributes.Set( "renderTranslucent", true );
		Attributes.Set( "directLighting", true );
		Attributes.Set( "indirectLighting", true );
		Attributes.Set( "renderSun", true );
		Attributes.Set( "drawShadows", true );
		Attributes.Set( "ParticleLighting", ParticleLightingSystem.Unlit );

		FieldOfView = 70.0f;

		Tonemap = new TonemapSystem( this );
		Bloom = new BloomAccessor( this );
	}

	CFrustum Frustum
	{
		get
		{
			if ( !_frustum.IsValid )
			{
				_frustum = CFrustum.Create();
			}

			if ( FrustumDirty )
			{
				if ( Size.y <= 0.0f )
					Size = new Vector2( 1, 1 );

				var aspect = Size.x / Size.y;

				if ( Ortho )
				{
					float orthoWidth = Size.x * (OrthoHeight / Size.y);
					_frustum.InitOrthoCamera( Position, Angles, ZNear, ZFar, orthoWidth, OrthoHeight );
				}
				else
				{
					_frustum.InitCamera( Position, Angles, ZNear, ZFar, FieldOfView, aspect );
					_frustum.SetCameraWidthHeight( Size.x, Size.y );
				}

				FrustumDirty = false;
			}

			return _frustum;
		}
	}

	bool FrustumDirty { get; set; } = true;



	public override string ToString() => $"SceneCamera:{Name}";

	HashSet<SceneWorld> _worlds = new HashSet<SceneWorld>();
	SceneWorld _world;

	/// <summary>
	/// The world we're going to render.
	/// </summary>
	public SceneWorld World //PaintDay: MainWorld?
	{
		get => _world;
		set
		{
			if ( _world == value ) return;

			_worlds?.Remove( _world );

			_world = value;

			if ( _world.IsValid() )
			{
				_worlds?.Add( _world );
			}
		}
	}

	/// <summary>
	/// Your camera can render multiple worlds.
	/// </summary>
	public HashSet<SceneWorld> Worlds => _worlds;

	/// <summary>
	/// Worlds holding debug drawing and editor gizmos. Views render them unless
	/// <see cref="ViewSetup.EnableDebugOverlays"/> is false, which renders the game only.
	/// </summary>
	internal HashSet<SceneWorld> DebugWorlds { get; } = new();

	Transform _transform = new Transform();

	/// <summary>
	/// The position of the scene's camera.
	/// </summary>
	public Vector3 Position
	{
		get => _transform.Position;
		set
		{
			_transform.Position = value;
			FrustumDirty = true;
		}
	}

	/// <summary>
	/// The rotation of the scene's camera.
	/// </summary>
	public Rotation Rotation
	{
		get => _transform.Rotation;
		set
		{
			_transform.Rotation = value;
			FrustumDirty = true;
		}
	}

	/// <summary>
	/// The rotation of the scene's camera.
	/// </summary>
	public Angles Angles
	{
		get => _transform.Rotation;

		set
		{
			_transform.Rotation = value;
			FrustumDirty = true;
		}
	}

	float _fov = 90;

	/// <summary>
	/// The horizontal field of view of the Camera in degrees.
	/// </summary>
	public float FieldOfView
	{
		get => _fov;
		set
		{
			_fov = value;
			FrustumDirty = true;
		}
	}

	float _zfar = 100000;

	/// <summary>
	/// The camera's zFar distance. This is the furthest distance this camera will be able to render.
	/// This value totally depends on the game you're making. Shorter the better, sensible ranges would be
	/// between about 1000 and 30000, but if you want it to be further out you can balance that out by making
	/// znear larger.
	/// </summary>
	public float ZFar
	{
		get => _zfar;
		set
		{
			_zfar = value;
			FrustumDirty = true;
		}
	}

	float _znear = 1;

	/// <summary>
	/// The camera's zNear distance. This is the closest distance this camera will be able to render.
	/// A good value for this is about 5. Below 5 and particularly below 1 you're going to start to see
	/// a lot of artifacts like z-fighting.
	/// </summary>
	public float ZNear
	{
		get => _znear;
		set
		{
			_znear = value;
			FrustumDirty = true;
		}
	}

	bool _ortho;

	/// <summary>
	/// Whether to use orthographic projection.
	/// </summary>
	public bool Ortho
	{
		get => _ortho;
		set
		{
			_ortho = value;
			FrustumDirty = true;
		}
	}

	/// <summary>
	/// Height of the ortho when <see cref="Ortho"/> is enabled.
	/// </summary>
	public float OrthoHeight
	{
		get => Attributes.GetFloat( "cam.orthoHeight" );
		set
		{
			Attributes.Set( "cam.orthoHeight", value );
			FrustumDirty = true;
		}
	}

	/// <summary>
	/// Render this camera using a different render mode
	/// </summary>
	public SceneCameraDebugMode DebugMode
	{
		get => (SceneCameraDebugMode)Attributes.GetInt( "ToolsVisMode" );
		set => Attributes.Set( "ToolsVisMode", (int)value );
	}

	/// <summary>
	/// Render this camera using a wireframe view.
	/// </summary>
	public bool WireframeMode
	{
		get => Attributes.GetInt( "Wireframe" ) >= 1;
		set => Attributes.Set( "Wireframe", value ? 1 : 0 );
	}

	/// <summary>
	/// What kind of clearing should we do before we begin?
	/// </summary>
	public ClearFlags ClearFlags { get; set; } = ClearFlags.All;

	private Rect rect = new Rect( Vector2.Zero, Vector2.One );

	/// <summary>
	/// The rect of the screen to render to. This is normalized, between 0 and 1.
	/// </summary>
	public Rect Rect
	{
		get => rect;
		set
		{
			var newRect = value;
			newRect.Left = newRect.Left.Clamp( 0, 1 );
			newRect.Top = newRect.Top.Clamp( 0, 1 );

			rect = newRect;
		}
	}

	/// <summary>
	/// Color the scene camera clears the render target to.
	/// </summary>
	public Color BackgroundColor { get; set; }

	/// <summary>
	/// The color of the ambient light. Set it to black for no ambient light, alpha is used for lerping between IBL and constant color.
	/// </summary>
	public Color AmbientLightColor
	{
		get;
		set;
	} = Color.Transparent;

	/// <summary>
	/// Enable or disable anti-aliasing for this render.
	/// </summary>
	public bool AntiAliasing
	{
		get => Attributes.GetInt( "msaa", 4 ) == 4;
		set => Attributes.Set( "msaa", value ? 4 : 0 ); //  4 = RENDER_MULTISAMPLE_8X
	}

	/// <summary>
	/// Toggle all post processing effects for this camera. The default is on.
	/// </summary>
	public bool EnablePostProcessing { get; set; } = true;

	/// <summary>
	/// Whether CMAA2 filters this camera's image when the settings choose it (<see cref="AntiAliasMode.Cmaa2"/>). Scene Lab's
	/// native reference turns it off: the managed renderer it's compared with draws without camera stages, so without CMAA2.
	/// </summary>
	internal bool AllowCmaa2 { get; set; } = true;

	/// <summary>
	/// Should this camera render engine overlays, you'd only want this on the main camera.
	/// </summary>
	internal bool EnableEngineOverlays { get; set; } = false;

	/// <summary>
	/// Whether the UI stage layer should be created for this camera render.
	/// When false, the native pipeline will skip the UI layer entirely.
	/// </summary>
	internal bool RenderUI { get; set; } = true;

	/// <summary>
	/// When true, rendering from this camera won't request higher mip levels from
	/// the texture streaming system. Used by cubemap rendering to prevent envmap probes
	/// from pulling in full-resolution textures across the entire map.
	/// </summary>
	internal bool ExcludeFromTextureStreaming { get; set; }

	/// <summary>
	/// Render as a tools view (<c>SVF_TOOL_VIEW</c>), as native's tools render theirs: tools materials draw in the pipeline's
	/// ToolsUtil layers. Off by default, as every camera has been.
	/// </summary>
	internal bool ToolsView { get; set; }

	private static WeakReference<SceneCamera> _recordingCamera;

	/// <summary>
	/// The camera currently used for video/screenshot recording.
	/// </summary>
	internal static SceneCamera RecordingCamera
	{
		get => _recordingCamera?.TryGetTarget( out var cam ) == true ? cam : null;
		set => _recordingCamera = value is not null ? new WeakReference<SceneCamera>( value ) : null;
	}

	internal bool IsRecordingCamera => RecordingCamera == this;

	/// <summary>
	/// The HMD eye that this camera is targeting.
	/// Use <see cref="StereoTargetEye.None"/> for the user's monitor (i.e. the companion window).
	/// </summary>
	public StereoTargetEye TargetEye { get; set; } = StereoTargetEye.None;

	/// <summary>
	/// Set this to false if you don't want the stereo renderer to submit this camera's texture to the compositor.
	/// This option isn't considered if <see cref="TargetEye"/> is <see cref="StereoTargetEye.None"/>.
	/// </summary>
	public bool WantsStereoSubmit { get; set; } = false;

	/// <summary>
	/// Enable or disable direct lighting
	/// </summary>
	public bool EnableDirectLighting
	{
		get => Attributes.GetBool( "directLighting" );
		set => Attributes.Set( "directLighting", value );
	}

	/// <summary>
	/// Enable or disable indirect lighting
	/// </summary>
	public bool EnableIndirectLighting
	{
		get => Attributes.GetBool( "indirectLighting" );
		set => Attributes.Set( "indirectLighting", value );
	}

	/// <summary>
	/// This camera only draws UI - skip the scene rendering pipeline entirely and put the UI
	/// straight onto its target. For a window that is nothing but panels, which is what a
	/// launcher window is: the pipeline's passes and render targets are all for a scene that
	/// isn't there.
	/// </summary>
	internal bool UIOnly { get; set; }

	/// <summary>
	/// Should be called before a render
	/// </summary>
	internal void OnPreRender( Vector2 size )
	{
		Size = size;

		ConfigureView( default );
	}

	/// <summary>
	/// Configure the view immediately before rendering. This will set the camera position
	/// etc on the local camera renderer. This should be called immediately before adding
	/// the views etc.
	/// </summary>
	internal void ConfigureView( in ViewSetup config )
	{
		if ( _world.IsValid() && !Graphics.IsActive )
		{
			_world.UpdateObjectsForRendering( Position, ZFar );
		}
	}

	void IManagedCamera.OnRenderStage( Rendering.Stage renderStage )
	{
		if ( renderStage == Rendering.Stage.BeforePostProcess )
			ApplyAntiAliasing();

		switch ( renderStage )
		{
			case Rendering.Stage.AfterDepthPrepass:
				{
					// Light shadow-mask command lists (screen-space contact shadows) only need the
					// full-res depth buffer, and this stage already runs per view with a valid.
					ShadowMapperCallbacks.RenderScreenSpaceShadows();
					break;
				}

			case Rendering.Stage.EarlyUI:
				{
					if ( RenderUI )
						OnRenderUIBeforePostProcess?.Invoke();
					break;
				}
			case Rendering.Stage.UI:
				{
					OnRenderUI?.Invoke();
					break;
				}

			case Rendering.Stage.AfterPostProcess:
				{
					OnRenderOverlay?.Invoke();
					break;
				}

		}

		// new stuff is commandlist based, so is total thread safe
		OnRenderStageHook?.InvokeWithWarning( renderStage, this );

		if ( renderStage == Rendering.Stage.AfterPostProcess )
			OnAfterPostProcess?.Invoke();

		if ( renderStage == Rendering.Stage.AfterUI && IsRecordingCamera && Application.IsEditor && !Game.IsPlaying )
		{
			ScreenCaptureUtility.CaptureFrame();
			ScreenCaptureUtility.DrawRecordingBorder();
		}
	}

	/// <summary>
	/// CMAA2 runs on a camera's own frames, never on a capture other rendering samples (<see cref="CaptureFrames"/>): in either
	/// renderer, a cube face, a child view or a reflection draws without it.
	/// </summary>
	void ApplyAntiAliasing()
	{
		if ( AllowCmaa2 && !Graphics.IsCapture && RenderSettings.Instance.AntiAliasMode == AntiAliasMode.Cmaa2
			&& Graphics.ToolsVisMode == (int)SceneCameraDebugMode.Normal )
		{
			_cmaa2Renderer.Value.Render();
			return;
		}

		if ( _cmaa2Renderer.IsValueCreated )
			_cmaa2Renderer.Value.ReleaseResources();
	}

	/// <summary>
	/// Given a pixel rect return a frustum on the current camera.
	/// </summary>
	public Frustum GetFrustum( Rect pixelRect ) => GetFrustum( pixelRect, Size );

	/// <summary>
	/// Given a pixel rect return a frustum on the current camera. Pass in 1 to ScreenSize to use normalized screen coords.
	/// </summary>
	public Frustum GetFrustum( Rect pixelRect, Vector3 screenSize )
	{
		if ( Ortho )
		{
			var min = Vector2.Min( pixelRect.TopLeft, pixelRect.BottomRight );
			var max = Vector2.Max( pixelRect.TopLeft, pixelRect.BottomRight );

			return Sandbox.Frustum.FromOrtho( min, max, screenSize, Position, Rotation, OrthoHeight, ZNear, ZFar );
		}
		else
		{
			var c1 = pixelRect.TopLeft;
			var c2 = pixelRect.BottomRight;

			var tl = GetRay( new Vector2( MathF.Min( c1.x, c2.x ), MathF.Min( c1.y, c2.y ) ), screenSize );
			var tr = GetRay( new Vector2( MathF.Max( c1.x, c2.x ), MathF.Min( c1.y, c2.y ) ), screenSize );
			var bl = GetRay( new Vector2( MathF.Min( c1.x, c2.x ), MathF.Max( c1.y, c2.y ) ), screenSize );
			var br = GetRay( new Vector2( MathF.Max( c1.x, c2.x ), MathF.Max( c1.y, c2.y ) ), screenSize );

			return Sandbox.Frustum.FromCorners( tl, tr, br, bl, ZNear, ZFar );
		}
	}

	/// <summary>
	/// Given a cursor position get a scene aiming ray.
	/// </summary>
	public Ray GetRay( Vector3 cursorPosition ) => GetRay( cursorPosition, Size );

	/// <summary>
	/// Given a cursor position get a scene aiming ray.
	/// </summary>
	public Ray GetRay( Vector2 cursorPosition, Vector3 screenSize )
	{
		if ( screenSize.x <= 0.0f || screenSize.y <= 0.0f )
			return new Ray( Position, Rotation.Forward );

		if ( !Ortho )
		{
			var aspect = screenSize.x / screenSize.y;
			var posNormalized = new Vector2( (2.0f * cursorPosition.x / screenSize.x) - 1, (2.0f * cursorPosition.y / screenSize.y) - 1 ) * -1.0f;

			float halfWidth = MathF.Tan( FieldOfView * MathF.PI / 360.0f );
			float halfHeight = halfWidth / aspect;

			var ray = new Vector3( 1.0f, posNormalized.x / (1.0f / halfWidth), posNormalized.y / (1.0f / halfHeight) ) * Rotation;
			return new Ray( Position, ray.Normal );
		}
		else
		{
			var screenX = cursorPosition.x;
			var screenY = cursorPosition.y;
			var halfScreenHeight = OrthoHeight / 2f;
			var halfScreenWidth = halfScreenHeight * screenSize.x / screenSize.y;
			var orthoX = (2f * screenX / screenSize.x - 1f) * halfScreenWidth;
			var orthoY = (1f - 2f * screenY / screenSize.y) * halfScreenHeight;
			var forward = Rotation.Forward;

			return new Ray
			{
				Position = Position + Rotation.Right * orthoX + Rotation.Up * orthoY + forward * ZNear,
				Forward = forward
			};
		}
	}

	/// <summary>
	/// Convert from world coords to screen coords. The results for x and y will be from 0 to <see cref="Size"/>.
	/// </summary>
	public Vector2 ToScreen( Vector3 world )
	{
		Frustum.WorldToView( world, out var result );

		result.x = result.x.Remap( -1, 1, 0, Size.x );
		result.y = result.y.Remap( -1, 1, Size.y, 0 );

		return result;
	}

	/// <summary>
	/// Convert from world coords to screen coords. The results for x and y will be from 0 to <see cref="Size"/>.
	/// </summary>
	public bool ToScreen( Vector3 world, out Vector2 screen )
	{
		var v = ToScreenWithDirection( world );
		screen = new Vector2( v.x, v.y ) * Size;
		return v.z > 0.0f;
	}

	/// <summary>
	/// Convert from world coords to normal screen corrds. The results will be between 0 and 1
	/// </summary>
	public Vector2 ToScreenNormal( Vector3 world )
	{
		Frustum.WorldToView( world, out var result );

		result.x = result.x.Remap( -1, 1, 0, 1 );
		result.y = result.y.Remap( -1, 1, 1, 0 );

		return result;
	}

	/// <summary>
	/// Convert from world coords to screen coords but the Z component stores whether this vector
	/// is behind the screen (&lt;0) or in front of it (&gt;0). The X and Y components are normalized
	/// from 0 to 1.
	/// </summary>
	/// <param name="world"></param>
	/// <returns></returns>
	internal Vector3 ToScreenWithDirection( Vector3 world )
	{
		var behind = Frustum.ScreenTransform( world, out var result );

		if ( !Ortho )
		{
			result.x = (result.x + 1f) / 2f;
			result.y = ((result.y * -1f) + 1f) / 2f;
			result.z = behind ? -1f : 1f;
		}
		else
		{
			result.x = (result.x + 1f) / 2f;
			result.y = (1f - result.y) / 2f;

			if ( result.x <= 0f || result.x >= 1f || result.y <= 0f || result.y >= 1f )
				result.z = -1f;
			else
				result.z = 1f;
		}

		return result;
	}

	/// <summary>
	/// Convert from screen coords to world coords on the near frustum plane.
	/// </summary>
	public Vector3 ToWorld( Vector2 screen )
	{
		screen.x = screen.x.Remap( 0, Size.x, -1, 1 );
		screen.y = screen.y.Remap( Size.y, 0, -1, 1 );

		Frustum.ViewToWorld( screen, out var result );

		return result;
	}

	internal void GatherVolumetricFog( RenderAttributes attributes )
	{
		if ( !VolumetricFog.Enabled )
		{
			if ( VolumetricFogImpl != null )
			{
				MainThread.QueueDispose( VolumetricFogImpl );
				VolumetricFogImpl = null;
			}

			return;
		}

		if ( VolumetricFogImpl == null )
		{
			VolumetricFogImpl = new();
		}

		VolumetricFogImpl.Update( VolumetricFog );
		attributes.SetPointer( "IVolumetricFog", VolumetricFogImpl.native );
	}
	internal void GatherTonemapper( RenderAttributes attributes )
	{
		attributes.SetPointer( "ITonemapSystem", Tonemap.Enabled ? ToneMapping.GetNative() : IntPtr.Zero );
	}

	/// <summary>
	/// The view a camera renders into its swap chain: keyed to the camera, as native keys a view's history (<c>ViewUniqueId</c>).
	/// </summary>
	internal ViewSetup MainView => new() { ViewHash = HashCode.Combine( this ) };

	// Every scene render ends in one of these. They render through whichever renderer r_managed_scene picked this frame
	// (SceneRenderer.Current); tools that compare the two pass one by name.

	internal void AddToRenderList( SwapChainHandle_t swapChain, Vector2? size ) => AddToRenderList( SceneRenderer.Current, swapChain, size );

	internal void AddToRenderList( ISceneRenderer renderer, SwapChainHandle_t swapChain, Vector2? size ) => renderer.Render( this, swapChain, size, MainView );

	internal bool WantsToRenderInStereo()
	{
		return VRSystem.IsActive && TargetEye > StereoTargetEye.None;
	}

	internal void RenderToTexture( Texture texture, Vector2? size, in ViewSetup config ) => ChildRenderer.RenderToTexture( this, texture, size, config );

	/// <summary>
	/// Submit a view into colour and optional depth textures, as a child of <paramref name="parent"/> when it's valid,
	/// and copy its resolved projection and viewport for whatever samples it.
	/// </summary>
	internal bool RenderToTexture( Texture target, Texture depthTarget, ISceneView parent, in ViewSetup config, string name, out ViewData viewData )
		=> ChildRenderer.RenderToTexture( this, target, depthTarget, parent, config, name, out viewData );

	/// <summary>
	/// What renders a view into a texture: the renderer of the frame being drawn, which renders it as a child view of that frame
	/// (a command list's <c>DrawView</c>, water's refraction), or this frame's renderer outside one.
	/// </summary>
	static ISceneRenderer ChildRenderer => Graphics.CurrentManagedView?.Renderer ?? SceneRenderer.Current;

	internal void RenderToBitmap( Bitmap bitmap, in ViewSetup config = default ) => SceneRenderer.Current.RenderToBitmap( this, bitmap, config );

	/// <summary>
	/// Renders the scene from the camera position to a cube texture, capturing all 6 directions.
	/// </summary>
	internal void RenderToCubeTexture( Texture texture, in ViewSetup config = default, CommandList afterFaces = null ) => SceneRenderer.Current.RenderToCube( this, texture, config, afterFaces );

	/// <summary>
	/// Allows specifying a custom projection matrix for this camera
	/// </summary>
	public Matrix? CustomProjectionMatrix { get; set; }
}

/// <summary>
/// Flags for clearing a RT before rendering a scene using a SceneCamera
/// </summary>
[Flags, Expose]
public enum ClearFlags
{
	None = 0x00,

	[Icon( "palette" )]
	[Description( "The color buffer (the stuff you can see)" )]
	Color = 0xFF,

	[Icon( "table_rows" )]
	[Description( "The depth buffer" )]
	Depth = 0x100,

	[Icon( "interests" )]
	[Description( "The stencil" )]
	Stencil = 0x200,

	All = Color | Depth | Stencil
}

[Expose]
public enum SceneCameraDebugMode
{
	[Title( "Lit" ), Icon( "image" )]
	Normal = 0,

	[Title( "Full Bright" ), Icon( "lightbulb" )]
	FullBright = 1,

	[Title( "World-Space Normals" ), Icon( "shuffle" )]
	NormalMap = 21,

	[Title( "Albedo" ), Icon( "palette" )]
	Albedo = 10,

	[Title( "Roughness" ), Icon( "texture" )]
	Roughness = 12,

	[Title( "Diffuse" ), Icon( "cloud" )]
	Diffuse = 2,

	[Title( "Reflect" ), Icon( "flare" )]
	Reflect = 3,

	[Title( "Transmission" ), Icon( "deblur" )]
	Transmission = 4,

	[Title( "UV Maps" ), Icon( "gradient" )]
	ShowUV = 6,

	[Title( "Shader IDs" ), Icon( "sell" )]
	ShaderIDColor = 16,
	[Title( "Clustered Light Culling" ), Icon( "scatter_plot" )]
	ClusteredLightCulling = 50,

	[Title( "Quad Overdraw" ), Icon( "signal_cellular_null" )]
	QuadOverdraw = 100,
	[Title( "Overdraw" ), Icon( "layers" )]
	Overdraw = 101,
	[Title( "Ambient Occlusion" ), Icon( "radio_button_checked" )]
	AmbientOcclusion = 14,
	[Title( "Motion Vectors" ), Icon( "animation" )]
	MotionVectors = 102,
	[Title( "Reactive Mask" ), Icon( "shield" )]
	ReactiveMask = 103,
}
