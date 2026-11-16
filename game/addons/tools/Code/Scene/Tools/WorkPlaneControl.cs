namespace Editor;

sealed class WorkPlaneControl
{
	public enum EditMode
	{
		None,
		Move,
		Rotate
	}

	public EditMode Mode { get; private set; }
	public bool IsActive => Mode != EditMode.None;
	public bool IsHandlingInput => IsActive || _waitForRelease || _waitForKeysRelease;

	SceneEditorSession _session;
	Transform _start;
	Vector3 _movement;
	bool _dragging;
	bool _waitForRelease;
	bool _waitForKeysRelease;
	SceneCamera _dragCamera;
	readonly Dictionary<Widget, (CursorShape Shape, Pixmap Pixmap)> _cursors = [];
	Pixmap _pickCursor;

	public void Begin( SceneEditorSession session, EditMode mode )
	{
		Finish();
		_session = session;
		Mode = mode;
		_start = session.WorkPlane;
		_movement = default;
	}

	public void Finish()
	{
		_waitForKeysRelease |= IsActive;
		foreach ( var (widget, cursor) in _cursors )
		{
			if ( !widget.IsValid() ) continue;
			widget.Cursor = cursor.Shape;
			if ( cursor.Pixmap is not null )
				widget.PixmapCursor = cursor.Pixmap;
		}
		_cursors.Clear();
		if ( _session is not null )
			_session.SetWorkPlane( _session.WorkPlane );
		Mode = EditMode.None;
		_dragging = false;
	}

	public void Reset( SceneEditorSession session )
	{
		Finish();
		session.ResetWorkPlane();
	}

	public bool Frame( SceneEditorSession session, Widget viewport )
	{
		if ( session != _session )
			Finish();

		if ( !IsActive )
		{
			if ( _waitForKeysRelease )
			{
				if ( EditorShortcuts.IsDown( "scene.workplane-finish" ) ||
					EditorShortcuts.IsDown( "scene.workplane-drop" ) ||
					EditorShortcuts.IsDown( "scene.workplane-cancel" ) )
					return true;
				_waitForKeysRelease = false;
			}
			if ( !_waitForRelease ) return false;
			if ( Gizmo.HasMouseFocus && !Gizmo.IsLeftMouseDown )
				_waitForRelease = false;
			return true;
		}

		if ( Gizmo.HasMouseFocus )
			_waitForRelease = Gizmo.IsLeftMouseDown;
		var frame = session.WorkPlane;
		if ( (Gizmo.WasLeftMousePressed || !Gizmo.Pressed.Any) && (!_dragging || _dragCamera == Gizmo.Camera) )
		{
			if ( _dragging )
				session.SetWorkPlane( frame );
			_dragging = false;
			_start = frame;
			_movement = default;
		}

		UpdateCursor( viewport );
		if ( Pick( session ) )
		{
			_start = session.WorkPlane;
			_movement = default;
		}

		using ( Gizmo.Scope( "WorkPlane", _start.WithPosition( session.WorkPlane.Position ) ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Hitbox.DepthBias = 0.01f;
			if ( Mode != EditMode.Move )
			{
				if ( Gizmo.Control.UpdateRotation( "Rotate", Rotation.Identity, out var rotation ) )
				{
					session.SetWorkPlane( new Transform( _start.Position, _start.Rotation * Gizmo.Snap( rotation ) ), true );
					_dragging = true;
					_dragCamera = Gizmo.Camera;
				}
			}
			else if ( Gizmo.Control.UpdatePosition( "Move", Vector3.Zero, out var delta, Rotation.Identity ) )
			{
				_movement += delta;
				var position = _start.PointToWorld( Gizmo.Snap( _movement, _movement ) );
				session.SetWorkPlane( new Transform( position, _start.Rotation ), true );
				_dragging = true;
				_dragCamera = Gizmo.Camera;
			}
			Gizmo.Transform = session.WorkPlane;
			if ( Mode == EditMode.Move )
				Gizmo.Draw.Position( "Move", Rotation.Identity );
			else
				Gizmo.Draw.Rotate( "Rotate" );
		}

		return true;
	}

	void UpdateCursor( Widget widget )
	{
		if ( !widget.IsValid() ) return;

		_cursors.TryAdd( widget, (widget.Cursor, widget.PixmapCursor) );
		if ( Gizmo.HasHovered || Gizmo.Pressed.Any )
		{
			if ( widget.Cursor != CursorShape.Arrow )
				widget.Cursor = CursorShape.Arrow;
		}
		else
		{
			_pickCursor ??= Pixmap.FromFile( "cursors/eyedropper_centered.png" );
			widget.PixmapCursor = _pickCursor;
		}
	}

	bool Pick( SceneEditorSession session )
	{
		if ( !Gizmo.HasMouseFocus || Gizmo.HasHovered || Gizmo.Pressed.Any )
			return false;

		var trace = session.Scene.Trace.Ray( Gizmo.CurrentRay, Gizmo.RayDepth )
			.UseRenderMeshes( true, EditorPreferences.BackfaceSelection )
			.WithoutTags( "hidden" )
			.UsePhysicsWorld( false );
		var hit = Gizmo.Camera.Ortho ? default : trace.Run();
		Transform picked;
		if ( Gizmo.IsShiftPressed && hit.Hit && hit.GameObject.IsValid() )
			picked = new Transform( hit.GameObject.WorldPosition, hit.GameObject.WorldRotation );
		else if ( Gizmo.IsShiftPressed || !WorkPlane.TryPickSurface( hit, out picked ) )
		{
			if ( !WorkPlane.TryGetPlacement( trace, session.WorkPlane, true, out var point, out _ ) )
				return false;
			picked = new Transform( WorkPlane.Snap( point ), session.WorkPlane.Rotation );
		}

		DrawPreview( hit, picked );
		if ( !Gizmo.WasLeftMousePressed ) return false;
		session.SetWorkPlane( picked );
		return true;
	}

	static void DrawPreview( SceneTraceResult hit, Transform frame )
	{
		using var scope = Gizmo.Scope( "WorkPlanePreview" );
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 2;
		Gizmo.Draw.Color = Gizmo.Colors.Active;
		if ( Gizmo.IsShiftPressed && hit.GameObject.IsValid() )
		{
			Gizmo.Draw.LineBBox( hit.GameObject.GetBounds() );
		}
		else if ( hit.Component is MeshComponent { Mesh: { } mesh } component )
		{
			var face = mesh.TriangleToFace( hit.Triangle );
			if ( face.IsValid )
			{
				using var surface = Gizmo.Scope( "Surface", component.WorldTransform );
				foreach ( var edge in mesh.GetFaceEdges( face ) )
					Gizmo.Draw.Line( mesh.GetEdgeLine( edge ) );
			}
		}

		Gizmo.Transform = frame;
		using var scale = Gizmo.GizmoControls.PushFixedScale();
		Gizmo.Draw.Color = Gizmo.Colors.Active.WithAlpha( 0.5f );
		for ( var i = -2; i <= 2; i++ )
		{
			Gizmo.Draw.Line( new Vector3( i * 6, -12, 0 ), new Vector3( i * 6, 12, 0 ) );
			Gizmo.Draw.Line( new Vector3( -12, i * 6, 0 ), new Vector3( 12, i * 6, 0 ) );
		}
		Gizmo.Draw.Color = Gizmo.Colors.Forward;
		Gizmo.Draw.Arrow( Vector3.Zero, Vector3.Forward * 18, 4, 2 );
		Gizmo.Draw.Color = Gizmo.Colors.Left;
		Gizmo.Draw.Arrow( Vector3.Zero, Vector3.Left * 18, 4, 2 );
		Gizmo.Draw.Color = Gizmo.Colors.Up;
		Gizmo.Draw.Arrow( Vector3.Zero, Vector3.Up * 12, 4, 2 );
		Gizmo.Draw.Color = Color.White;
		Gizmo.Draw.SolidSphere( Vector3.Zero, 1.5f );
	}
}
