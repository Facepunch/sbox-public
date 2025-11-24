
using Sandbox.Rendering;
using System.Drawing;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Every scene should have at least one Camera.
/// </summary>
[Expose]
[Title( "Camera" )]
[Category( "Camera" )]
[Icon( "videocam" )]
[EditorHandle( "materials/gizmo/camera.png" )]
public sealed partial class CameraComponent : Component, Component.ExecuteInEditor, Component.ISceneStage
{
	SceneCamera sceneCamera;

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
	/// If specified, this camera will render to this RenderTexture instead of the screen.
	/// This can then be used in other stuff like materials.
	/// </summary>
	[Property]
	public RenderTextureAsset RenderTexture;

	/// <summary>
	/// Since we are rendering to a texture, better to generate mipmaps for it.
	/// </summary>
	private CommandList _renderTextureMipGenCommandList
	{
		get
		{
			var cmd = new CommandList( "Generate MipMaps For RenderTexture" );

			if ( RenderTexture != null && RenderTexture.Texture != null && RenderTexture.Texture.Mips > 1 )
				cmd.GenerateMipMaps( RenderTexture.Texture );

			return cmd;
		}
	}

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

		sceneCamera.OnRenderStageHook = ExecuteCommandLists;
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
	}

	protected override void DrawGizmos()
	{
		if ( sceneCamera is null )
			return;

		using var scope = Gizmo.Scope( $"{GetHashCode()}" );

		Gizmo.Transform = global::Transform.Zero;

		UpdateSceneCameraTransform( sceneCamera );

		var frustum = sceneCamera.GetFrustum( new Rect( 0f, 0f, 1920f, 1080f ), new Vector2( 1920, 1080 ) );

		Gizmo.Draw.Color = Color.White.WithAlpha( 0.4f );
		Gizmo.Draw.LineFrustum( frustum );
	}

	internal void UpdateSceneCameraTransform( SceneCamera camera )
	{
		camera.Position = WorldPosition;
		camera.Rotation = WorldRotation;
		camera.ZNear = ZNear;
		camera.ZFar = ZFar;
		camera.Rect = new Rect( Viewport.x, Viewport.y, Viewport.z, Viewport.w );
		camera.Size = ScreenRect.Size;
		camera.Ortho = Orthographic;
		camera.OrthoHeight = OrthographicHeight;

		if ( FovAxis == Axis.Vertical )
			camera.FieldOfView = Screen.CreateVerticalFieldOfView( FieldOfView );
		else
			camera.FieldOfView = FieldOfView;
	}

	internal void UpdateSceneCameraStereo( SceneCamera camera )
	{
		camera.TargetEye = TargetEye;
		camera.WantsStereoSubmit = !Scene.IsEditor;
	}

	/// <summary>
	/// Update a SceneCamera with the settings from this component
	/// </summary>
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
		camera.Worlds.Add( Scene.DebugSceneWorld );

		// Also render any active gizmos
		if ( Gizmo.Active is not null )
			camera.Worlds.Add( Gizmo.Active.World );

		CopyPostProcessing( camera );

		// A camera (such as the editor viewport) might not want to take render tags into account
		// from an existing camera source
		if ( includeTags )
		{
			camera.RenderTags.SetFrom( RenderTags );
			camera.ExcludeTags.SetFrom( RenderExcludeTags );
		}

		// Merge our global Scene attributes into the camera
		Scene.RenderAttributes.MergeTo( camera.Attributes );

		camera.DebugMode = DebugMode;
		camera.WireframeMode = WireframeMode;
		camera.EnablePostProcessing = EnablePostProcessing;
		camera.BackgroundColor = ClearFlags.Contains( ClearFlags.Color ) ? BackgroundColor : Color.Transparent;
	}

	internal void CopyPostProcessing( SceneCamera camera )
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
			camera.VolumetricFog.Enabled = Scene.GetAllComponents<VolumetricFogVolume>().Count() > 0;
			camera.VolumetricFog.DrawDistance = 4096;
			camera.VolumetricFog.FadeInStart = 64;
			camera.VolumetricFog.FadeInEnd = 256;
			camera.VolumetricFog.IndirectStrength = 1.0f;
			camera.VolumetricFog.Anisotropy = 1;
			camera.VolumetricFog.Scattering = 1.0f;
			camera.VolumetricFog.BakedIndirectTexture = Scene.GetAllComponents<VolumetricFogController>().FirstOrDefault()?.BakedFogTexture;
		}

#pragma warning disable CS0612
		GameObject.RunEvent<ISceneCameraSetup>( x => x.SetupCamera( this, camera ) );
#pragma warning restore CS0612

		//
		// Child camera executes command lists from this camera
		//
		camera.OnRenderStageHook = ExecuteCommandLists;

		//
		// Hack because I don't want this to have to be on a camera. This
		// is hidden from users, so we'll figure out how to square it later
		//
		foreach ( var cubemapFog in Scene.GetAllComponents<CubemapFog>() )
		{
			if ( cubemapFog.Tags.HasAny( RenderExcludeTags ) )
				continue;

			cubemapFog.SetupCamera( this, camera );
		}
	}

	/// <summary>
	/// Update the SceneCamera UI with the settings from this component
	/// </summary>
	private void UpdateSceneCameraUI( SceneCamera camera )
	{
		if ( Scene is null )
			return;

		camera.OnRenderUI = () => OnCameraRenderUI( camera );
		camera.OnRenderOverlay = () =>
		{
			if ( IsMainCamera ) Sandbox.DebugOverlay.Render();
		};
	}

	[Obsolete( "Use CommandList" )]
	public IDisposable AddHookAfterOpaque( string debugName, int order, Action<SceneCamera> renderEffect ) => null;

	/// <summary>
	/// Obsolete 09/06/2025
	/// </summary>
	[Obsolete( "Use CommandList" )]
	public IDisposable AddHookAfterTransparent( string debugName, int order, Action<SceneCamera> renderEffect ) => null;

	/// <summary>
	/// Obsolete 09/06/2025
	/// </summary>
	[Obsolete( "Use CommandList" )]
	public IDisposable AddHookBeforeOverlay( string debugName, int order, Action<SceneCamera> renderEffect ) => null;

	/// <summary>
	/// Obsolete 02/10/2025
	/// </summary>
	[Obsolete( "Use CommandList" )]
	public IDisposable AddHookAfterUI( string debugName, int order, Action<SceneCamera> renderEffect ) => null;

	private void OnCameraRenderUI( SceneCamera camera )
	{
		if ( Scene is null )
			return;

		foreach ( var c in Scene.GetAll<ScreenPanel>().OrderBy( x => x.ZIndex ) )
		{
			if ( !c.Active ) continue;
			var target = c.TargetCamera ?? (IsMainCamera ? this : null);
			if ( target != this ) continue;
			if ( RenderExcludeTags.HasAny( c.GameObject.Tags ) ) continue;

			c.Render();
		}
	}

	/// <summary>
	/// Obsolete 02/10/2025
	/// </summary>
	[Obsolete]
	public interface ISceneCameraSetup
	{
		void SetupCamera( CameraComponent camera, SceneCamera sceneCamera );
	}

	internal bool IsSceneEditorCamera;

	internal void InitializeRendering()
	{
		using ( Scene.Push() )
		{
			EnsureSceneCameraCreated();

			if ( IsSceneEditorCamera )
			{
				UpdateSceneCamera( sceneCamera );
				Scene.Camera?.CopyPostProcessing( sceneCamera );
			}
			else
			{
				UpdateSceneCamera( sceneCamera );
			}

			UpdateSceneCameraUI( sceneCamera );
			UpdateSceneCameraStereo( sceneCamera );
		}
	}

	/// <summary>
	/// This should only be called when creating render lists!!
	/// </summary>
	internal void AddToRenderList( SwapChainHandle_t swapChain, Vector2? size )
	{
		if ( !Active )
			return;

		// if width and height are too small, skip it
		if ( Viewport.z <= 0 ) return;
		if ( Viewport.w <= 0 ) return;

		using ( Scene.Push() )
		{
			InitializeRendering();

			if ( RenderTarget is not null && RenderTarget.native.IsValid )
			{
				SceneCamera.RenderToTexture( RenderTarget, size ?? CustomSize, default );
			}
			else
			{
				SceneCamera.AddToRenderList( swapChain, size ?? CustomSize );
			}
		}
	}

	public Vector2 PointToScreenNormal( in Vector3 worldPosition )
	{
		EnsureSceneCameraCreated();
		UpdateSceneCameraTransform( sceneCamera );

		return sceneCamera.ToScreenNormal( worldPosition );
	}

	public Vector2 PointToScreenPixels( in Vector3 worldPosition )
	{
		var sr = ScreenRect;
		var v = PointToScreenNormal( worldPosition );
		return new Vector2( v.x, v.y ) * sr.Size;
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
		Vector2[] corners = new Vector2[8];
		Vector3 min = bounds.Mins;
		Vector3 max = bounds.Maxs;

		isBehind = true;

		corners[0] = PointToScreenPixels( new Vector3( min.x, min.y, min.z ) );
		corners[1] = PointToScreenPixels( new Vector3( max.x, min.y, min.z ) );
		corners[2] = PointToScreenPixels( new Vector3( min.x, max.y, min.z ) );
		corners[3] = PointToScreenPixels( new Vector3( min.x, min.y, max.z ) );
		corners[4] = PointToScreenPixels( new Vector3( max.x, max.y, min.z ) );
		corners[5] = PointToScreenPixels( new Vector3( min.x, max.y, max.z ) );
		corners[6] = PointToScreenPixels( new Vector3( max.x, min.y, max.z ) );
		corners[7] = PointToScreenPixels( new Vector3( max.x, max.y, max.z ) );

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
		EnsureSceneCameraCreated();
		UpdateSceneCameraTransform( sceneCamera );

		var sr = ScreenRect;
		var v = sceneCamera.ToScreenWithDirection( worldPosition );
		isBehind = v.z <= 0.0f;
		return new Vector2( v.x, v.y ) * sr.Size;
	}

	public Vector2 PointToScreenNormal( Vector3 worldPosition, out bool isBehind )
	{
		EnsureSceneCameraCreated();
		UpdateSceneCameraTransform( sceneCamera );

		var v = sceneCamera.ToScreenWithDirection( worldPosition );
		isBehind = v.z <= 0.0f;
		return new Vector2( v.x, v.y );
	}

	public Ray ScreenPixelToRay( Vector2 pixelPosition )
	{
		EnsureSceneCameraCreated();
		UpdateSceneCameraTransform( sceneCamera );

		return sceneCamera.GetRay( pixelPosition, ScreenRect.Size );
	}

	public Ray ScreenNormalToRay( Vector3 normalPosition )
	{
		var pixelPosition = new Vector3(
			normalPosition.x * ScreenRect.Size.x,
			normalPosition.y * ScreenRect.Size.y,
			normalPosition.z );

		return ScreenPixelToRay( pixelPosition );
	}

	/// <summary>
	/// Convert from screen coords to world coords on the near frustum plane.
	/// </summary>
	public Vector3 ScreenToWorld( Vector2 screen )
	{
		EnsureSceneCameraCreated();
		UpdateSceneCameraTransform( sceneCamera );

		return sceneCamera.ToWorld( screen );
	}

	protected override void OnEnabled()
	{
		Scene?.UpdateMainCamera();

		AddCommandList( _hudCommandList, Rendering.Stage.AfterPostProcess, 5000 );
		AddCommandList( _overlayCommandList, Rendering.Stage.AfterUI, 5000 );

		if ( RenderTexture is not null )
			AddCommandList( _renderTextureMipGenCommandList, Rendering.Stage.AfterPostProcess, 10000 );
	}

	protected override void OnDisabled()
	{
		Scene?.UpdateMainCamera();

		RemoveCommandList( _hudCommandList );
		RemoveCommandList( _overlayCommandList );
		RemoveCommandList( _renderTextureMipGenCommandList );
	}

	/// <summary>
	/// Returns the view frustum of the current screen rect.
	/// </summary>
	public Frustum GetFrustum()
	{
		UpdateSceneCameraTransform( sceneCamera );
		return sceneCamera.GetFrustum( ScreenRect );
	}

	/// <summary>
	/// Given a pixel rect return a frustum on the current camera.
	/// </summary>
	public Frustum GetFrustum( Rect screenRect )
	{
		// Note: Updating every time might cause issues if too much pressure is put on this function. Keep an eye on it.
		UpdateSceneCameraTransform( sceneCamera );
		return sceneCamera.GetFrustum( screenRect );
	}

	/// <summary>
	/// Given a pixel rect return a frustum on the current camera. Pass in 1 to ScreenSize to use normalized screen coords.
	/// </summary>
	public Frustum GetFrustum( Rect screenRect, Vector3 screenSize )
	{
		// Same as above
		UpdateSceneCameraTransform( sceneCamera );
		return sceneCamera.GetFrustum( screenRect, screenSize );
	}

	/// <summary>
	/// Render scene to a texture from this camera's point of view
	/// </summary>
	public bool RenderToTexture( Texture target, in ViewSetup config = default )
	{
		if ( target is null || target.native.IsNull )
			return false;

		if ( !Graphics.IsActive )
		{
			Scene.PreCameraRender();
			InitializeRendering();
		}

		using var setup = new CameraRenderer( $"{GameObject.Name}.RenderToTexture", sceneCamera._cameraId );

		lock ( this )
		{
			setup.Configure( sceneCamera, config );

			//
			// Adds the views to the scene system
			//
			setup.Native.RenderToTexture( target.native, Graphics.SceneView );
			setup.Native.ClearSceneWorlds();
		}


		return true;
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
	/// By default the camera size is screen size or render target size.
	/// </summary>
	[JsonIgnore, Hide]
	public Vector2? CustomSize { get; set; }

	/// <summary>
	/// Get frustum projection matrix.
	/// </summary>
	public Matrix ProjectionMatrix => sceneCamera.ProjectionMatrix;

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
	public void RenderToBitmap( Bitmap targetBitmap )
	{
		if ( targetBitmap == null || targetBitmap.Width <= 1 || targetBitmap.Height <= 1 )
			return;

		if ( !this.IsValid() )
			return;

		using ( Scene.Push() )
		{
			Scene.PreCameraRender();
			InitializeRendering();
			SceneCamera.OnPreRender( targetBitmap.Size );

			SceneCamera.RenderToBitmap( targetBitmap );
		}
	}
}
