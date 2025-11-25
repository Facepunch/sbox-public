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
	internal ToneMapping ToneMapping = Application.IsHeadless ? null : new ToneMapping();
	internal VolumetricFog VolumetricFogImpl = null;

	/// <summary>
	/// This is a c++ object with a ton of useful shit.
	/// Don't access it directly because it might be dirty.
	/// </summary>
	CFrustum _frustum;

	internal Matrix ProjectionMatrix => Frustum.GetProj();

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
	/// Called when rendering the post process pass
	/// </summary>
	[Obsolete]
	public Action OnRenderPostProcess { get; set; }

	/// <summary>
	/// Called when rendering the transparent pass
	/// </summary>
	[Obsolete]
	public Action OnRenderOpaque { get; set; }

	/// <summary>
	/// Called when rendering the transparent pass
	/// </summary>
	[Obsolete]
	public Action OnRenderTransparent { get; set; }

	public Action OnRenderOverlay { get; set; }

	public Action OnRenderUI { get; set; }

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

			disposedValue = true;
		}
	}

	public void Dispose()
	{
		Dispose( disposing: true );
		GC.SuppressFinalize( this );
	}


	internal int _cameraId;

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
		get => Attributes.GetInt( "Wireframe" ) > 1;
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
	/// Should this camera render engine overlays, you'd only want this on the main camera.
	/// </summary>
	internal bool EnableEngineOverlays { get; set; } = false;

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
	void ConfigureView( in ViewSetup config )
	{
		if ( _world.IsValid() && !Graphics.IsActive )
		{
			_world.UpdateObjectsForRendering( Position, ZFar );
		}
	}

	void IManagedCamera.OnRenderStage( Rendering.Stage renderStage )
	{
		// legacy stuff isn't thread safe
		if ( ThreadSafe.IsMainThread )
		{
			switch ( renderStage )
			{
				case Rendering.Stage.AfterPostProcess:
					{
						OnRenderOverlay?.Invoke();
						break;
					}

				case Rendering.Stage.AfterUI:
					{
						OnRenderUI?.Invoke();
						break;
					}
			}
		}

		// new stuff is commandlist based, so is total thread safe
		OnRenderStageHook?.InvokeWithWarning( renderStage, this );
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
	/// Projects a line in world space to screen coords, returning null if the line is
	/// fully behind the camera.
	/// </summary>
	internal Line2D? ToScreen( Line worldLine )
	{
		var forward = Rotation.Forward;

		// Clip line to be fully in front of near plane

		if ( worldLine.Clip( new Plane( Position + forward * ZNear, forward ) ) is not { } clipped )
		{
			return null;
		}

		Frustum.WorldToView( clipped.Start, out var start );
		Frustum.WorldToView( clipped.End, out var end );

		return new Line2D( start, end )
			.Remap(
				new Rect( -1f, -1f, 2f, 2f ),
				new Rect( 0f, Size.y, Size.x, -Size.y ) );
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
			result.x = (result.x + 2f) / 2f;
			result.y = ((result.y - 2f) / 2f) * -1f;

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

	internal void AddToRenderList( SwapChainHandle_t swapChain, Vector2? size )
	{
		ViewSetup setup = default;
		setup.ViewHash = HashCode.Combine( this );

		if ( WantsToRenderInStereo() )
		{
			RenderStereo( setup );
		}
		else
		{
			Render( swapChain, size, setup );
		}
	}

	private bool WantsToRenderInStereo()
	{
		return VRSystem.IsActive && TargetEye > StereoTargetEye.None;
	}

	private void Render( SwapChainHandle_t swapChain, Vector2? size, in ViewSetup config = default )
	{
		if ( swapChain.self == default )
			return;

		var renderSize = size ?? Size;

		if ( renderSize.x <= 0 ) return;
		if ( renderSize.y <= 0 ) return;

		OnPreRender( renderSize );

		var setup = new CameraRenderer( "RenderToSwapChain", _cameraId );
		setup.Configure( this, config );
		setup.Native.Render( swapChain );
	}

	internal void RenderToTexture( Texture texture, Vector2? size, in ViewSetup config )
	{
		if ( texture is null || texture.native.IsNull )
			return;

		var renderSize = size ?? texture.Size;

		if ( renderSize.x <= 0 ) return;
		if ( renderSize.y <= 0 ) return;

		ConfigureView( in config );

		var setup = new CameraRenderer( "RenderToTexture", _cameraId );
		setup.Configure( this, config );
		setup.Native.RenderToTexture( texture.native, Graphics.SceneView );
	}


	internal void RenderToBitmap( Bitmap bitmap, in ViewSetup config = default )
	{
		if ( !bitmap.IsValid() )
			return;

		var renderSize = bitmap.Size;

		if ( renderSize.x <= 1 ) return;
		if ( renderSize.y <= 1 ) return;

		OnPreRender( renderSize );

		var setup = new CameraRenderer( "RenderToBitmap", _cameraId );
		setup.Configure( this, config );

		unsafe
		{
			setup.Native.RenderToBitmap( (IntPtr)bitmap.GetPointer(), bitmap.Width, bitmap.Height, bitmap.BytesPerPixel );
		}
	}


	/// <summary>
	/// Renders the scene from the camera position to a cube texture, capturing all 6 directions.
	/// </summary>
	internal void RenderToCubeTexture( Texture texture, in ViewSetup config = default )
	{
		if ( texture is null || texture.native.IsNull )
			return;

		if ( texture.Depth != 6 ) throw new Exception( "Expected a texture with 6 depth slices for RenderToCubeTexture" );

		var renderSize = texture.Size;

		if ( renderSize.x <= 0 ) return;
		if ( renderSize.y <= 0 ) return;

		OnPreRender( renderSize );

		//
		// Adds the views to the scene system
		//
		var setup = new CameraRenderer( "RenderToCubeTexture", _cameraId );
		setup.Configure( this, config );

		for ( int i = 0; i < CubeRotations.Length; i++ )
		{
			// Override rotation for each face
			setup.Native.CameraRotation = (Rotation * CubeRotations[i]).Angles();
			setup.Native.RenderToCubeTexture( texture.native, i );
		}
	}
	
	/// <summary>
	/// Renders the scene from the camera position to a single face of a cube texture.
	/// </summary>
	internal void RenderToCubeTextureFace( Texture texture, int faceIndex, in ViewSetup config = default )
	{
		if ( texture is null || texture.native.IsNull )
			return;

		if ( texture.Depth != 6 ) throw new Exception( "Expected a texture with 6 depth slices for RenderToCubeTextureFace" );

		if ( faceIndex < 0 || faceIndex >= 6 ) throw new ArgumentOutOfRangeException( nameof(faceIndex), "Face index must be between 0 and 5" );

		var renderSize = texture.Size;

		if ( renderSize.x <= 0 ) return;
		if ( renderSize.y <= 0 ) return;

		OnPreRender( renderSize );

		//
		// Adds a single view to the scene system
		//
		var setup = new CameraRenderer( "RenderToCubeTextureFace", _cameraId );
		setup.Configure( this, config );

		// Override rotation for the specific face
		setup.Native.CameraRotation = (Rotation * CubeRotations[faceIndex]).Angles();
		setup.Native.RenderToCubeTexture( texture.native, faceIndex );
	}

	private void RenderStereo( in ViewSetup config = default )
	{
		if ( !TargetEye.Contains( StereoTargetEye.LeftEye ) && !TargetEye.Contains( StereoTargetEye.RightEye ) )
		{
			Log.Warning( $"Called {nameof( RenderStereo )} but neither eyes were present in {nameof( TargetEye )}?" );
			return;
		}

		var setup = new CameraRenderer( "RenderStereo", _cameraId );
		setup.Configure( this, config );

		var n = setup.Native;

		for ( int iEye = 0; iEye < 2; ++iEye )
		{
			bool isLeftEye = iEye == 0;
			var stereoTargetEye = isLeftEye ? StereoTargetEye.LeftEye : StereoTargetEye.RightEye;
			var eye = isLeftEye ? VREye.Left : VREye.Right;

			// Check flags to see if we should render using this eye...
			if ( !TargetEye.Contains( stereoTargetEye ) )
				continue;

			// PreRender
			OnPreRender( VRNative.EyeRenderTargetSize );

			// Save off clip planes, used for depth submit
			VRNative.ClipPlanes.ZNear = ZNear;
			VRNative.ClipPlanes.ZFar = ZFar;

			// Grab overrides for this eye
			var transform = VRNative.GetTransformForEye( n.CameraPosition, n.CameraRotation, eye );
			n.CameraPosition = transform.Position;
			n.CameraRotation = transform.Rotation;

			// Save off middle eye position for things like skybox
			n.MiddleEyePosition = Position;
			n.MiddleEyeRotation = Rotation.Angles();

			n.OverrideProjection = VRNative.GetProjectionMatrix( ZNear, ZFar, eye );
			n.HasOverrideProjection = true;

			n.FieldOfView = 0f; // Let clip bounds drive projection
			n.ClipSpaceBounds = VRNative.GetClipForEye( eye );

			// Render
			var submitThisEye = WantsStereoSubmit && eye == VREye.Right;
			n.RenderStereo( iEye, (int)VRNative.EyeRenderTargetSize.x, (int)VRNative.EyeRenderTargetSize.y, submitThisEye );
		}

		VRSystem.IsRendering = WantsStereoSubmit;

		// Done here, clean up / reset overrides
		n.HasOverrideProjection = false;
		n.ClearSceneWorlds();
	}

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
	[Title( "Tiled Rendering Lights" ), Icon( "view_module" )]
	TiledRenderingQuads = 50,

	[Title( "Quad Overdraw" ), Icon( "signal_cellular_null" )]
	QuadOverdraw = 100,
	[Title( "Overdraw" ), Icon( "layers" )]
	Overdraw = 101,
	[Title( "Ambient Occlusion" ), Icon( "radio_button_checked" )]
	AmbientOcclusion = 14,
}
