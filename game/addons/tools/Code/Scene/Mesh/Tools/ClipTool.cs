
namespace Editor.MeshEditor;

[Alias( "tools.clip-tool" )]
public partial class ClipTool : EditorTool
{
	Plane? _hitPlane;
	Plane? _plane;
	Vector3 _point1;
	Vector3 _point2;

	Dictionary<MeshComponent, PolygonMesh> _meshes = [];

	public bool CapNewSurfaces
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			ApplyClip();
		}
	}

	public enum ClipKeepMode
	{
		Front, Back, Both
	}

	public ClipKeepMode KeepMode
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			ApplyClip();
		}
	}

	void Reset()
	{
		_plane = default;
		_hitPlane = default;
		_point1 = default;
		_point2 = default;
	}

	void CacheSelectedMeshes()
	{
		_meshes = Selection.OfType<GameObject>()
			.Select( x => x.GetComponent<MeshComponent>() )
			.Where( x => x.IsValid() )
			.ToDictionary( x => x, x => x.Mesh );

		Reset();
	}

	public override void OnEnabled()
	{
		Reset();

		CacheSelectedMeshes();
	}

	public override void OnSelectionChanged()
	{
		CacheSelectedMeshes();
	}

	public override void OnDisabled()
	{
		Cancel();
	}

	public override void OnUpdate()
	{
		var tr = TracePlane();
		UpdatePoints( tr );

		foreach ( var mesh in _meshes )
		{
			DrawMesh( mesh.Key, mesh.Value );
		}

		DrawNewEdges();

		if ( _hitPlane.HasValue )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.LineThickness = 4;
			Gizmo.Draw.Sprite( _point1, 10, null, false );
			Gizmo.Draw.Sprite( _point2, 10, null, false );
			Gizmo.Draw.Line( _point1, _point2 );
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
			if ( tr.Hit == false )
			{
				var plane = new Plane( Vector3.Up, 0.0f );
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

	void UpdatePoints( SceneTraceResult tr )
	{
		if ( tr.Hit == false ) return;

		var rotation = Rotation.LookAt( tr.Normal );
		var point = tr.HitPosition * rotation.Inverse;
		point = Gizmo.Snap( point, new Vector3( 0, 1, 1 ) );
		point *= rotation;

		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.LineThickness = 4;
			Gizmo.Draw.Sprite( point, 10, null, false );
		}

		if ( Gizmo.WasLeftMousePressed )
		{
			_hitPlane = new Plane( point, tr.Normal );
			_point1 = point;
			_point2 = point;
			_plane = default;
		}
		else if ( Gizmo.IsLeftMouseDown && point.AlmostEqual( _point1 ) == false )
		{
			_point2 = point;

			var up = tr.Normal;
			var right = _point2 - _point1;
			var forward = up.Cross( right ).Normal;
			_plane = new Plane( forward, _point1.Dot( forward ) );

			ApplyClip();
		}
	}

	void Apply()
	{
		if ( _plane.HasValue == false ) return;
		if ( _meshes.Count == 0 ) return;

		var meshes = _meshes.Keys.Where( x => x.IsValid() );
		if ( meshes.Any() == false ) return;

		_newEdges.Clear();
		_newFaces.Clear();

		using var scope = SceneEditorSession.Scope();

		foreach ( var (component, mesh) in _meshes )
		{
			component.Mesh = mesh;
		}

		using ( SceneEditorSession.Active.UndoScope( "Clip" )
			.WithComponentChanges( meshes )
			.Push() )
		{
			foreach ( var mesh in meshes )
			{
				ApplyClip( mesh );
			}

			foreach ( var key in _meshes.Keys )
			{
				_meshes[key] = key.Mesh;
			}
		}

		Reset();
	}

	void ApplyClip()
	{
		if ( _plane.HasValue == false ) return;

		_newEdges.Clear();
		_newFaces.Clear();

		var meshes = _meshes.Keys.Where( x => x.IsValid() );
		foreach ( var kv in _meshes )
		{
			if ( kv.Key.IsValid() == false ) continue;

			var mesh = new PolygonMesh();
			mesh.Transform = kv.Value.Transform;
			mesh.MergeMesh( kv.Value, Transform.Zero, out _, out _, out _ );
			kv.Key.Mesh = mesh;

			ApplyClip( kv.Key );
		}
	}

	readonly List<MeshEdge> _newEdges = [];
	readonly List<MeshFace> _newFaces = [];

	void ApplyClip( MeshComponent mesh )
	{
		var plane = _plane.Value;

		if ( KeepMode == ClipKeepMode.Front )
		{
			plane = new Plane( -plane.Normal, -plane.Distance );
		}

		var transform = mesh.WorldTransform;
		plane = new Plane( transform.Rotation.Inverse * plane.Normal, plane.Distance - Vector3.Dot( plane.Normal, transform.Position ) );

		var newEdges = new List<HalfEdgeMesh.HalfEdgeHandle>();
		var newFaces = new List<HalfEdgeMesh.FaceHandle>();
		mesh.Mesh.ClipFacesByPlaneAndCap( [.. mesh.Mesh.FaceHandles], plane, KeepMode != ClipKeepMode.Both, CapNewSurfaces, newEdges, newFaces );
		mesh.Mesh.ComputeFaceTextureCoordinatesFromParameters();
		mesh.RebuildMesh();

		foreach ( var edge in newEdges )
		{
			_newEdges.Add( new MeshEdge( mesh, edge ) );
		}

		foreach ( var face in newFaces )
		{
			_newFaces.Add( new MeshFace( mesh, face ) );
		}
	}

	void Cancel()
	{
		foreach ( var mesh in _meshes )
		{
			if ( mesh.Key.Mesh == mesh.Value ) continue;

			mesh.Value.ComputeFaceTextureCoordinatesFromParameters();
			mesh.Key.Mesh = mesh.Value;
		}

		Reset();
	}

	void DrawNewEdges()
	{
		if ( _newEdges.Count == 0 ) return;

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = Color.Orange;
		Gizmo.Draw.LineThickness = 2;

		foreach ( var edge in _newEdges )
		{
			edge.Component.Mesh.GetEdgeVertexPositions( edge.Handle, edge.Component.WorldTransform, out var start, out var end );
			Gizmo.Draw.Line( start, end );
		}
	}

	static void DrawMesh( MeshComponent component, PolygonMesh mesh )
	{
		if ( component.IsValid() == false ) return;

		using ( Gizmo.ObjectScope( component.GameObject, component.WorldTransform ) )
		{
			using ( Gizmo.Scope( "Edges" ) )
			{
				var edgeColor = new Color( 0.3137f, 0.7843f, 1.0f, 1f );

				Gizmo.Draw.LineThickness = 1;
				Gizmo.Draw.IgnoreDepth = true;
				Gizmo.Draw.Color = edgeColor.Darken( 0.3f ).WithAlpha( 0.2f );

				foreach ( var v in mesh.GetEdges() )
				{
					Gizmo.Draw.Line( v );
				}

				Gizmo.Draw.Color = edgeColor;
				Gizmo.Draw.IgnoreDepth = false;
				Gizmo.Draw.LineThickness = 2;

				foreach ( var v in mesh.GetEdges() )
				{
					Gizmo.Draw.Line( v );
				}
			}
		}
	}
}
