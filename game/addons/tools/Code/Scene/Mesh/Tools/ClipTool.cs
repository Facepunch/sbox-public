namespace Editor.MeshEditor;

[Alias( "tools.clip-tool" )]
public partial class ClipTool : EditorTool
{
	Plane? _hitPlane;
	Plane? _plane;
	Vector3 _point1;
	Vector3 _point2;
	bool _faceSelection;
	bool _applied;

	readonly List<MeshEdge> _newEdges = [];

	readonly Dictionary<MeshComponent, (PolygonMesh Mesh, HashSet<HalfEdgeMesh.FaceHandle> Faces)> _targets = [];

	public bool CapNewSurfaces
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			EditorCookie.Set( "ClipTool.CapNewSurfaces", value );

			ApplyClipPreview();
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


			EditorCookie.Set( "ClipTool.KeepMode", value );

			ApplyClipPreview();
		}
	}

	void Reset()
	{
		_plane = default;
		_hitPlane = default;
		_point1 = default;
		_point2 = default;

		_newEdges.Clear();
	}

	void CacheSelectedMeshes()
	{
		_targets.Clear();

		_faceSelection = false;

		foreach ( var go in Selection.OfType<GameObject>() )
		{
			var mc = go.GetComponent<MeshComponent>();
			if ( mc.IsValid() ) _targets[mc] = (mc.Mesh, null);
		}

		if ( _targets.Count == 0 )
		{
			_faceSelection = true;

			foreach ( var group in Selection.OfType<MeshFace>().GroupBy( f => f.Component ) )
			{
				var selectedFaces = new HashSet<HalfEdgeMesh.FaceHandle>( group.Select( f => f.Handle ) );
				var targetFaces = selectedFaces.Count == group.Key.Mesh.FaceHandles.Count() ? null : selectedFaces;
				_targets[group.Key] = (group.Key.Mesh, targetFaces);
			}
		}

		Reset();
	}

	public override void OnEnabled()
	{
		KeepMode = EditorCookie.Get( "ClipTool.KeepMode", ClipKeepMode.Front );
		CapNewSurfaces = EditorCookie.Get( "ClipTool.CapNewSurfaces", false );

		Reset();
		CacheSelectedMeshes();
	}

	public override void OnSelectionChanged()
	{
		if ( _applied ) return;

		CacheSelectedMeshes();
	}

	public override void OnDisabled()
	{
		Cancel();
	}

	static Vector3 SnapToPlaneGrid( Vector3 point, Vector3 planeNormal )
	{
		var rotation = Rotation.LookAt( planeNormal );
		var local = point * rotation.Inverse;
		local = Gizmo.Snap( local, new Vector3( 0, 1, 1 ) );
		return local * rotation;
	}

	Vector3 _dragStartP1;
	Vector3 _dragStartP2;

	public override void OnUpdate()
	{
		var tr = TracePlane();
		UpdatePoints( tr );

		foreach ( var (component, data) in _targets )
			DrawMesh( component, data.Mesh );

		DrawNewEdges();

		if ( !_hitPlane.HasValue )
			return;

		var normal = _hitPlane.Value.Normal;

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.Color = Color.White;

		using ( Gizmo.Scope( "clip_p1", _point1 ) )
		{
			Gizmo.Hitbox.Sprite( 0, 12, false );

			if ( Gizmo.WasLeftMousePressed && Gizmo.IsHovered )
				_dragStartP1 = _point1;

			if ( Gizmo.Pressed.This )
			{
				var drag = Gizmo.GetMouseDrag( 0, normal );
				_point1 = SnapToPlaneGrid( _dragStartP1 - drag, normal );
				UpdateClipPlane();
			}

			Gizmo.Draw.Sprite( 0, Gizmo.IsHovered ? 12 : 10, null, false );
		}

		using ( Gizmo.Scope( "clip_p2", _point2 ) )
		{
			Gizmo.Hitbox.Sprite( 0, 12, false );

			if ( Gizmo.WasLeftMousePressed && Gizmo.IsHovered )
				_dragStartP2 = _point2;

			if ( Gizmo.Pressed.This )
			{
				var drag = Gizmo.GetMouseDrag( 0, normal );
				_point2 = SnapToPlaneGrid( _dragStartP2 - drag, normal );
				UpdateClipPlane();
			}

			Gizmo.Draw.Sprite( 0, Gizmo.IsHovered ? 12 : 10, null, false );
		}

		using ( Gizmo.Scope( "clip_line" ) )
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

				UpdateClipPlane();
			}

			Gizmo.Draw.LineThickness = Gizmo.IsHovered ? 5 : 4;
			Gizmo.Draw.Line( _point1, _point2 );
		}
	}

	void UpdateClipPlane()
	{
		if ( !_hitPlane.HasValue )
			return;

		var up = _hitPlane.Value.Normal;
		var right = _point2 - _point1;

		if ( right.LengthSquared.AlmostEqual( 0.0f ) )
			return;

		var forward = up.Cross( right ).Normal;

		_plane = new Plane( forward, _point1.Dot( forward ) );

		ApplyClipPreview();
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

	void UpdatePoints( SceneTraceResult tr )
	{
		if ( !tr.Hit ) return;

		var point = SnapToPlaneGrid( tr.HitPosition, tr.Normal );

		if ( !Gizmo.HasHovered )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.Sprite( point, 10, null, false );
		}

		if ( Gizmo.WasLeftMousePressed )
		{
			_hitPlane = new Plane( point, tr.Normal );
			_point1 = point;
			_point2 = point;
			_plane = default;
		}
		else if ( Gizmo.IsLeftMouseDown && !point.AlmostEqual( _point1 ) )
		{
			_point2 = point;

			var up = tr.Normal;
			var right = _point2 - _point1;
			var forward = up.Cross( right ).Normal;

			_plane = new Plane( forward, _point1.Dot( forward ) );

			ApplyClipPreview();
		}
	}

	bool CanApply => _plane.HasValue && _targets.Count > 0;

	void Apply()
	{
		Apply( true );
	}

	void Apply( bool closeTool = true )
	{
		if ( !CanApply ) return;

		_applied = true;

		var components = _targets.Keys.Where( x => x.IsValid() ).ToArray();
		var selectAllFacesAfterApply = _faceSelection && _targets.Values.Any( x => x.Faces is null );

		_newEdges.Clear();

		using var scope = SceneEditorSession.Scope();

		foreach ( var (component, data) in _targets )
			component.Mesh = data.Mesh;

		using ( SceneEditorSession.Active.UndoScope( "Clip" )
			.WithComponentChanges( components )
			.WithGameObjectCreations()
			.Push() )
		{
			if ( closeTool || _faceSelection )
				Selection.Clear();

			foreach ( var (component, data) in _targets.ToArray() )
			{
				if ( !component.IsValid() ) continue;

				if ( _faceSelection == false && KeepMode == ClipKeepMode.Both )
				{
					var newMesh = ApplyClipBoth( component, data.Faces );
					if ( closeTool )
						Selection.Add( newMesh.GameObject );
				}
				else
				{
					ApplyClip( component, _plane.Value, KeepMode, data.Faces );
				}

				if ( closeTool && _faceSelection == false )
				{
					Selection.Add( component.GameObject );
				}
			}

			if ( _faceSelection )
			{
				if ( selectAllFacesAfterApply )
				{
					foreach ( var (component, data) in _targets )
					{
						if ( !component.IsValid() || data.Faces is not null )
							continue;

						foreach ( var face in component.Mesh.FaceHandles )
							Selection.Add( new MeshFace( component, face ) );
					}
				}
				else
				{
					foreach ( var edge in _newEdges )
					{
						if ( !edge.IsValid() )
							continue;

						var mesh = edge.Component.Mesh;
						mesh.GetFacesConnectedToEdge( edge.Handle, out var faceA, out var faceB );

						if ( faceA.IsValid )
							Selection.Add( new MeshFace( edge.Component, faceA ) );

						if ( faceB.IsValid )
							Selection.Add( new MeshFace( edge.Component, faceB ) );
					}
				}
			}

			foreach ( var key in _targets.Keys )
				_targets[key] = (key.Mesh, _targets[key].Faces);
		}

		Reset();
		_applied = false;

		if ( closeTool )
		{
			EditorToolManager.SetSubTool( _faceSelection ? nameof( FaceTool ) : nameof( ObjectSelection ) );
		}
		else if ( _faceSelection )
		{
			CacheSelectedMeshes();
		}
	}

	void Cancel()
	{
		foreach ( var (component, data) in _targets )
		{
			if ( component.Mesh == data.Mesh ) continue;

			data.Mesh.ComputeFaceTextureCoordinatesFromParameters();
			component.Mesh = data.Mesh;
		}

		Reset();
	}

	MeshComponent ApplyClipBoth( MeshComponent mesh, HashSet<HalfEdgeMesh.FaceHandle> faces )
	{
		var newMesh = new PolygonMesh();
		newMesh.Transform = mesh.Mesh.Transform;
		newMesh.MergeMesh( mesh.Mesh, Transform.Zero, out _, out _, out var newFaces );

		var go = new GameObject( true, mesh.GameObject.Name );
		go.MakeNameUnique();
		go.WorldTransform = mesh.WorldTransform;

		var mc = go.Components.Create<MeshComponent>( false );
		mc.Mesh = newMesh;

		HashSet<HalfEdgeMesh.FaceHandle> remappedFaces = null;

		if ( faces is not null )
		{
			remappedFaces = [.. faces.Where( newFaces.ContainsKey ).Select( f => newFaces[f] )];
		}

		var plane = _plane.Value;

		ApplyClip( mesh, plane, ClipKeepMode.Front, faces );
		ApplyClip( mc, plane, ClipKeepMode.Back, remappedFaces );

		mc.Enabled = true;

		return mc;
	}

	void ApplyClipPreview()
	{
		if ( !_plane.HasValue ) return;

		_newEdges.Clear();

		foreach ( var (component, data) in _targets )
		{
			if ( !component.IsValid() ) continue;

			var mesh = new PolygonMesh();
			mesh.Transform = data.Mesh.Transform;
			mesh.MergeMesh( data.Mesh, Transform.Zero, out _, out _, out var newFaces );
			component.Mesh = mesh;

			HashSet<HalfEdgeMesh.FaceHandle> faces = null;

			if ( data.Faces is not null )
			{
				faces = [.. data.Faces.Where( newFaces.ContainsKey ).Select( f => newFaces[f] )];
			}

			ApplyClip( component, _plane.Value, KeepMode, faces );
		}
	}

	void ApplyClip( MeshComponent mesh, Plane plane, ClipKeepMode keepMode, HashSet<HalfEdgeMesh.FaceHandle> faces )
	{
		if ( keepMode == ClipKeepMode.Front )
			plane = new Plane( -plane.Normal, -plane.Distance );

		var transform = mesh.WorldTransform;
		plane = new Plane( transform.Rotation.Inverse * plane.Normal, plane.Distance - Vector3.Dot( plane.Normal, transform.Position ) );

		var faceSet = faces ?? mesh.Mesh.FaceHandles;
		var newEdges = new List<HalfEdgeMesh.HalfEdgeHandle>();

		mesh.Mesh.ClipFacesByPlaneAndCap( [.. faceSet], plane, keepMode != ClipKeepMode.Both, CapNewSurfaces, newEdges );
		mesh.Mesh.ComputeFaceTextureCoordinatesFromParameters();
		mesh.RebuildMesh();

		foreach ( var edge in newEdges )
			_newEdges.Add( new MeshEdge( mesh, edge ) );
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
		if ( !component.IsValid() ) return;

		using ( Gizmo.ObjectScope( component.GameObject, component.WorldTransform ) )
		using ( Gizmo.Scope( "Edges" ) )
		{
			var edgeColor = new Color( 0.3137f, 0.7843f, 1f, 1f );

			Gizmo.Draw.LineThickness = 1;
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = edgeColor.Darken( 0.3f ).WithAlpha( 0.2f );

			foreach ( var v in mesh.GetEdges() )
				Gizmo.Draw.Line( v );

			Gizmo.Draw.Color = edgeColor;
			Gizmo.Draw.IgnoreDepth = false;
			Gizmo.Draw.LineThickness = 2;

			foreach ( var v in mesh.GetEdges() )
				Gizmo.Draw.Line( v );
		}
	}

	void CycleMode()
	{
		KeepMode = KeepMode switch
		{
			ClipKeepMode.Front => ClipKeepMode.Back,
			ClipKeepMode.Back => ClipKeepMode.Both,
			_ => ClipKeepMode.Front
		};
	}
}
