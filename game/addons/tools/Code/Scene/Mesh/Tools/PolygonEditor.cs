using System.Runtime.InteropServices;

namespace Editor.MeshEditor;

/// <summary>
/// Draw a polygon mesh.
/// </summary>
[Title( "Polygon" ), Icon( "pentagon" )]
public sealed class PolygonEditor( PrimitiveTool tool ) : PrimitiveEditor( tool )
{
	Plane _plane;
	readonly List<Vector3> _points = [];
	HalfEdgeMesh.FaceHandle _face;
	Model _previewModel;
	bool _valid;
	Material _activeMaterial = tool.ActiveMaterial;

	public override bool CanBuild => _points.Count >= 3 && _valid;
	public override bool InProgress => _points.Count > 0;

	public override PolygonMesh Build()
	{
		if ( !CanBuild ) return default;

		var mesh = new PolygonMesh();
		var count = _points.Count;

		PlaneEquation( _points, out var faceNormal );

		var hasHeight = !Height.AlmostEqual( 0.0f );
		var flip = faceNormal.Dot( _plane.Normal ) < 0.0f;
		if ( hasHeight && !Hollow ) flip = !flip;

		var basePoints = new Vector3[count];
		if ( flip ) for ( var i = 0; i < count; i++ ) basePoints[i] = _points[count - 1 - i];
		else _points.CopyTo( basePoints );

		var bottomRing = mesh.AddVertices( basePoints );
		var bottomFace = mesh.AddFace( bottomRing );
		if ( !bottomFace.IsValid ) return default;

		mesh.SetFaceMaterial( bottomFace, Tool.ActiveMaterial );

		if ( !hasHeight )
		{
			mesh.SetSmoothingAngle( 40.0f );
			mesh.TextureAlignToGrid( Transform.Zero );
			_face = bottomFace;
			return mesh;
		}

		var offset = Vector3.Up * Height;
		var topPoints = new Vector3[count];
		for ( var i = 0; i < count; i++ ) topPoints[i] = basePoints[i] + offset;

		var topRing = mesh.AddVertices( topPoints );
		var topCap = new HalfEdgeMesh.VertexHandle[count];
		for ( var i = 0; i < count; i++ ) topCap[i] = topRing[count - 1 - i];

		var topFace = mesh.AddFace( topCap );
		mesh.SetFaceMaterial( topFace, Tool.ActiveMaterial );

		for ( var i = 0; i < count; i++ )
		{
			mesh.SetFaceMaterial( mesh.AddFace( [bottomRing[i], topRing[i], topRing[(i + 1) % count], bottomRing[(i + 1) % count]] ),
				Tool.ActiveMaterial );
		}

		mesh.SetSmoothingAngle( 40.0f );
		mesh.TextureAlignToGrid( Transform.Zero );

		_face = topFace;
		return mesh;
	}


	void BuildPreview()
	{
		var mesh = Build();
		_previewModel = mesh?.Rebuild();
	}

	public override void OnCreated( MeshComponent component )
	{
		_points.Clear();
		_valid = false;

		var selection = SceneEditorSession.Active.Selection;
		selection.Clear();
		selection.Add( component.GameObject );
		selection.Add( new MeshFace( component, _face ) );

		EditorToolManager.SetSubTool( nameof( FaceTool ) );
	}

	public override void OnUpdate( SceneTrace trace )
	{
		if ( Application.IsKeyDown( KeyCode.Escape ) )
		{
			Cancel();
		}

		if ( _activeMaterial != Tool.ActiveMaterial )
		{
			BuildPreview();
			_activeMaterial = Tool.ActiveMaterial;
		}

		if ( !Gizmo.Pressed.Any )
		{
			if ( _points.Count > 0 )
			{
				DrawingStage();
			}
			else
			{
				StartStage( trace );
			}
		}

		DrawGizmos();
	}

	public override void OnCancel()
	{
		Cancel();
	}

	void Cancel()
	{
		_points.Clear();
		_valid = false;
	}

	void DrawGizmos()
	{
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 2;

		var valid = _previewModel.IsValid() && !_previewModel.IsError;

		if ( valid )
		{
			Gizmo.Draw.Model( _previewModel );
		}

		if ( _points.Count < 3 ) valid = true;

		Gizmo.Draw.Color = valid ? Color.Yellow : Color.Red;

		for ( int i = 0; i < _points.Count; i++ )
		{
			var a = _points[i];
			var b = _points[(i + 1) % _points.Count];

			Gizmo.Draw.Line( a, b );
		}

		for ( int i = 0; i < _points.Count; i++ )
		{
			var point = _points[i];
			var size = 3.0f * Gizmo.Camera.Position.Distance( point ) / 1000.0f;

			using ( Gizmo.Scope( $"point {i}" ) )
			{
				Gizmo.Hitbox.DepthBias = 0.01f;
				Gizmo.Hitbox.Sphere( new Sphere( point, size * 2 ) );

				if ( Gizmo.Pressed.This )
				{
					if ( _plane.TryTrace( Gizmo.CurrentRay, out var newPoint, true ) )
					{
						newPoint = GridSnap( newPoint, _plane.Normal );
						if ( !point.AlmostEqual( newPoint ) )
						{
							_points[i] = newPoint;
							point = newPoint;

							OnPointsChanged();
						}
					}
				}

				Gizmo.Draw.Color = Gizmo.IsHovered ? Color.Yellow : Color.White;
				Gizmo.Draw.SolidSphere( point, Gizmo.IsHovered ? size * 2 : size );
			}
		}
	}

	void AddPoint( Vector3 point )
	{
		_points.Add( point );
		OnPointsChanged();
	}

	void RemovePoint()
	{
		if ( _points.Count == 0 ) return;
		_points.RemoveAt( _points.Count - 1 );
		OnPointsChanged();
	}

	void OnPointsChanged()
	{
		_valid = _points.Count < 3 || Mesh.TriangulatePolygon( CollectionsMarshal.AsSpan( _points ) ).Length >= 3;
		BuildPreview();
	}

	void DrawingStage()
	{
		if ( !_plane.TryTrace( Gizmo.CurrentRay, out var point, true ) ) return;

		point = GridSnap( point, _plane.Normal );

		if ( Gizmo.WasLeftMousePressed )
		{
			AddPoint( point );
		}

		if ( !Gizmo.HasHovered )
		{
			var size = 3.0f * Gizmo.Camera.Position.Distance( point ) / 1000.0f;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.SolidSphere( point, size );
			Gizmo.Draw.Line( _points.Last(), point );
		}
	}

	void StartStage( SceneTrace trace )
	{
		_previewModel = null;

		var tr = trace.Run();

		if ( !tr.Hit )
		{
			var plane = new Plane( Vector3.Up, 0.0f );
			if ( plane.TryTrace( Gizmo.CurrentRay, out var point, true ) )
			{
				tr.Hit = true;
				tr.Normal = plane.Normal;
				tr.EndPosition = point;
			}
		}

		if ( !tr.Hit ) return;

		tr.EndPosition = GridSnap( tr.EndPosition, tr.Normal );

		if ( Gizmo.WasLeftMousePressed )
		{
			_plane = new Plane( tr.EndPosition, tr.Normal );
			_points.Add( tr.EndPosition );
		}

		if ( !Gizmo.HasHovered )
		{
			var size = 3.0f * Gizmo.Camera.Position.Distance( tr.EndPosition ) / 1000.0f;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.SolidSphere( tr.EndPosition, size );
		}
	}

	public override Widget CreateWidget()
	{
		return new PolygonEditorWidget( this );
	}

	[WideMode]
	public float Height
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			BuildPreview();
		}
	}

	[WideMode]
	public bool Hollow
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			BuildPreview();
		}
	}

	class PolygonEditorWidget : ToolSidebarWidget
	{
		readonly PolygonEditor _editor;

		public PolygonEditorWidget( PolygonEditor editor )
		{
			_editor = editor;

			Layout.Margin = 0;

			{
				var group = AddGroup( "Polygon Properties" );
				var row = group.AddRow();
				var so = editor.GetSerialized();
				row.Add( ControlSheetRow.Create( so.GetProperty( nameof( editor.Height ) ) ) );
				row.Add( ControlSheetRow.Create( so.GetProperty( nameof( editor.Hollow ) ) ) ).FixedWidth = 60;
			}

			Layout.AddStretchCell();
		}

		[Shortcut( "editor.delete", "DEL", typeof( SceneDock ) )]
		public void DeletePoint() => _editor.RemovePoint();
	}

	static Vector3 GridSnap( Vector3 point, Vector3 normal )
	{
		var basis = Rotation.LookAt( normal );
		return Gizmo.Snap( point * basis.Inverse, new Vector3( 0, 1, 1 ) ) * basis;
	}

	static void PlaneEquation( IReadOnlyList<Vector3> vertices, out Vector3 outNormal )
	{
		var normal = Vector3.Zero;
		var count = vertices.Count;

		for ( var i = 0; i < count; i++ )
		{
			var u = vertices[i];
			var v = vertices[(i + 1) % count];
			normal.x += (u.y - v.y) * (u.z + v.z);
			normal.y += (u.z - v.z) * (u.x + v.x);
			normal.z += (u.x - v.x) * (u.y + v.y);
		}

		outNormal = normal.Normal;
	}
}
