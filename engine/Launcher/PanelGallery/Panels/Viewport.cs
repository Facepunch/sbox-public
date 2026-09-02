#pragma warning disable CS0618 // ScenePanel's world/camera API is obsolete, but it's the one that lets us drive the camera ourselves

namespace Sandbox.PanelGallery;

/// <summary>
/// A scene view in a panel, with its own fly camera. If a scene is open it renders that - in the
/// gallery there usually isn't one, so it's the grid and whatever the gizmos draw, which is enough
/// to prove a 3D view lives happily inside panel UI.
/// </summary>
public class Viewport : Panel
{
	/// <summary>
	/// Something was clicked in the view.
	/// </summary>
	public Action<GameObject> OnPicked { get; set; }

	ScenePanel scene;
	Sandbox.UI.Label modeLabel;
	Sandbox.UI.Label statsLabel;

	readonly HashSet<string> heldKeys = new();

	/// <summary>
	/// Our own gizmo instance. Gizmo geometry goes into the instance's own world, so drawing into
	/// it and adding that world to our camera is what puts gizmos in this view.
	/// </summary>
	readonly Gizmo.Instance gizmo = new();

	Vector3 cameraPosition = new( -320, 220, 210 );
	Rotation cameraRotation = Rotation.From( 18, -35, 0 );

	bool looking;
	Vector2 lastCursor;
	float moveSpeed = 500.0f;

	public Viewport()
	{
		AddClass( "viewport" );

		AcceptsFocus = true;

		BuildScene();
		BuildOverlay();
	}

	void BuildScene()
	{
		scene = AddChild<ScenePanel>();
		scene.AddClass( "scene" );

		scene.Camera.BackgroundColor = Color.FromBytes( 8, 9, 14 );
		scene.Camera.AmbientLightColor = Color.FromBytes( 40, 44, 60 );

		scene.Camera.Worlds.Add( gizmo.World );
	}

	void BuildOverlay()
	{
		var overlay = Add.Panel( "overlay" );

		// Frame rate in the corner, like the editor's viewport
		statsLabel = overlay.Add.Label( "", "fps" );

		BuildToolStrip( overlay );
		BuildAxisWidget( overlay );

		var hud = overlay.Add.Panel( "hud" );
		hud.Add.Panel( "grow" );

		var live = hud.Add.Panel( "chip live" );
		live.Add.Panel( "dot" );
		modeLabel = live.Add.Label( "Free Camera" );

		overlay.Add.Panel( "vignette" );
	}

	readonly Dictionary<string, Panel> toolButtons = new();

	/// <summary>
	/// The move/rotate/scale strip down the left. In the gallery it's the control being proven,
	/// not a real tool switch - picking one just moves the highlight.
	/// </summary>
	void BuildToolStrip( Panel overlay )
	{
		var strip = overlay.Add.Panel( "toolstrip" );

		Tool( strip, "control_camera", "position" );
		Tool( strip, "360", "rotation" );
		Tool( strip, "zoom_out_map", "scale" );
	}

	void Tool( Panel strip, string icon, string toolName )
	{
		var button = strip.Clickable( "toolbutton", () => SetTool( toolName ) );
		button.Icon( icon );

		toolButtons[toolName] = button;
	}

	string currentTool = "position";

	void SetTool( string toolName )
	{
		currentTool = toolName;

		foreach ( var (name, button) in toolButtons )
		{
			button.SetClass( "active", name == currentTool );
		}
	}

	//
	// The little axis ball. Each arm is a panel put where that axis points on screen, so it spins
	// with the camera the way the editor's does.
	//
	Panel axisWidget;
	readonly List<(Vector3 Axis, Panel Panel)> axisArms = new();

	void BuildAxisWidget( Panel overlay )
	{
		axisWidget = overlay.Add.Panel( "axiswidget" );

		Arm( Vector3.Forward, "X", "x" );
		Arm( Vector3.Backward, null, "x" );
		Arm( Vector3.Left, "Y", "y" );
		Arm( Vector3.Right, null, "y" );
		Arm( Vector3.Up, "Z", "z" );
		Arm( Vector3.Down, null, "z" );
	}

	void Arm( Vector3 axis, string title, string className )
	{
		var arm = axisWidget.Add.Panel( "arm" );
		arm.AddClass( className );
		arm.SetClass( "negative", title is null );

		if ( title is not null ) arm.Add.Label( title );

		axisArms.Add( (axis, arm) );
	}

	const float AxisRadius = 21.0f;

	void UpdateAxisWidget()
	{
		var rotation = scene.Camera.Rotation.Inverse;

		foreach ( var (axis, arm) in axisArms )
		{
			var local = rotation * axis;

			// Camera space is x forward, y left, z up - screen right is -y, screen up is +z
			arm.Style.Left = 26.0f - local.y * AxisRadius;
			arm.Style.Top = 26.0f - local.z * AxisRadius;

			// Arms pointing away sit behind, and fade back
			arm.Style.ZIndex = local.x > 0 ? 1 : 3;
			arm.Style.Opacity = local.x > 0 ? 0.4f : 1.0f;
		}
	}
	RealTimeSince timeSinceStats;

	public override void Tick()
	{
		var session = SceneEditorSession.Active;
		var world = session?.Scene?.SceneWorld;

		if ( scene.Camera.World != world )
		{
			scene.Camera.World = world;
		}

		Fly();

		scene.Camera.Position = cameraPosition;
		scene.Camera.Rotation = cameraRotation;

		DrawGizmos( session );

		UpdateAxisWidget();

		if ( timeSinceStats > 0.2f )
		{
			timeSinceStats = 0;

			statsLabel.Text = $"{1.0f / MathF.Max( RealTime.Delta, 0.0001f ):0} FPS";

			modeLabel.Text = looking ? "Flying" : "Free Camera";

			foreach ( var (name, button) in toolButtons )
			{
				button.SetClass( "active", name == currentTool );
			}
		}
	}

	/// <summary>
	/// The grid, whatever the scene's components draw, and the selection. Runs inside our own
	/// gizmo instance so it ends up in our view.
	/// </summary>
	void DrawGizmos( SceneEditorSession session )
	{
		gizmo.Input.Camera = scene.Camera;
		gizmo.Input.IsHovered = false; // we only draw - picking is a plain trace
		gizmo.Input.CursorPosition = MousePosition;
		gizmo.Input.CursorRay = scene.Camera.GetRay( MousePosition, Box.Rect.Size );

		if ( session?.Scene is not { } drawScene )
		{
			// No scene open - the grid on its own still proves the view works
			using ( gizmo.Push() )
			using ( Gizmo.Scope( "grid" ) )
			{
				Gizmo.Draw.Grid( Gizmo.GridAxis.XY, 64.0f, 0.3f );
			}

			return;
		}

		gizmo.Selection = session.Selection;

		using ( drawScene.Push() )
		using ( gizmo.Push() )
		{
			using ( Gizmo.Scope( "grid" ) )
			{
				Gizmo.Draw.Grid( Gizmo.GridAxis.XY, Gizmo.Settings?.GridSpacing ?? 64.0f, 0.3f );
			}

			// Components draw their own gizmos here - light cones, camera frustums, colliders
			drawScene.EditorDraw();

			DrawSelection( session );
		}
	}

	static void DrawSelection( SceneEditorSession session )
	{
		foreach ( var item in session.Selection.OfType<GameObject>() )
		{
			if ( !item.IsValid() ) continue;

			var bounds = item.GetBounds();
			if ( bounds.Size.Length < 0.01f ) continue;

			using ( Gizmo.Scope( "selection" ) )
			{
				Gizmo.Draw.Color = Color.FromBytes( 76, 141, 255 );
				Gizmo.Draw.LineThickness = 2;
				Gizmo.Draw.LineBBox( bounds );
			}
		}
	}

	/// <summary>
	/// Right mouse to look, WASD to move. The camera is ours, so this just moves it.
	/// </summary>
	void Fly()
	{
		if ( !looking )
		{
			heldKeys.Clear();
			return;
		}

		var cursor = MousePosition;
		var delta = cursor - lastCursor;
		lastCursor = cursor;

		if ( delta.Length > 0 )
		{
			var angles = cameraRotation.Angles();

			angles.yaw -= delta.x * 0.12f;
			angles.pitch = (angles.pitch + delta.y * 0.12f).Clamp( -89, 89 );
			angles.roll = 0;

			cameraRotation = angles.ToRotation();
		}

		var move = Vector3.Zero;

		if ( heldKeys.Contains( "w" ) ) move += Vector3.Forward;
		if ( heldKeys.Contains( "s" ) ) move += Vector3.Backward;
		if ( heldKeys.Contains( "a" ) ) move += Vector3.Left;
		if ( heldKeys.Contains( "d" ) ) move += Vector3.Right;
		if ( heldKeys.Contains( "e" ) ) move += Vector3.Up;
		if ( heldKeys.Contains( "q" ) ) move += Vector3.Down;

		if ( move.Length > 0 )
		{
			var speed = moveSpeed * (heldKeys.Contains( "lshift" ) ? 3.0f : 1.0f);
			cameraPosition += cameraRotation * move.Normal * speed * RealTime.Delta;
		}
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		Focus();

		if ( e.MouseButton == MouseButtons.Right )
		{
			looking = true;
			lastCursor = MousePosition;

			SetMouseCapture( true );
			return;
		}

		if ( e.MouseButton == MouseButtons.Left )
		{
			Pick();
		}
	}

	/// <summary>
	/// Trace into the scene from where the cursor is and select whatever it hits.
	/// </summary>
	void Pick()
	{
		var session = SceneEditorSession.Active;
		if ( session?.Scene is not { } sceneToPick ) return;

		var size = Box.Rect.Size;
		if ( size.x < 1 || size.y < 1 ) return;

		var ray = scene.Camera.GetRay( MousePosition, size );

		var trace = sceneToPick.Trace.Ray( ray, 100000 ).Run();

		var hit = trace.Hit ? trace.GameObject : null;

		session.Selection.Set( hit );
		OnPicked?.Invoke( hit );
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		if ( e.MouseButton != MouseButtons.Right ) return;

		looking = false;
		SetMouseCapture( false );
	}

	public override void OnButtonEvent( ButtonEvent e )
	{
		if ( e.Pressed ) heldKeys.Add( e.Button );
		else heldKeys.Remove( e.Button );

		base.OnButtonEvent( e );
	}

	public override void OnMouseWheel( Vector2 value )
	{
		// Scroll changes how fast flying moves, same as the real viewport
		moveSpeed = MathX.Clamp( moveSpeed * (1.0f - value.y * 0.1f), 50.0f, 5000.0f );
	}
}
