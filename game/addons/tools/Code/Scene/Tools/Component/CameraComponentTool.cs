namespace Editor;

public class CameraEditorTool : EditorTool<CameraComponent>
{
	CameraToolWindow window;

	/// <summary>
	/// Track when mouse is pressed during look-at mode (prevent exiting mode immediately)
	/// </summary>
	bool lookAtMousePressed = false;

	public override void OnEnabled()
	{
		window = new CameraToolWindow();
		AddOverlay( window, TextFlag.RightBottom, 10 );
	}

	public override void OnUpdate()
	{
		window.ToolUpdate();
		AllowGameObjectSelection = EditorToolManager.CurrentModeName != "camera.lookat";

		if ( EditorToolManager.CurrentModeName == "camera.lookat" )
			DoCameraLookAt();
		else
			lookAtMousePressed = false; // Reset when not in look-at mode
	}

	public override bool ShouldKeepActive()
	{
		return window.ShouldKeepActive();
	}

	public override void OnDisabled()
	{

	}

	public override void OnSelectionChanged()
	{
		var camera = GetSelectedComponent<CameraComponent>();
		window.OnSelectionChanged( camera );
	}

	void DoCameraLookAt()
	{
		var camera = GetSelectedComponent<CameraComponent>();

		if ( !camera.IsValid() )
		{
			EditorToolManager.CurrentModeName = "object";
			return;
		}

		using ( Gizmo.ObjectScope( camera, Transform.Zero ) )
		{
			var tr = MeshTrace.Run();
			if ( tr.Hit && camera.IsValid() )
			{
				camera.WorldRotation = Rotation.LookAt( tr.HitPosition - camera.WorldPosition );

				using ( Gizmo.Scope( "Aim Handle", new Transform( tr.HitPosition, Rotation.LookAt( tr.Normal ) ) ) )
				{
					Gizmo.Draw.IgnoreDepth = true;
					Gizmo.Draw.Color = Color.White;
					Gizmo.Draw.LineThickness = 2;
					Gizmo.Draw.LineCircle( 0, 8 );
					Gizmo.Draw.Color = Color.White.WithAlpha( 0.5f );
					Gizmo.Draw.LineCircle( 0, 12 );
					Gizmo.Draw.Color = Color.White.WithAlpha( 0.3f );
					Gizmo.Draw.LineCircle( 0, 24 );
					Gizmo.Draw.Color = Color.White.WithAlpha( 0.1f );
					Gizmo.Draw.LineCircle( 0, 48 );
				}
			}

			// Track if mouse was pressed during look-at mode
			if ( Gizmo.WasLeftMousePressed )
			{
				lookAtMousePressed = true;
			}

			// Only exit if mouse was pressed AND released during look-at mode
			if ( lookAtMousePressed && Gizmo.WasLeftMouseReleased )
			{
				EditorToolManager.CurrentModeName = "object";
			}
		}
	}
}


class CameraToolWindow : WidgetWindow
{
	private CameraComponent selectedComponent;
	CameraComponent targetComponent;
	SceneWidget SceneWidget;

	private static CameraComponent PinnedCamera;
	private static bool IsPinned;
	static bool IsClosed = false;

	public CameraToolWindow()
	{
		ContentMargins = 0;
		Layout = Layout.Column();

		Rebuild();
	}

	private IconButton _pinButton;

	void Rebuild()
	{
		if ( IsPinned && PinnedCamera.IsValid() )
		{
			targetComponent = PinnedCamera;
		}

		Layout.Clear( true );
		Layout.Margin = 0;
		Icon = IsClosed ? "" : "photo_camera";
		WindowTitle = IsClosed ? "" : $"Camera Preview - {targetComponent?.GameObject?.Name ?? ""}";
		IsGrabbable = !IsClosed;

		if ( IsPinned )
			WindowTitle += " (Pinned)";

		if ( IsClosed )
		{
			var closedRow = Layout.AddRow();
			closedRow.Add( new IconButton( "photo_camera", () => { IsClosed = false; Rebuild(); } ) { ToolTip = "Open Camera Preview", FixedHeight = HeaderHeight, FixedWidth = HeaderHeight, Background = Theme.ControlBackground } );
			return;
		}

		var headerRow = Layout.AddRow();
		headerRow.AddStretchCell();

		_pinButton = new IconButton( IsPinned ? "lock_open" : "lock", TogglePinned )
		{
			ToolTip = IsPinned ? "Unpin" : "Pin",
			FixedHeight = HeaderHeight,
			FixedWidth = HeaderHeight,
			Background = Theme.ControlBackground
		};

		headerRow.Add( _pinButton );
		headerRow.Add( new IconButton( "colorize", LookAt ) { ToolTip = "Look At", FixedHeight = HeaderHeight, FixedWidth = HeaderHeight, Background = Theme.ControlBackground } );
		headerRow.Add( new IconButton( "close", CloseWindow ) { ToolTip = "Close Preview", FixedHeight = HeaderHeight, FixedWidth = HeaderHeight, Background = Theme.ControlBackground } );

		SceneWidget = new SceneWidget( this );
		SceneWidget.FixedWidth = 1280 * 0.4f;
		SceneWidget.FixedHeight = 720 * 0.4f;

		if ( targetComponent is CameraComponent camera )
		{
			SceneWidget.Scene = camera.Scene;
		}

		Layout.Add( SceneWidget );
		Layout.Margin = 4;
	}

	private void TogglePinned()
	{
		SetPinnedState( !IsPinned );
	}

	private void SetPinnedState( bool isPinned )
	{
		if ( IsPinned == isPinned )
			return;

		IsPinned = isPinned;

		_pinButton.Icon = isPinned ? "lock_open" : "lock";
		_pinButton.ToolTip = isPinned ? "Unpin" : "Pin";
		_pinButton.Update();

		UpdateWindowTitle();

		if ( !isPinned && selectedComponent.IsValid() && selectedComponent != targetComponent )
		{
			OnSelectionChanged( selectedComponent );
		}

		if ( isPinned )
		{
			if ( selectedComponent.IsValid() )
			{
				PinnedCamera = selectedComponent;
			}
		}
		else
		{
			if ( !selectedComponent.IsValid() )
			{
				CloseWindow();
			}

			PinnedCamera = null;
		}
	}

	void LookAt()
	{
		EditorToolManager.CurrentModeName = "camera.lookat";
		// maintain focus on scene even after clicking the button
		SceneViewportWidget.LastSelected?.Focus();
	}

	void CloseWindow()
	{
		IsClosed = true;
		Release();
		Rebuild();
		Position = Parent.Size - 32;
	}

	public bool ShouldKeepActive()
	{
		return IsPinned && targetComponent.IsValid();
	}

	public void ToolUpdate()
	{
		if ( !targetComponent.IsValid() )
			return;

		if ( SceneWidget.IsValid() )
		{
			targetComponent.UpdateSceneCamera( SceneWidget.Camera );
			SceneWidget.Camera.Rect = new Rect( 0, 0, 1, 1 );
		}
	}

	void UpdateWindowTitle()
	{
		if ( IsClosed )
		{
			WindowTitle = "";
			Update();
			return;
		}

		if ( targetComponent.IsValid() && targetComponent.GameObject.IsValid() )
			WindowTitle = $"Camera Preview - {targetComponent.GameObject.Name}";
		else
			WindowTitle = "Camera Preview";

		if ( IsPinned )
			WindowTitle += " (Pinned)";

		Update();
	}

	internal void OnSelectionChanged( CameraComponent camera )
	{
		selectedComponent = camera;

		if ( camera.IsValid() && camera != PinnedCamera )
		{
			SetPinnedState( false );
		}

		if ( targetComponent.IsValid() )
		{
			// Don't do anything if we're pinned
			if ( IsPinned && (!camera.IsValid() || (targetComponent != camera)) )
				return;
		}

		if ( camera.IsValid() && IsPinned )
		{
			PinnedCamera = camera;
		}

		targetComponent = camera;
		UpdateWindowTitle();

		if ( SceneWidget.IsValid() )
			SceneWidget.Scene = camera.IsValid() ? camera.Scene : null;
	}
}

class SceneWidget : Widget
{
	public Scene Scene { get; set; }
	public SceneCamera Camera { get; set; } = new SceneCamera();

	Pixmap pixmap;

	public SceneWidget( Widget parent ) : base( parent )
	{

	}

	[EditorEvent.Frame]
	internal void Frame()
	{
		if ( !Visible ) return;

		var realSize = Size * DpiScale;

		if ( pixmap is null || pixmap.Size != realSize )
		{
			pixmap = new Pixmap( realSize );
		}

		if ( Scene.IsValid() )
		{
			Camera.World = Scene.SceneWorld;
			Camera.Worlds.Clear();

			Camera.RenderToPixmap( pixmap );
		}

		Update();
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		if ( pixmap is not null )
		{
			Paint.Draw( LocalRect, pixmap );
		}
	}
}
