namespace Editor.MeshEditor;

[Alias( "tools.mirror-tool" )]
public partial class MirrorTool( string tool ) : EditorTool
{
	Plane? _hitPlane;
	Plane? _plane;
	Vector3 _point1;
	Vector3 _point2;

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

			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.LineThickness = 4;
			Gizmo.Draw.Sprite( _point1, 10, null, false );
			Gizmo.Draw.Sprite( _point2, 10, null, false );
			Gizmo.Draw.Line( _point1, _point2 );
		}

		var tr = TracePlane();
		if ( !tr.Hit ) return;

		var rot = Rotation.LookAt( tr.Normal );
		var point = Gizmo.Snap( tr.HitPosition * rot.Inverse, new Vector3( 0, 1, 1 ) ) * rot;

		Gizmo.Draw.Sprite( point, 10, null, false );

		if ( Gizmo.WasLeftMousePressed )
		{
			_hitPlane = new Plane( point, tr.Normal );
			_plane = _hitPlane;
			_point1 = point;
			_point2 = point;
		}
		else if ( Gizmo.IsLeftMouseDown )
		{
			_point2 = point;

			if ( _point1.AlmostEqual( _point2 ) )
			{
				_plane = default;
			}
			else
			{
				var forward = tr.Normal.Cross( _point2 - _point1 ).Normal;
				_plane = new Plane( forward, _point1.Dot( forward ) );
			}
		}
		else
		{
			_hitPlane = default;
		}
	}

	SceneTraceResult TracePlane()
	{
		if ( Gizmo.Pressed.Any ) return default;

		if ( _hitPlane.HasValue &&
			 _hitPlane.Value.TryTrace( Gizmo.CurrentRay, out var hit, true ) )
		{
			return new SceneTraceResult
			{
				Hit = true,
				Normal = _hitPlane.Value.Normal,
				HitPosition = hit
			};
		}

		var tr = MeshTrace.Run();
		if ( tr.Hit ) return tr;

		var ground = new Plane( Vector3.Up, 0 );
		if ( ground.TryTrace( Gizmo.CurrentRay, out var g ) )
		{
			tr.Hit = true;
			tr.Normal = ground.Normal;
			tr.HitPosition = g;
		}

		return tr;
	}
}
