using Sandbox.Rendering;

namespace Editor.ShaderGraph;

public class Throbber : SceneCustomObject
{
	private readonly Texture _texture;

	private bool _enabled;
	private RealTimeSince _timeSinceDisabled;
	public bool Enabled
	{
		set
		{
			_enabled = value;

			if ( !value )
			{
				_timeSinceDisabled = 0;
			}
		}
	}

	private Preview _preview;

	public Throbber( SceneWorld sceneWorld, Preview preview ) : base( sceneWorld )
	{
		_preview = preview;
		_texture = Texture.Load( "tools/images/common/busy.png", true );
		Bounds = BBox.FromPositionAndSize( Vector3.Zero, float.MaxValue );
	}

	public override void RenderSceneObject()
	{
		base.RenderSceneObject();

		if ( !_enabled && _timeSinceDisabled > 0.5f )
			return;

		var speed = 300;
		var delta = _enabled ? 0.0f : _timeSinceDisabled * 2.0f;
		var angle = RealTime.Now % (MathF.PI * (2.0f * speed));
		var dpiScale = _preview.DpiScale;

		var pos = new Vector2( _preview.Width - 39, 41 ) * dpiScale;
		Matrix mat = Matrix.CreateRotation( Rotation.From( 0, angle * speed, 0 ) );
		mat *= Matrix.CreateTranslation( pos );
		Graphics.Attributes.Set( "LayerMat", mat );

		Graphics.Attributes.Set( "Texture", _texture );
		Graphics.Attributes.SetComboEnum( "D_BLENDMODE", Sandbox.BlendMode.Normal );
		Graphics.DrawQuad( new Rect( -50, 100 ) * dpiScale, Material.UI.Basic, Color.Black.WithAlpha( 0.5f.LerpTo( 0.0f, delta ) ) );
		Graphics.Attributes.SetComboEnum( "D_BLENDMODE", Sandbox.BlendMode.Lighten );

		pos = new Vector2( _preview.Width - 40, 40 ) * dpiScale;
		mat = Matrix.CreateRotation( Rotation.From( 0, angle * speed, 0 ) );
		mat *= Matrix.CreateTranslation( pos );
		Graphics.Attributes.Set( "LayerMat", mat );
		Graphics.DrawQuad( new Rect( -50, 100 ) * dpiScale, Material.UI.Basic, _enabled ? Color.White : Color.White.WithAlpha( 1.0f.LerpTo( 0.0f, delta ) ) );
	}
}

public class PreviewPanel : Widget
{
	private readonly Preview _preview;
	public Preview Preview => _preview;
	private readonly ComboBox _animationCombo;

	public Model Model
	{
		get => _preview.Model;
		set
		{
			if ( Model == value )
				return;

			_preview.Model = value ?? _preview.SphereModel;

			UpdateAnimationCombo();

			OnModelChanged?.Invoke( value );
		}
	}

	public Material Material
	{
		set => _preview.Material = value;
	}

	public Color Tint
	{
		set => _preview.Tint = value;
	}

	public bool PostProcessing
	{
		get => _preview.EnablePostProcessing;
		set
		{
			if ( _preview.EnablePostProcessing == value )
				return;

			_preview.EnablePostProcessing = value;
			_preview.UpdateMaterial();
			_preview.UpdatePostProcessing();
		}
	}

	private void UpdateAnimationCombo()
	{
		_animationCombo.Clear();

		var model = Model;
		var animationCount = Model.AnimationCount;

		if ( animationCount > 0 )
		{
			_animationCombo.Visible = true;
			_animationCombo.AddItem( "None", "animgraph_editor/single_frame_icon.png" );

			for ( int i = 0; i < model.AnimationCount; ++i )
			{
				_animationCombo.AddItem( model.GetAnimationName( i ), "animgraph_editor/single_frame_icon.png" );
			}
		}
		else
		{
			_animationCombo.Visible = false;
		}
	}

	public bool IsCompiling
	{
		set
		{
			_preview.IsCompiling = value;
		}
	}

	public Action<Model> OnModelChanged { get; set; }

	public void SetAttribute( string id, in Texture value )
	{
		_preview.SetAttribute( id, value );
	}

	public void SetAttribute( string id, in Color value )
	{
		_preview.SetAttribute( id, value );
	}

	public void SetAttribute( string id, in Vector3 value )
	{
		_preview.SetAttribute( id, value );
	}

	public void SetAttribute( string id, in Vector2 value )
	{
		_preview.SetAttribute( id, value );
	}

	public void SetAttribute( string id, in float value )
	{
		_preview.SetAttribute( id, value );
	}

	public void SetAttribute( string id, in bool value )
	{
		_preview.SetAttribute( id, value );
	}

	public void SetStage( int value )
	{
		_preview.SetStage( value );
	}

	public void ClearAttributes()
	{
		_preview.ClearAttributes();
	}

	public PreviewPanel( Widget parent, string model ) : base( parent )
	{
		Name = "Preview";
		WindowTitle = "Preview";
		SetWindowIcon( "photo" );

		_preview = new Preview( this, model );

		Layout = Layout.Column();

		var toolBar = new ToolBar( this, "PreviewToolBar" );
		toolBar.SetIconSize( 16 );
		toolBar.AddOption( null, "view_in_ar", () => Model = Model.Load( "models/dev/box.vmdl" ) ).ToolTip = "Box";
		toolBar.AddOption( null, "circle", () => Model = null ).ToolTip = "Sphere";
		toolBar.AddOption( null, "square", () => Model = Model.Load( "models/dev/plane.vmdl" ) ).ToolTip = "Plane";
		toolBar.AddOption( null, "accessibility", () =>
		{
			var picker = AssetPicker.Create( this, AssetType.Model );
			picker.OnAssetHighlighted = x => Model = x.First().LoadResource<Model>();
			picker.OnAssetPicked = x => Model = x.First().LoadResource<Model>();
			picker.Window.Show();
		} ).ToolTip = "Model";

		toolBar.AddSeparator();

		var combo = new Widget( toolBar );
		combo.Layout = Layout.Row();
		_animationCombo = new ComboBox( combo );
		combo.Layout.Add( _animationCombo, 1 );
		toolBar.AddWidget( combo );

		UpdateAnimationCombo();

		_animationCombo.ItemChanged += () =>
		{
			if ( _animationCombo.CurrentIndex == 0 )
			{
				_preview.UseAnimGraph = true;
			}
			else
			{
				_preview.UseAnimGraph = false;
				_preview.CurrentSequence = _animationCombo.CurrentText;
			}
		};

		var stretcher = new Widget( toolBar );
		stretcher.Layout = Layout.Row();
		stretcher.Layout.AddStretchCell( 1 );
		toolBar.AddWidget( stretcher );

		var option = toolBar.AddOption( null, "preview" );
		option.Checkable = true;
		option.Toggled = ( e ) => _preview.EnableNodePreview = e;
		option.ToolTip = "Toggle Node Preview";
		option.StatusTip = "Toggle Node Preview";

		toolBar.AddSeparator();

		option = toolBar.AddOption( null, "lightbulb", OpenLightSettings );
		option.ToolTip = "Light Settings";
		option.StatusTip = "Light Settings";

		toolBar.AddSeparator();

		option = toolBar.AddOption( null, "settings", OpenSettings );
		option.ToolTip = "Settings";
		option.StatusTip = "Settings";

		Layout.Add( toolBar );
		Layout.Add( _preview );
	}

	public void OpenLightSettings()
	{
		var popup = new PopupWidget( this );
		popup.IsPopup = true;
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 16;

		var cs = new ControlSheet();
		cs.AddProperty( _preview, x => x.SunAngle );
		cs.AddProperty( _preview, x => x.SunColor );
		cs.AddProperty( _preview, x => x.EnablePointLights );
		cs.AddProperty( _preview, x => x.EnableShadows );

		popup.Layout.Add( cs );
		popup.MaximumWidth = 300;
		popup.OpenAtCursor();
	}

	public void OpenSettings()
	{
		var popup = new PopupWidget( this );
		popup.IsPopup = true;
		popup.Layout = Layout.Column();
		popup.Layout.Margin = 16;

		var cs = new ControlSheet();
		cs.AddProperty( _preview, x => x.RenderBackfaces );
		cs.AddProperty( _preview, x => x.ShowGround );
		cs.AddProperty( _preview, x => x.ShowSkybox );
		cs.AddProperty( _preview, x => x.BackgroundColor );
		cs.AddProperty( _preview, x => x.Tint );

		popup.Layout.Add( cs );
		popup.MaximumWidth = 300;
		popup.OpenAtCursor();
	}

	public void LoadSettings( PreviewSettings settings )
	{
		_preview.RenderBackfaces = settings.RenderBackfaces;
		_preview.EnableShadows = settings.EnableShadows;
		_preview.ShowGround = settings.ShowGround;
		_preview.ShowSkybox = settings.ShowSkybox;
		_preview.BackgroundColor = settings.BackgroundColor;
		_preview.Tint = settings.Tint;
	}

	public void SaveSettings( PreviewSettings settings )
	{
		settings.RenderBackfaces = _preview.RenderBackfaces;
		settings.EnableShadows = _preview.EnableShadows;
		settings.ShowGround = _preview.ShowGround;
		settings.ShowSkybox = _preview.ShowSkybox;
		settings.BackgroundColor = _preview.BackgroundColor;
		settings.Tint = _preview.Tint;
	}
}

public class Preview : SceneRenderingWidget
{
	private SceneWorld _world => Scene.SceneWorld;

	private Vector2 _lastCursorPos;
	private Vector2 _cursorDelta;
	private Vector2 _angles;
	private Vector3 _origin;
	private float _distance;
	private float _modelRotation;
	private bool _orbitControl;
	private bool _orbitLights;
	private bool _zoomControl;
	private bool _panControl;

	private SceneModel _sceneObject;
	private Throbber _thobber;

	private Dictionary<string, Texture> _textureAttributes = new();
	private Dictionary<string, Color> _float4Attributes = new();
	private Dictionary<string, Vector3> _float3Attributes = new();
	private Dictionary<string, Vector2> _float2Attributes = new();
	private Dictionary<string, float> _floatAttributes = new();
	private Dictionary<string, bool> _boolAttributes = new();
	private int _stageId;

	private bool _enablePostProcessing;
	public bool EnablePostProcessing
	{
		get { return _enablePostProcessing; }
		set { _enablePostProcessing = value; }
	}

	private bool _enableNodePreview;
	public bool EnableNodePreview
	{
		get => _enableNodePreview;
		set
		{
			_enableNodePreview = value;

			if ( _sceneObject.IsValid() )
			{
				_sceneObject.Attributes.Set( "g_iStageId", _enableNodePreview ? _stageId : 0 );
			}
		}
	}

	private bool _enableShadows = true;
	public bool EnableShadows
	{
		get => _enableShadows;
		set
		{
			_enableShadows = value;

			if ( _sceneObject.IsValid() )
			{
				_sceneObject.Flags.CastShadows = _enableShadows;
			}
		}
	}

	public bool ShowSkybox
	{
		get => _sky.IsValid() && _sky.Enabled;
		set
		{
			if ( !_sky.IsValid() )
				return;

			_sky.Enabled = value;
		}
	}

	public bool ShowGround
	{
		get => _ground.RenderingEnabled;
		set => _ground.RenderingEnabled = value;
	}

	private bool _renderBackfaces;
	public bool RenderBackfaces
	{
		get => _renderBackfaces;
		set
		{
			_renderBackfaces = value;

			if ( _sceneObject.IsValid() )
			{
				_sceneObject.Attributes.SetCombo( "D_RENDER_BACKFACES", _renderBackfaces );
			}
		}
	}

	public Color BackgroundColor
	{
		get => Scene.Camera.IsValid() ? Scene.Camera.BackgroundColor : default;
		set
		{
			if ( !Scene.Camera.IsValid() )
				return;

			Scene.Camera.BackgroundColor = value;
		}
	}

	private DirectionalLight _sun;
	private HashSet<PointLight> _pointLights = new();
	private Angles _sunAngles = Rotation.FromPitch( 50 );
	public Angles SunAngle
	{
		get => _sunAngles;
		set
		{
			_sunAngles = value;
			if ( _sun.IsValid() )
			{
				_sun.WorldRotation = _sunAngles;
			}
		}
	}
	private Color _sunColor = new Color( 0.91f, 0.98f, 1.00f );
	public Color SunColor
	{
		get => _sunColor;
		set
		{
			_sunColor = value;
			if ( _sun.IsValid() )
			{
				_sun.LightColor = _sunColor;
			}
		}
	}
	private bool _enablePointLights = true;
	public bool EnablePointLights
	{
		get => _enablePointLights;
		set
		{
			_enablePointLights = value;
			foreach ( var light in _pointLights )
			{
				light.Enabled = _enablePointLights;
			}
		}
	}

	public void UpdateMaterial()
	{
		var modelMaterial = _material;
		if ( EnablePostProcessing )
		{
			modelMaterial = Material.Load( "materials/dev/reflectivity_50.vmat" );
		}

		if ( _sceneObject is SceneModel sceneModel )
		{
			sceneModel.SetMaterialOverride( modelMaterial );
		}
	}

	private CommandList _postProcessCmdList;

	internal void UpdatePostProcessing()
	{
		if ( Scene.Camera is null )
		{
			return;
		}

		if ( !EnablePostProcessing )
		{
			if ( _postProcessCmdList is not null )
			{
				Scene?.Camera?.RemoveCommandList( _postProcessCmdList );
				_postProcessCmdList = null;
			}
			return;
		}

		if ( _postProcessCmdList is null )
		{
			_postProcessCmdList = new CommandList( "Preview PostProcess" );
			Scene.Camera.AddCommandList( _postProcessCmdList, Stage.AfterPostProcess, 1000 );
		}

		_postProcessCmdList.Reset();
		_postProcessCmdList.Attributes.GrabFrameTexture( "ColorBuffer" );
		_postProcessCmdList.Blit( _material, _sceneObject.Attributes );
	}

	private Material _material;
	public Material Material
	{
		get => _material;
		set
		{
			_material = value;
			UpdateMaterial();
			UpdatePostProcessing();
		}
	}

	private Color _tint = Color.White;
	public Color Tint
	{
		get => _tint;
		set
		{
			_tint = value;
			if ( _sceneObject.IsValid() )
				_sceneObject.ColorTint = _tint;
		}
	}

	private Model _model;
	public Model Model
	{
		get => _model;
		set
		{
			_model = value;

			if ( _sceneObject.IsValid() )
			{
				_sceneObject.RenderingEnabled = false;
				_sceneObject.Delete();
			}

			_sceneObject = new SceneModel( _world, value, Transform.Zero )
			{
				ColorTint = Tint,
				Batchable = false
			};

			_sceneObject.Update( 1 );

			UpdateMaterial();

			foreach ( var texture in _textureAttributes )
			{
				_sceneObject.Attributes.Set( texture.Key, texture.Value );
			}

			foreach ( var v in _float4Attributes )
			{
				_sceneObject.Attributes.Set( v.Key, v.Value );
			}

			foreach ( var v in _float3Attributes )
			{
				_sceneObject.Attributes.Set( v.Key, v.Value );
			}

			foreach ( var v in _float2Attributes )
			{
				_sceneObject.Attributes.Set( v.Key, v.Value );
			}

			foreach ( var v in _floatAttributes )
			{
				_sceneObject.Attributes.Set( v.Key, v.Value );
			}

			foreach ( var v in _boolAttributes )
			{
				_sceneObject.Attributes.Set( v.Key, v.Value );
			}

			if ( _enableNodePreview )
			{
				_sceneObject.Attributes.Set( "g_iStageId", _stageId );
			}

			_sceneObject.Attributes.SetCombo( "D_RENDER_BACKFACES", _renderBackfaces );
			UpdatePostProcessing();
		}
	}

	public string CurrentSequence
	{
		set => _sceneObject.CurrentSequence.Name = value;
	}

	public bool UseAnimGraph
	{
		set => _sceneObject.UseAnimGraph = value;
	}

	public void SetAttribute( string id, Texture value )
	{
		_textureAttributes.Add( id, value );
		_sceneObject.Attributes.Set( id, value );
	}

	public void SetAttribute( string id, Color value )
	{
		_float4Attributes.Add( id, value );
		_sceneObject.Attributes.Set( id, value );
	}

	public void SetAttribute( string id, Vector3 value )
	{
		_float3Attributes.Add( id, value );
		_sceneObject.Attributes.Set( id, value );
	}

	public void SetAttribute( string id, Vector2 value )
	{
		_float2Attributes.Add( id, value );
		_sceneObject.Attributes.Set( id, value );
	}

	public void SetAttribute( string id, float value )
	{
		_floatAttributes.Add( id, value );
		_sceneObject.Attributes.Set( id, value );
	}

	public void SetAttribute( string id, in bool value )
	{
		_boolAttributes.Add( id, value );
		_sceneObject.Attributes.Set( id, value );
	}

	public void SetStage( int value )
	{
		_stageId = value;

		if ( _sceneObject.IsValid() )
		{
			_sceneObject.Attributes.Set( "g_iStageId", _enableNodePreview ? _stageId : 0 );
		}
	}

	public void ClearAttributes()
	{
		_textureAttributes.Clear();
		_float4Attributes.Clear();
		_float3Attributes.Clear();
		_float2Attributes.Clear();
		_floatAttributes.Clear();
		_boolAttributes.Clear();

		if ( _sceneObject.IsValid() )
		{
			_sceneObject.Attributes.Clear();
			_sceneObject.Attributes.SetCombo( "D_RENDER_BACKFACES", _renderBackfaces );
		}
	}

	public bool IsCompiling
	{
		set
		{
			_thobber.Enabled = value;
		}
	}

	public Model SphereModel { get; set; }
	public Model GroundModel { get; set; }

	private readonly SceneObject _ground;
	private readonly SkyBox2D _sky;

	public Preview( Widget parent, string model ) : base( parent )
	{
		MouseTracking = true;
		FocusMode = FocusMode.Click;

		Scene = Scene.CreateEditorScene();

		using ( Scene.Push() )
		{
			{
				var camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>();
				camera.BackgroundColor = Color.White;
			}
			{
				_sun = new GameObject( true, "sun" ).GetOrAddComponent<DirectionalLight>();
				_sun.WorldRotation = SunAngle;
				_sun.LightColor = SunColor;
			}
			{
				var light = new GameObject( true, "light" ).GetOrAddComponent<PointLight>( false );
				light.WorldPosition = 100;
				light.Radius = 500;
				light.LightColor = Color.Orange * 3;
				light.Enabled = true;
				_pointLights.Add( light );
			}
			{
				var light = new GameObject( true, "light" ).GetOrAddComponent<PointLight>( false );
				light.WorldPosition = -100;
				light.Radius = 500;
				light.LightColor = Color.Cyan * 3;
				light.Enabled = true;
				_pointLights.Add( light );
			}
			{
				var cubemap = new GameObject( true, "cubemap" ).GetOrAddComponent<EnvmapProbe>();
				cubemap.Texture = Texture.Load( "textures/cubemaps/default2.vtex" );
			}
			{
				_sky = new GameObject( true, "sky" ).GetOrAddComponent<SkyBox2D>();
			}
		}

		_distance = 150.0f;
		_angles = new Vector2( 45 * 3, 30 );

		Scene.Camera.WorldRotation = new Angles( _angles.y, -_angles.x, 0 );
		Scene.Camera.WorldPosition = Scene.Camera.WorldRotation.Backward * _distance;
		Scene.Camera.FieldOfView = 45;

		SphereModel = Model.Builder
			.AddMesh( CreateTessellatedSphere( 64, 64, 4.0f, 4.0f, 32.0f ) )
			.Create();

		GroundModel = Model.Builder
			.AddMesh( CreatePlane() )
			.Create();

		_thobber = new Throbber( _world, this );

		_material = Material.Load( "materials/core/shader_editor.vmat" );
		Model = string.IsNullOrWhiteSpace( model ) ? SphereModel : Model.Load( model );

		_ground = new SceneObject( _world, GroundModel );
		_ground.RenderingEnabled = false;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		if ( _postProcessCmdList is not null )
		{
			Camera?.RemoveCommandList( _postProcessCmdList );
			_postProcessCmdList = null;
		}

		Scene?.Destroy();
		Scene = null;
	}

	// Application.CursorPosition is fucked for different DPI
	private static Vector2 CursorPosition => Application.UnscaledCursorPosition;

	protected override void PreFrame()
	{
		Scene.EditorTick( RealTime.Now, RealTime.Delta );

		var cursorPos = CursorPosition;
		_cursorDelta = cursorPos - _lastCursorPos;

		if ( _orbitControl )
		{
			if ( _cursorDelta.Length > 0.0f )
			{
				_angles.x += _cursorDelta.x * 0.2f;

				if ( !_orbitLights )
					_angles.y += _cursorDelta.y * 0.2f;

				_angles.y = _angles.y.Clamp( -90, 90 );
				_angles.x = _angles.x.NormalizeDegrees();

				if ( _orbitLights )
				{
					_modelRotation -= _cursorDelta.x * 0.2f;
				}
			}

			Application.UnscaledCursorPosition = _lastCursorPos;
			Cursor = CursorShape.Blank;
		}
		else if ( _zoomControl )
		{
			if ( Math.Abs( _cursorDelta.y ) > 0.0f )
			{
				Zoom( _cursorDelta.y );
			}

			Application.UnscaledCursorPosition = _lastCursorPos;
			Cursor = CursorShape.Blank;
		}
		else if ( _panControl )
		{
			if ( _cursorDelta.Length > 0.0f )
			{
				var right = Scene.Camera.WorldRotation.Right * _cursorDelta.x * 0.2f;
				var down = Scene.Camera.WorldRotation.Down * _cursorDelta.y * 0.2f;
				var invRot = Rotation.FromYaw( _modelRotation ).Inverse;
				_origin += right * invRot;
				_origin += down * invRot;
			}

			Application.UnscaledCursorPosition = _lastCursorPos;
			Cursor = CursorShape.Blank;
		}
		else
		{
			_lastCursorPos = cursorPos;
			Cursor = CursorShape.None;
		}

		_sceneObject.ColorTint = Tint;
		_sceneObject.Rotation = Rotation.FromYaw( _modelRotation );
		_sceneObject.Update( RealTime.Delta );
		_sceneObject.Attributes.Set( "g_flPreviewTime", RealTime.Now );

		_ground.Position = Vector3.Up * (_model.RenderBounds.Mins.z - 0.1f);

		Scene.Camera.WorldRotation = new Angles( _angles.y, -_angles.x, 0 );
		Scene.Camera.WorldPosition = (_origin + _model.RenderBounds.Center) * _sceneObject.Rotation + Scene.Camera.WorldRotation.Backward * _distance;
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );

		if ( e.Key == KeyCode.Control )
		{
			_orbitLights = true;
		}
	}

	protected override void OnKeyRelease( KeyEvent e )
	{
		base.OnKeyRelease( e );

		if ( e.Key == KeyCode.Control )
		{
			_orbitLights = false;
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		_orbitLights = e.HasCtrl;

		if ( e.LeftMouseButton )
		{
			_orbitControl = true;
			_lastCursorPos = CursorPosition;
			_modelRotation = _sceneObject.Rotation.Yaw();
		}
		else if ( e.RightMouseButton )
		{
			_zoomControl = true;
			_lastCursorPos = CursorPosition;
		}
		else if ( e.MiddleMouseButton )
		{
			_panControl = true;
			_lastCursorPos = CursorPosition;
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		if ( e.LeftMouseButton )
		{
			_orbitControl = false;
			_orbitLights = false;
		}
		else if ( e.RightMouseButton )
		{
			_zoomControl = false;
		}
		else if ( e.MiddleMouseButton )
		{
			_panControl = false;
		}
	}

	protected override void OnWheel( WheelEvent e )
	{
		base.OnWheel( e );

		Zoom( e.Delta * -0.1f );
	}

	private void Zoom( float delta )
	{
		_distance += delta;
		_distance = _distance.Clamp( 0, 2000 );
	}

	static Mesh CreatePlane()
	{
		var material = Material.Load( "materials/dev/gray_grid_8.vmat" );
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( 4, new[]
		{
			new Vertex( new Vector3( -200, -200, 0 ), Vector3.Up, Vector3.Forward, new Vector4( 0, 0, 0, 0 ) ),
			new Vertex( new Vector3( 200, -200, 0 ), Vector3.Up, Vector3.Forward, new Vector4( 2, 0, 0, 0 ) ),
			new Vertex( new Vector3( 200, 200, 0 ), Vector3.Up, Vector3.Forward, new Vector4( 2, 2, 0, 0 ) ),
			new Vertex( new Vector3( -200, 200, 0 ), Vector3.Up, Vector3.Forward, new Vector4( 0, 2, 0, 0 ) ),
		} );
		mesh.CreateIndexBuffer( 6, new[] { 0, 1, 2, 2, 3, 0 } );
		mesh.Bounds = BBox.FromPositionAndSize( 0, 100 );

		return mesh;
	}

	// We could do with a nice geometry API but this is tools code so fuck it!
	static Mesh CreateTessellatedSphere( int uFacets, int vFacets, float maxU, float maxV, float radius )
	{
		float dU = 1.0f / uFacets;
		float dV = 1.0f / vFacets;

		var material = Material.Load( "materials/core/shader_editor.vmat" );
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer<Vertex>( (uFacets + 1) * (vFacets + 1) );
		mesh.CreateIndexBuffer( 2 * 3 * uFacets * vFacets );
		mesh.Bounds = BBox.FromPositionAndSize( 0, radius * 2 );

		mesh.LockVertexBuffer<Vertex>( ( vertices ) =>
		{
			float v = 0.5f;
			int i = 0;

			for ( int nV = 0; nV < (vFacets + 1); nV++ )
			{
				float u = 0.0f;

				for ( int nU = 0; nU < (uFacets + 1); nU++ )
				{
					float sinTheta = MathF.Sin( u * MathF.PI );
					float cosTheta = MathF.Cos( u * MathF.PI );
					float sinPhi = MathF.Sin( v * 2.0f * MathF.PI );
					float cosPhi = MathF.Cos( v * 2.0f * MathF.PI );

					var vertex = new Vertex();
					vertex.Position = radius * new Vector3( sinTheta * cosPhi, sinTheta * sinPhi, cosTheta );
					vertex.Normal = new Vector3( sinTheta * cosPhi, sinTheta * sinPhi, cosTheta ).Normal;
					vertex.Tangent = new Vector4( new Vector3( -sinPhi, cosPhi, 0.0f ).Normal, -1.0f );
					vertex.TexCoord0 = new Vector2( (v - 0.5f) * maxV, u * maxU );
					vertex.TexCoord1 = vertex.TexCoord0 * -1.0f;
					vertex.Color = Color.Lerp( Color.Red, Color.Green, (vertex.Position.z + radius) / (2 * radius) );

					vertices[i++] = vertex;

					u += dU;
				}

				v += dV;
			}
		} );

		mesh.LockIndexBuffer( ( indices ) =>
		{
			int i = 0;

			for ( int v = 0; v < vFacets; v++ )
			{
				for ( int u = 0; u < uFacets; u++ )
				{
					indices[i++] = v * (uFacets + 1) + u;
					indices[i++] = v * (uFacets + 1) + (u + 1);
					indices[i++] = (v + 1) * (uFacets + 1) + u;
					indices[i++] = v * (uFacets + 1) + (u + 1);
					indices[i++] = (v + 1) * (uFacets + 1) + (u + 1);
					indices[i++] = (v + 1) * (uFacets + 1) + u;
				}
			}
		} );

		return mesh;
	}
}
