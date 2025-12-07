namespace Editor;

public partial class SceneViewportWidget : Widget
{
	public static SceneViewportWidget LastSelected { get; private set; }
	public static Vector2 MousePosition { get; private set; }

	public int Id { get; private set; }
	public SceneViewWidget SceneView { get; private set; }
	public Gizmo.Instance GizmoInstance { get; private set; }
	public SceneOverlayWidget Overlay { get; private set; }

	// todo: this is pinched from hammer, replace with proper scene bounds calcs?
	private static readonly float MASSIVEZFAR = 1.7321f * (16384 + 16384);
	private float CurrentFOV { get; set; }
	private float CurrentOrthoHeight { get; set; }
	private float TransitionBlend { get; set; } = 0.0f;
	private float TargetFOV { get; set; } = 80;
	private static float TransitionSpeed => 40;

	protected virtual SceneEditorSession Session => SceneView.Session;
	EditorToolManager Tools => SceneView.Tools;

	public SceneRenderingWidget Renderer;

	ViewportOptions ViewportOptions;

	/// <summary>
	/// NOTE: You should not access position or rotation from here, get and set from  <see cref="State"/> instead.
	/// </summary>
	private CameraComponent _activeCamera;

	private CameraComponent _editorCamera;
	private CameraComponent _ejectCamera;

	internal RealTimeSince timeSinceCameraSpeedChange = 99;

	public SceneViewportWidget( SceneViewWidget sceneView, int id ) : base( sceneView )
	{
		SceneView = sceneView;
		Session.OnFrameTo += FrameOn;

		Id = id;
		if ( Id == 0 )
		{
			LastSelected = this;
		}

		if ( ProjectCookie.Get<ViewportState>( $"SceneView.Viewport{Id}.Settings", null ) is ViewportState savedSettings )
		{
			State = savedSettings;
		}
		else
		{
			State = new ViewportState();
			State.View = (ViewMode)(Id % typeof( ViewMode ).GetFields().Length);
		}

		InitializeCamera();

		AcceptDrops = true;

		Renderer = new SceneRenderingWidget( this );
		Renderer.Scene = Session.Scene;
		_editorCamera = Renderer.CreateSceneEditorCamera();
		_activeCamera = _editorCamera;
		Renderer.OnPreFrame += OnEditorPreFrame;

		Renderer.Camera = _activeCamera;

		GizmoInstance = Renderer.GizmoInstance;
		GizmoInstance.Settings = EditorScene.GizmoSettings;
		GizmoInstance.Selection = Session.Selection;

		Layout = Layout.Column();
		Layout.Add( Renderer );

		FocusMode = FocusMode.TabOrClickOrWheel;

		Overlay = new SceneOverlayWidget( this );
		Overlay.Position = ScreenPosition;
		Overlay.Size = Size;
		Overlay.Show();

		ViewportOptions = new ViewportOptions( this );
		Overlay.Header.Add( ViewportOptions );
		ViewportOptions.Show();

		FocusMode = FocusMode.None;
	}

	Vector3? cameraTargetPosition;
	Vector3 cameraVelocity;
	float cameraOrbitDistance = 400;
	bool doubleClick;

	protected override void OnDoubleClick( MouseEvent e )
	{
		base.OnDoubleClick( e );

		doubleClick = true;
	}

	protected override void OnVisibilityChanged( bool visible )
	{
		base.OnVisibilityChanged( visible );

		if ( Overlay.IsValid() )
		{
			Overlay.Visible = visible;
		}
	}

	protected override void OnFocus( FocusChangeReason reason )
	{
		base.OnFocus( reason );

		Session.MakeActive();
	}

	bool hasMouseInput = false;
	bool mouseWasPressed = false;
	int framesAfterRelease = 0;
	Vector2 initialMousePosition = Vector2.Zero;

	/// <summary>
	/// When the mouse is pressed, don't change the input enabled state
	/// until a few frames after it's released. If the mouse is pressed on the viewport it
	/// needs to have input until it's released (and then process the mouse up frame).
	/// If the mouse is pressed externally, we don't want to process the mouse down
	/// or mouse up commands on the viewport.
	/// </summary>
	void UpdateInputState()
	{
		// If mouse buttons are pressed, maintain current hasMouseInput state until released
		if ( Application.MouseButtons != MouseButtons.None )
		{
			mouseWasPressed = true;
			framesAfterRelease = 0;
			return;
		}

		// If mouse was just released, keep hasMouseInput stable for a few frames to ensure click processing
		if ( mouseWasPressed )
		{
			mouseWasPressed = false;
			framesAfterRelease = 3;
			return;
		}

		if ( framesAfterRelease > 0 )
		{
			framesAfterRelease--;
			return;
		}

		var hoveredWidget = Application.HoveredWidget;
		var hovered = hoveredWidget == Renderer;

		hasMouseInput = IsActiveWindow && hovered;
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		//if ( isFullscreen )
		{
			Paint.ClearPen();
			Paint.SetBrush( (Color)"#38393c" );
			Paint.DrawRect( LocalRect );
		}
	}

	/// <summary>
	/// Gets a camera size value based on a distance and field of view.
	/// </summary>
	private static float SizeFromDistanceAndFieldOfView( float distance, float fov )
		=> 2.0f * distance * MathF.Tan( 0.5f * MathX.DegreeToRadian( fov ) );

	/// <summary>
	/// Returns the distance the camera needs to be from the target to fit the ortho height in the view at the given FOV.
	/// </summary>
	float GetDollyDistance( float fovDegrees, float orthoHeight )
	{
		float fovRad = MathX.DegreeToRadian( fovDegrees );
		float halfAngle = MathF.Max( fovRad / 2, 0.0001f );
		var aspect = _activeCamera.ScreenRect.Width / _activeCamera.ScreenRect.Height;
		return (orthoHeight * aspect) / (2f * MathF.Tan( halfAngle ));
	}

	private bool was2d = false;
	void UpdateCamera()
	{
		if ( !_activeCamera.IsValid() )
		{
			Renderer.Camera = Renderer.CreateSceneEditorCamera();
			_activeCamera = Renderer.Camera;
		}

		_activeCamera.BackgroundColor = "#32415e";
		_activeCamera.WorldPosition = State.CameraPosition;
		_activeCamera.WorldRotation = State.CameraRotation;

		//
		// Smooth transition between ortho and perspective
		//
		if ( State.Is2D )
		{
			// todo: fog?
			if ( !was2d )
			{
				float distance = (_activeCamera.WorldRotation.Forward * State.CameraPosition).Length;
				CurrentOrthoHeight = SizeFromDistanceAndFieldOfView( distance, CurrentFOV );
				TargetFOV = 1f; // Going lower than that makes it more error prone depth precision wise
			}
		}
		else
		{
			TargetFOV = EditorPreferences.CameraFieldOfView;
		}

		// Small value to make the jump to ortho less jarring in low precision
		if ( TransitionBlend.AlmostEqual( 0f, 0.000001f ) ) TransitionBlend = 0;
		if ( TransitionBlend.AlmostEqual( 1f, 0.000001f ) ) TransitionBlend = 1;

		// Transition is [ 0, 1 ] or [ 1, 0 ] depending on going to or from 2D
		TransitionBlend = State.Is2D ?
			MathX.Lerp( TransitionBlend, 1f, RealTime.Delta * TransitionSpeed ) :
			MathX.Lerp( TransitionBlend, 0f, RealTime.Delta * TransitionSpeed );

		_activeCamera.OrthographicHeight = CurrentOrthoHeight;

		CurrentFOV = MathX.Lerp( CurrentFOV, TargetFOV, RealTime.Delta * TransitionSpeed );
		CurrentOrthoHeight = MathX.Lerp( CurrentOrthoHeight, State.CameraOrthoHeight, RealTime.Delta * TransitionSpeed );
		was2d = State.Is2D;

		_activeCamera.ClearFlags = ClearFlags.Color | ClearFlags.Depth | ClearFlags.Stencil;
		_activeCamera.ZNear = EditorPreferences.CameraZNear;
		_activeCamera.ZFar = State.Is2D ? MASSIVEZFAR : EditorPreferences.CameraZFar;
		_activeCamera.FieldOfView = CurrentFOV;
		_activeCamera.EnablePostProcessing = State.EnablePostProcessing;
		_activeCamera.Orthographic = State.Is2D && TransitionBlend.AlmostEqual( 1 );
		_activeCamera.OrthographicHeight = CurrentOrthoHeight;
		_activeCamera.DebugMode = State.RenderMode;
		_activeCamera.WireframeMode = State.WireframeMode;
		_activeCamera.CustomSize = Renderer.Size * DpiScale;

		// If we're in 2D mode, we can optionally show the skybox
		if ( State.Is2D )
		{
			if ( !State.ShowSkyIn2D )
				_activeCamera.BackgroundColor = Color.Black;

			_activeCamera.RenderExcludeTags.Set( "skybox", !State.ShowSkyIn2D );
		}

		if ( cameraTargetPosition is not null )
		{
			var currentPos = _activeCamera.WorldPosition;
			var targetPos = cameraTargetPosition.Value;

			// If camera position is fucked, just jump to target
			if ( currentPos.IsNaN || currentPos.IsInfinity )
			{
				_activeCamera.WorldPosition = targetPos;
				cameraTargetPosition = null;
				cameraVelocity = Vector3.Zero;
				return;
			}

			// If target is fucked, just ignore it
			if ( targetPos.IsNaN || targetPos.IsInfinity )
			{
				cameraTargetPosition = null;
				cameraVelocity = Vector3.Zero;
				return;
			}

			// If we're at a crazy distance, jump a bit closer first before smoothing
			var distance = targetPos.Distance( currentPos );
			if ( distance > 1e5f )
			{
				var direction = (targetPos - currentPos).Normal;
				_activeCamera.WorldPosition = targetPos - direction * 1e5f;
				currentPos = _activeCamera.WorldPosition;
				distance = targetPos.Distance( currentPos );
			}

			var pos = Vector3.SmoothDamp( currentPos, targetPos, ref cameraVelocity, 0.3f, RealTime.Delta );
			_activeCamera.WorldPosition = pos;

			if ( distance < 0.1f )
			{
				cameraTargetPosition = null;
			}
		}
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( SceneView.CurrentView == SceneViewWidget.ViewMode.Game )
		{
			Renderer.Focus();
			e.Accepted = false;
			return;
		}

		base.OnMousePress( e );

		if ( e.Button == MouseButtons.Right )
		{
			initialMousePosition = e.LocalPosition;
		}
	}

	SceneTraceResult? GetCursorTracePosition( Ray ray )
	{
		using ( GizmoInstance.Push() )
		{
			var tr = Session.Scene.Trace.Ray( ray, Gizmo.RayDepth )
				.UseRenderMeshes( true )
				.UsePhysicsWorld( false )
				.Run();

			if ( tr.Hit ) return tr;
		}

		return null;
	}

	Ray CursorTraceRay => _activeCamera.ScreenPixelToRay( initialMousePosition );

	SceneTraceResult? GetCursorTracePosition() => GetCursorTracePosition( CursorTraceRay );

	void PasteAtCursor()
	{
		EditorScene.Paste();

		var selections = Session.Selection.OfType<GameObject>().ToList();
		if ( selections.Count == 0 ) return;

		// Compute the average point of all selected objects
		Vector3 middlePoint = Vector3.Zero;
		foreach ( var go in selections )
			middlePoint += go.WorldPosition;

		middlePoint /= selections.Count;

		// Reposition all game objects relative to new center
		if ( GetCursorTracePosition() is SceneTraceResult hitPos )
		{
			foreach ( var go in selections )
			{
				Vector3 offset = go.WorldPosition - middlePoint;
				go.LocalPosition = hitPos.HitPosition + offset;
			}
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( SceneView.CurrentView == SceneViewWidget.ViewMode.Game )
		{
			Renderer.Focus();
			e.Accepted = false;
			return;
		}

		base.OnMouseReleased( e );

		var tool = SceneView?.Tools.CurrentTool;
		if ( tool is not null && !tool.AllowContextMenu )
			return;

		// Unity does a 6 pixel deadzone to trigger the context menu
		if ( e.KeyboardModifiers == KeyboardModifiers.None && e.Button == MouseButtons.Right &&
			 Vector2.DistanceBetween( initialMousePosition, e.LocalPosition ) < 6 )
		{
			var menu = new ContextMenu( this );
			bool HasSelection = Session.Selection.OfType<GameObject>().Any();
			menu.AddOption( "Cut", "content_cut", EditorScene.Cut, "editor.cut" ).Enabled = HasSelection;
			menu.AddOption( "Copy", "content_copy", EditorScene.Copy, "editor.copy" ).Enabled = HasSelection;
			menu.AddOption( "Paste", "content_paste", PasteAtCursor, "editor.paste" );
			menu.AddSeparator();
			menu.AddOption( "Duplicate", "file_copy", SceneEditorMenus.Duplicate, "editor.duplicate" ).Enabled = HasSelection;
			menu.AddOption( "Delete", "delete", SceneEditorMenus.Delete, "editor.delete" ).Enabled = HasSelection;

			menu.AddSeparator();

			Menu addMenu = menu.AddMenu( "Create" );

			var ray = CursorTraceRay;
			var trace = GetCursorTracePosition( ray );

			GameObjectNode.CreateObjectMenu( addMenu, null, go =>
			{
				if ( trace is { } tr )
				{
					var normal = tr.Normal;
					var bounds = go.GetBounds();
					var halfExtent = Vector3.Dot( bounds.Size, normal.Abs() ) / 2.0f;
					go.LocalPosition = tr.HitPosition + normal * halfExtent;
				}

				EditorScene.Selection.Clear();
				EditorScene.Selection.Add( go );
			} );

			var ev = new EditorEvent.ShowContextMenuEvent( Session, menu, ray, trace );

			EditorEvent.RunInterface<EditorEvent.ISceneView>( x => x.ShowContextMenu( ev ) );

			menu.OpenAtCursor();
		}
	}

	bool blockCamera;

	void OnEditorPreFrame()
	{
		// don't do editor update if we're the play view
		if ( IsGameView ) return;

		UpdateInputState();

		var isActive = Session == SceneEditorSession.Active;
		if ( !isActive && !hasMouseInput )
			return;

		MousePosition = Renderer.FromScreen( Application.CursorPosition ) * Renderer.DpiScale;

		UpdateCamera();
		DressPrefabScene();

		//
		// Play sounds from the point of view of the scene camera if we're not
		// playing the game, and this window is focused
		//
		if ( hasMouseInput )
		{
			Sound.Listener = _activeCamera.WorldTransform;
		}

		//
		// Handle input
		//

		var hasMouseFocus = hasMouseInput;
		if ( IsFocused )
		{
			LastSelected = this;
		}

		GizmoInstance.Input.IsHovered = hasMouseFocus;

		if ( IsActiveWindow ) // don't update camera input if the editor window isn't active
		{
			var rightMouse = Application.MouseButtons.HasFlag( MouseButtons.Right );
			var modifiers = Application.KeyboardModifiers != KeyboardModifiers.None;
			blockCamera = modifiers && (!blockCamera ? !rightMouse : blockCamera);

			_activeCamera.OrthographicHeight = State.CameraOrthoHeight;

			if ( !blockCamera )
			{
				if ( GizmoInstance.OrbitCamera( _activeCamera, Renderer, ref cameraOrbitDistance ) )
				{
					cameraTargetPosition = null;
					GizmoInstance.Input.IsHovered = false;
				}
				else if ( GizmoInstance.FirstPersonCamera( _activeCamera, Renderer, State.View == ViewMode.Perspective ) )
				{
					cameraTargetPosition = null;
					GizmoInstance.Input.IsHovered = false;
				}
			}
			else
			{
				Renderer.Cursor = CursorShape.None;
			}

			State.CameraPosition = _activeCamera.WorldPosition;
			State.CameraRotation = _activeCamera.WorldRotation;
			State.CameraOrthoHeight = _activeCamera.OrthographicHeight;
		}

		if ( State.Is2D )
		{
			Vector3 viewOffset = _activeCamera.WorldRotation.Forward;
			if ( TransitionBlend > 0f && TransitionBlend < 1f )
			{
				// Dolly out when transitioning to ortho
				float dolly = GetDollyDistance( CurrentFOV, _activeCamera.OrthographicHeight );
				viewOffset *= MathF.Min( dolly, EditorPreferences.CameraZFar ) * TransitionBlend;
			}
			else
			{
				// Keep ortho clipping planes predictable
				viewOffset *= _activeCamera.ZFar / 2;
			}

			_activeCamera.WorldPosition -= viewOffset;
		}
		else if ( TransitionBlend > 0f && TransitionBlend < 1f )
		{
			// Dolly in when transitioning to perspective
			float dolly = GetDollyDistance( CurrentFOV, _activeCamera.OrthographicHeight );
			_activeCamera.WorldPosition -= _activeCamera.WorldRotation.Forward * MathF.Min( dolly, EditorPreferences.CameraZFar ) * TransitionBlend;
		}

		if ( GizmoInstance.Input.IsHovered && hasMouseFocus )
			GizmoInstance.Input.DoubleClick = doubleClick;

		doubleClick = false;

		Renderer.UpdateGizmoInputs( GizmoInstance.Input.IsHovered && hasMouseFocus );

		if ( State.ShowGrid )
		{
			using ( Gizmo.Scope( "grid" ) )
			{
				Gizmo.Draw.IgnoreDepth = State.Is2D;
				Gizmo.Draw.Grid( State.GridAxis, Gizmo.Settings.GridSpacing, State.GridOpacity );
			}
		}

		if ( GizmoInstance.Input.IsHovered )
		{
			UpdateHovered();
		}

		Tools.Frame( _activeCamera, Session );

		EditorEvent.RunInterface<EditorEvent.ISceneView>( x => x.DrawGizmos( Session.Scene ) );
		Session.Scene.EditorDraw();

		DrawSelection();
		UpdateDragDrops();

		DrawCameraSpeedOverlay();
	}

	/// <summary>
	/// We add a light in the prefab scene if there is no light. This can be toggled via the viewport settings.
	/// </summary>
	void DressPrefabScene()
	{
		if ( Session.Scene is not PrefabScene prefabScene )
			return;

		if ( !prefabScene.Active )
			return;

		var directionalLights = prefabScene.GetAllComponents<DirectionalLight>().ToArray();

		var prefabLight = directionalLights.FirstOrDefault( x => x.GameObject.Flags.HasFlag( GameObjectFlags.NotSaved ) && x.GameObject.Flags.HasFlag( GameObjectFlags.Hidden ) && x.GameObject.Tags.Has( "____prefabLight" ) );

		var otherLightsExist = directionalLights.Where( x => x != prefabLight ).Any();

		if ( prefabLight.IsValid() && (otherLightsExist || !State.EnablePrefabLighting) )
		{
			// Scene had or added a light, remove the prefab light
			prefabLight.GameObject.Destroy();
			prefabLight = null;
			return;
		}

		if ( State.EnablePrefabLighting && !prefabLight.IsValid() )
		{
			var go = Session.Scene.CreateObject();
			go.Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden;
			go.Tags.Add( "____prefabLight" );
			prefabLight = go.Components.Create<DirectionalLight>();
			prefabLight.LightColor = Color.White;
			prefabLight.SkyColor = "#557685";
		}

	}

	void DrawSelection()
	{
		var session = Session;
		if ( session is null ) return;
		if ( !Gizmo.Settings.Selection ) return;

		foreach ( var modelRenderer in session.Selection.OfType<GameObject>().SelectMany( x => x.Components.GetAll<ModelRenderer>() ) )
		{
			if ( modelRenderer.Model == null )
				continue;

			using ( Gizmo.ObjectScope( modelRenderer.GameObject, default ) )
			{
				Gizmo.Transform = modelRenderer.GameObject.WorldTransform;
				Gizmo.Draw.Color = Gizmo.Colors.Selected;
				Gizmo.Draw.LineBBox( modelRenderer.Model.Bounds );
			}
		}
	}

	void DrawCameraSpeedOverlay()
	{
		if ( timeSinceCameraSpeedChange >= 2f )
			return;

		using ( Gizmo.Scope( "speed-overlay" ) )
		{
			var scale = Application.DpiScale;
			var baseRect = LocalRect * scale;

			var iconSize = new Vector2( 16f, 48f ) * scale;
			var iconFontSize = 18 * scale;

			Gizmo.Draw.Color = Color.White.WithAlpha( 2f - timeSinceCameraSpeedChange );
			Gizmo.Draw.ScreenText( "speed", new Vector2( baseRect.Width - iconSize.x, iconSize.y ), "Material Icons", iconFontSize, TextFlag.Center );

			var barSize = new Rect( 24, 64, 12, 100 ) * scale;

			var rect = new Rect(
				baseRect.Width - barSize.Left,
				barSize.Top,
				barSize.Width,
				baseRect.Height - barSize.Height
			);

			Gizmo.Draw.ScreenRect( rect, Color.Transparent, default, Gizmo.Draw.Color, new Vector4( 2 ) );

			var height = rect.Height * Math.Clamp( (EditorPreferences.CameraSpeed - 0.25f) / (100f - 0.25f), 0, 1 );
			rect.Position = rect.Position.WithY( rect.Position.y + rect.Height - height );
			rect.Height = height;
			rect = rect.Shrink( 2 );

			var speedSize = new Vector2( 16f, 16f ) * scale;
			var speedFontSize = 12 * scale;
			Gizmo.Draw.ScreenRect( rect, Gizmo.Draw.Color );
			Gizmo.Draw.ScreenText( EditorPreferences.CameraSpeed.ToString(), new Vector2( baseRect.Width - speedSize.x, baseRect.Height - speedSize.y ), "Inter", speedFontSize, TextFlag.Center );
		}
	}

	void UpdateHovered()
	{
		var session = Session;
		if ( session is null ) return;
		if ( !Gizmo.Settings.Selection ) return;

		// Trace models in the scene, hover the hit one
		var tr = session.Scene.Trace.Ray( Gizmo.CurrentRay, Gizmo.RayDepth )
					.UseRenderMeshes( true )
					.UsePhysicsWorld( false )
					.Run();

		if ( tr.Hit && tr.Component.IsValid() )
		{
			using ( Gizmo.ObjectScope( tr.GameObject, tr.GameObject.WorldTransform ) )
			{
				Gizmo.Hitbox.DepthBias = 1;
				Gizmo.Hitbox.TrySetHovered( tr.Distance );

				if ( !Gizmo.IsHovered )
					return;

				if ( tr.Component is ModelRenderer mr && mr.Model is not null && !session.Selection.Contains( tr.GameObject ) )
				{
					Gizmo.Draw.Color = Gizmo.Colors.Active.WithAlpha( MathF.Sin( RealTime.Now * 20.0f ).Remap( -1, 1, 0.3f, 0.8f ) );
					Gizmo.Draw.LineBBox( mr.Model.Bounds );
				}
			}
		}
	}

	void FrameOn( BBox target )
	{
		var distance = MathX.SphereCameraDistance( target.Size.Length, _activeCamera.FieldOfView ) * 1.0f;
		var targetPos = target.Center + distance * _activeCamera.WorldRotation.Backward;

		cameraTargetPosition = target.Center + distance * _activeCamera.WorldRotation.Backward;
		cameraOrbitDistance = target.Center.Distance( cameraTargetPosition.Value );

		GizmoInstance.SetValue<Vector3?>( "CameraTarget", null );
		GizmoInstance.SetValue<Vector3>( "CameraVelocity", 0 );
	}

	public override void OnDestroyed()
	{
		Session.OnFrameTo -= FrameOn;

		_activeCamera?.GameObject?.Destroy();
		_activeCamera = null;
	}
}
