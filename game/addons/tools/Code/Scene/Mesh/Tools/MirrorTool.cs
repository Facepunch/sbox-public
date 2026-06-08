namespace Editor.MeshEditor;

[Alias( "tools.mirror-tool" )]
public partial class MirrorTool( string tool ) : EditorTool
{
	Plane? _hitPlane;
	Plane? _plane;
	Vector3 _point1;
	Vector3 _point2;

	Vector3 _dragStartP1;
	Vector3 _dragStartP2;

	readonly List<(Transform, GameObject)> _selectedObjects = [];

	private IDisposable _undoScope;

	void Reset()
	{
		_hitPlane = default;
		_plane = default;
		_point1 = default;
		_point2 = default;

		_undoScope?.Dispose();
		_undoScope = default;
	}

	public override void OnEnabled()
	{
		Reset();

		using var scope = SceneEditorSession.Scope();

		_undoScope = SceneEditorSession.Active.UndoScope( "Mirror Selection" )
			.WithGameObjectCreations()
			.Push();

		foreach ( var go in Selection.OfType<GameObject>() )
		{
			var copy = go.Clone( go.WorldTransform );
			_selectedObjects.Add( new( go.WorldTransform, copy ) );

			foreach ( var mc in copy.GetComponentsInChildren<MeshComponent>() )
			{
				mc.Mesh = BuildMesh( mc );
			}
		}

		if ( _selectedObjects.Count > 0 ) return;

		foreach ( var group in Selection.OfType<MeshFace>().GroupBy( f => f.Component ) )
		{
			var tx = group.Key.WorldTransform;
			var go = new GameObject( true, group.Key.GameObject.Name );
			go.MakeNameUnique();
			go.WorldTransform = tx;
			var mc = go.Components.Create<MeshComponent>( false );
			mc.Mesh = BuildMesh( group.Key, [.. group.Select( f => f.Handle )] );
			mc.Enabled = true;
			_selectedObjects.Add( new( tx, go ) );
		}
	}

	public override void OnDisabled()
	{
		Reset();

		_selectedObjects.Clear();
	}

	void Apply()
	{
		if ( !_plane.HasValue ) return;

		Reset();

		Selection.Clear();

		foreach ( var (_, go) in _selectedObjects )
		{
			Selection.Add( go );
		}

		_selectedObjects.Clear();

		EditorToolManager.SetSubTool( tool );
	}

	void Cancel()
	{
		using var scope = SceneEditorSession.Scope();

		foreach ( var (_, go) in _selectedObjects )
		{
			if ( go.IsValid() ) go.Destroy();
		}

		Reset();

		_selectedObjects.Clear();

		EditorToolManager.SetSubTool( tool );
	}

	static PolygonMesh BuildMesh( MeshComponent mc, HashSet<HalfEdgeMesh.FaceHandle> faces = null )
	{
		var mesh = new PolygonMesh();
		mesh.SetSmoothingAngle( 40 );
		mesh.Transform = mc.Mesh.Transform;
		mesh.MergeMesh( mc.Mesh, Transform.Zero, out _, out _, out var newFaces );

		if ( faces is not null )
		{
			mesh.RemoveFaces( [.. newFaces.Where( kv => !faces.Contains( kv.Key ) ).Select( kv => kv.Value )] );
		}

		mesh.FlipAllFaces();
		mesh.Scale( new Vector3( 1, -1, 1 ) );

		return mesh;
	}

	static Transform MirrorTransform( Transform world, Plane plane )
	{
		var n = plane.Normal.Normal;

		Vector3 Reflect( Vector3 v ) => v - 2.0f * Vector3.Dot( n, v ) * n;

		var d = plane.GetDistance( world.Position );
		var pos = world.Position - 2.0f * d * n;

		var forward = Reflect( world.Rotation.Forward );
		var up = Reflect( world.Rotation.Up );

		return new Transform( pos, Rotation.LookAt( forward, up ) );
	}

	static Vector3 SnapToPlaneGrid( Vector3 point, Vector3 planeNormal )
	{
		var rotation = Rotation.LookAt( planeNormal );
		var local = point * rotation.Inverse;
		local = Gizmo.Snap( local, new Vector3( 0, 1, 1 ) );
		return local * rotation;
	}

	void UpdateMirrorPlane()
	{
		if ( !_hitPlane.HasValue ) return;

		var up = _hitPlane.Value.Normal;
		var right = _point2 - _point1;

		if ( right.LengthSquared.AlmostEqual( 0.0f ) )
		{
			_plane = default;
			return;
		}

		var forward = up.Cross( right ).Normal;
		_plane = new Plane( forward, _point1.Dot( forward ) );
	}

	public override void OnUpdate()
	{
		if ( _selectedObjects.Count == 0 ) return;

		Gizmo.Draw.IgnoreDepth = true;

		if ( _plane.HasValue )
		{
			foreach ( var (tx, copy) in _selectedObjects )
			{
				if ( !copy.IsValid() ) continue;

				copy.WorldTransform = MirrorTransform( tx, _plane.Value );
			}
		}

		if ( _hitPlane.HasValue )
		{
			var normal = _hitPlane.Value.Normal;

			Gizmo.Draw.Color = Color.White;

			using ( Gizmo.Scope( "mirror_p1", _point1 ) )
			{
				Gizmo.Hitbox.Sprite( 0, 12, false );

				if ( Gizmo.WasLeftMousePressed && Gizmo.IsHovered )
					_dragStartP1 = _point1;

				if ( Gizmo.Pressed.This )
				{
					var drag = Gizmo.GetMouseDrag( 0, normal );
					_point1 = SnapToPlaneGrid( _dragStartP1 - drag, normal );
					UpdateMirrorPlane();
				}

				Gizmo.Draw.Sprite( 0, Gizmo.IsHovered ? 12 : 10, null, false );
			}

			using ( Gizmo.Scope( "mirror_p2", _point2 ) )
			{
				Gizmo.Hitbox.Sprite( 0, 12, false );

				if ( Gizmo.WasLeftMousePressed && Gizmo.IsHovered )
					_dragStartP2 = _point2;

				if ( Gizmo.Pressed.This )
				{
					var drag = Gizmo.GetMouseDrag( 0, normal );
					_point2 = SnapToPlaneGrid( _dragStartP2 - drag, normal );
					UpdateMirrorPlane();
				}

				Gizmo.Draw.Sprite( 0, Gizmo.IsHovered ? 12 : 10, null, false );
			}

			using ( Gizmo.Scope( "mirror_line" ) )
			{
				using var _ = Gizmo.Hitbox.LineScope();

				if ( Gizmo.WasLeftMousePressed && Gizmo.IsHovered )
				{
					_dragStartP1 = _point1;
					_dragStartP2 = _point2;
				}

				if ( Gizmo.Pressed.This )
				{
					var drag = Gizmo.GetMouseDrag( _dragStartP1, normal );
					_point1 = SnapToPlaneGrid( _dragStartP1 - drag, normal );
					_point2 = SnapToPlaneGrid( _dragStartP2 - drag, normal );
					UpdateMirrorPlane();
				}

				Gizmo.Draw.LineThickness = Gizmo.IsHovered ? 5 : 4;
				Gizmo.Draw.Line( _point1, _point2 );
			}
		}

		var tr = TracePlane();
		if ( !tr.Hit ) return;

		var point = SnapToPlaneGrid( tr.HitPosition, tr.Normal );

		if ( !Gizmo.HasHovered )
		{
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.Sprite( point, 10, null, false );
		}

		if ( Gizmo.WasLeftMousePressed && !Gizmo.HasHovered )
		{
			_hitPlane = new Plane( point, tr.Normal );
			_point1 = point;
			_point2 = point;
			_plane = default;
		}
		else if ( Gizmo.IsLeftMouseDown && !Gizmo.HasHovered && !point.AlmostEqual( _point1 ) )
		{
			_point2 = point;
			UpdateMirrorPlane();
		}
	}

	SceneTraceResult TracePlane()
	{
		if ( Gizmo.Pressed.Any ) return default;

		SceneTraceResult tr = default;

		if ( _hitPlane.HasValue )
		{
			var plane = _hitPlane.Value;
			if ( plane.TryTrace( Gizmo.CurrentRay, out var hit, true ) )
			{
				tr.Hit = true;
				tr.Normal = plane.Normal;
				tr.HitPosition = hit;
			}
		}
		else
		{
			tr = MeshTrace.Run();

			if ( !tr.Hit )
			{
				var plane = new Plane( Vector3.Up, 0 );
				if ( plane.TryTrace( Gizmo.CurrentRay, out var hit ) )
				{
					tr.Hit = true;
					tr.Normal = plane.Normal;
					tr.HitPosition = hit;
				}
			}
		}

		return tr;
	}
}
