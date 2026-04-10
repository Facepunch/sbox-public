using System.Text.Json.Nodes;

namespace Editor.MeshEditor;

/// <summary>
/// Draw a cable path by placing points in the scene.
/// </summary>
[Title( "Cable" ), Icon( "cable" )]
public sealed class CableEditor( PrimitiveTool tool ) : PrimitiveEditor( tool )
{
	readonly List<Vector3> _points = [];
	Model _previewModel;
	Material _activeMaterial = tool.ActiveMaterial;
	const float DefaultTextureScale = 0.25f;
	const float DefaultTextureRepeatsCircumference = 1.0f;
	const float DefaultSlack = 0.0f;

	public override bool CanBuild => _points.Count >= 2;
	public override bool InProgress => _points.Count > 0;

	public override PolygonMesh Build()
	{
		if ( !CanBuild ) return null;

		var points = BuildPathPoints();
		if ( points.Count < 2 ) return null;

		var sides = Math.Max( 3, Subdivisions );
		var radius = Math.Max( 0.1f, Size );
		var material = Tool.ActiveMaterial;

		var mesh = new PolygonMesh();
		var rings = new HalfEdgeMesh.VertexHandle[points.Count][];
		var pathParam = BuildPathUvs( points, material );
		var circumferenceParam = BuildCircumferenceUvs( sides );

		var tangent = (points[1] - points[0]).Normal;
		var normal = BuildInitialNormal( tangent );

		for ( int i = 0; i < points.Count; i++ )
		{
			var point = points[i];
			tangent = BuildTangent( points, i );
			normal = BuildNormalFromPrevious( tangent, normal );
			var bitangent = tangent.Cross( normal ).Normal;

			var ring = new Vector3[sides];
			for ( int j = 0; j < sides; j++ )
			{
				var angle = (MathF.PI * 2.0f * j) / sides;
				var offset = normal * MathF.Cos( angle ) * radius + bitangent * MathF.Sin( angle ) * radius;
				ring[j] = point + offset;
			}

			rings[i] = mesh.AddVertices( ring );
		}

		for ( int i = 0; i < rings.Length - 1; i++ )
		{
			for ( int j = 0; j < sides; j++ )
			{
				var next = (j + 1) % sides;
				var nextCircumference = next == 0 ? GetCircumferenceUvAtRingEnd( circumferenceParam[0] ) : circumferenceParam[next];
				var face = mesh.AddFace( [rings[i][j], rings[i][next], rings[i + 1][next], rings[i + 1][j]] );
				mesh.SetFaceMaterial( face, material );

				var uv0 = BuildTextureUv( pathParam[i], circumferenceParam[j] );
				var uv1 = BuildTextureUv( pathParam[i], nextCircumference );
				var uv2 = BuildTextureUv( pathParam[i + 1], nextCircumference );
				var uv3 = BuildTextureUv( pathParam[i + 1], circumferenceParam[j] );
				mesh.SetFaceTextureCoords( face, [uv0, uv1, uv2, uv3] );
			}
		}

		if ( CapEnds )
		{
			var startCap = new HalfEdgeMesh.VertexHandle[sides];
			var endCap = new HalfEdgeMesh.VertexHandle[sides];
			for ( int i = 0; i < sides; i++ )
			{
				startCap[i] = rings[0][sides - 1 - i];
				endCap[i] = rings[^1][i];
			}

			var startFace = mesh.AddFace( startCap );
			var endFace = mesh.AddFace( endCap );
			mesh.SetFaceMaterial( startFace, material );
			mesh.SetFaceMaterial( endFace, material );

			var startUvs = new Vector2[sides];
			var endUvs = new Vector2[sides];
			for ( int i = 0; i < sides; i++ )
			{
				var u = (float)i / Math.Max( 1, sides - 1 );
				startUvs[i] = new Vector2( u, 0.0f );
				endUvs[i] = new Vector2( u, 1.0f );
			}

			mesh.SetFaceTextureCoords( startFace, startUvs );
			mesh.SetFaceTextureCoords( endFace, endUvs );
		}

		mesh.SetSmoothingAngle( 180.0f );
		return mesh;
	}

	public override void OnCreated( MeshComponent component )
	{
		var points = _points.ToArray();
		var cableType = TypeLibrary.GetType( "Sandbox.CableComponent" );
		var cableNodeType = TypeLibrary.GetType( "Sandbox.CableNodeComponent" );
		if ( cableType is not null )
		{
			if ( component.GameObject.Components.Create( cableType ) is Component cable )
			{
				var localPoints = points.Select( x => x - component.GameObject.WorldPosition ).ToArray();
				var serialized = cable.Serialize().AsObject();
				serialized["Material"] = JsonNode.Parse( Json.Serialize( Tool.ActiveMaterial ) );
				serialized["Size"] = JsonNode.Parse( Json.Serialize( Math.Max( 0.1f, Size ) ) );
				serialized["Subdivisions"] = JsonNode.Parse( Json.Serialize( Math.Clamp( Subdivisions, 3, 32 ) ) );
				serialized["PathDetail"] = JsonNode.Parse( Json.Serialize( Math.Clamp( PathDetail, 0, 16 ) ) );
				serialized["Slack"] = JsonNode.Parse( Json.Serialize( Math.Clamp( Slack, -512f, 512f ) ) );
				serialized["CapEnds"] = JsonNode.Parse( Json.Serialize( CapEnds ) );
				cable.DeserializeImmediately( serialized );

				for ( int i = 0; i < localPoints.Length; i++ )
				{
					var nodeObject = new GameObject( true, $"Cable Node {i + 1}" );
					nodeObject.SetParent( component.GameObject, false );
					nodeObject.LocalPosition = localPoints[i];
					if ( cableNodeType is not null && nodeObject.Components.Create( cableNodeType ) is Component nodeComponent )
					{
						var nodeSerialized = nodeComponent.Serialize().AsObject();
						nodeSerialized["RadiusScale"] = JsonNode.Parse( Json.Serialize( 1.0f ) );
						nodeSerialized["Roll"] = JsonNode.Parse( Json.Serialize( 0.0f ) );
						nodeComponent.DeserializeImmediately( nodeSerialized );
					}
				}
			}
		}

		PopUndo();
		_points.Clear();
		_previewModel = null;
		SceneEditorSession.Active.Selection.Set( component.GameObject );
	}

	public override void OnUpdate( SceneTrace trace )
	{
		if ( _activeMaterial != Tool.ActiveMaterial )
		{
			_activeMaterial = Tool.ActiveMaterial;
			BuildPreview();
		}

		if ( !Gizmo.Pressed.Any )
			UpdatePlacement( trace );

		DrawGizmos( trace );
	}

	public override void OnCancel() => Cancel();

	void Cancel()
	{
		PopUndo();
		_points.Clear();
		_previewModel = null;
	}

	void UpdatePlacement( SceneTrace trace )
	{
		if ( !TryGetPoint( trace, out var point, out var normal ) ) return;
		point = ApplyPlacementOffset( point, normal );

		if ( Gizmo.WasLeftMousePressed )
			AddPoint( point );
	}

	void AddPoint( Vector3 point )
	{
		var before = new List<Vector3>( _points );
		_points.Add( point );
		BuildPreview();
		PushUndo( "Add Cable Point", before );
	}

	void RemovePoint()
	{
		if ( _points.Count == 0 ) return;

		var before = new List<Vector3>( _points );
		_points.RemoveAt( _points.Count - 1 );
		BuildPreview();
		PushUndo( "Remove Cable Point", before );
	}

	void DrawGizmos( SceneTrace trace )
	{
		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 2;

		if ( _previewModel.IsValid() && !_previewModel.IsError )
		{
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.Model( _previewModel );
		}

		Gizmo.Draw.Color = Color.Yellow;
		for ( int i = 0; i < _points.Count - 1; i++ )
		{
			Gizmo.Draw.Line( _points[i], _points[i + 1] );
		}

		for ( int i = 0; i < _points.Count; i++ )
		{
			var point = _points[i];
			var size = 3.0f * Gizmo.Camera.Position.Distance( point ) / 1000.0f;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.SolidSphere( point, size );
		}

		if ( _points.Count > 0 && TryGetPoint( trace, out var preview, out var normal ) && !Gizmo.HasHovered )
		{
			preview = ApplyPlacementOffset( preview, normal );
			var size = 3.0f * Gizmo.Camera.Position.Distance( preview ) / 1000.0f;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.SolidSphere( preview, size );
			Gizmo.Draw.Line( _points[^1], preview );
		}
	}

	void BuildPreview()
	{
		var mesh = Build();
		_previewModel = mesh?.Rebuild();
	}

	void PushUndo( string name, List<Vector3> before )
	{
		var after = _points.ToArray();
		var editor = this;

		PushUndo( name,
			undo: () =>
			{
				editor._points.Clear();
				editor._points.AddRange( before );
				editor.BuildPreview();
			},
			redo: () =>
			{
				editor._points.Clear();
				editor._points.AddRange( after );
				editor.BuildPreview();
			}
		);
	}

	Vector3 ApplyPlacementOffset( Vector3 point, Vector3 normal )
	{
		if ( !OffsetFromSurface )
			return point;

		return point + normal.Normal * Math.Max( 0.1f, Size );
	}

	bool TryGetPoint( SceneTrace trace, out Vector3 point, out Vector3 normal )
	{
		var tr = trace.Run();
		if ( tr.Hit )
		{
			point = tr.EndPosition;
			normal = tr.Normal;
			return true;
		}

		var ground = new Plane( Vector3.Up, 0.0f );
		if ( ground.TryTrace( Gizmo.CurrentRay, out point, true ) )
		{
			normal = ground.Normal;
			return true;
		}

		point = default;
		normal = Vector3.Up;
		return false;
	}

	float[] BuildPathUvs( IReadOnlyList<Vector3> pathPoints, Material material )
	{
		var uvs = new float[pathPoints.Count];
		if ( pathPoints.Count <= 0 )
			return uvs;

		var texelsPerUnit = 1.0f / Math.Max( 1.0f / 256.0f, MathF.Abs( DefaultTextureScale ) );
		var materialSizeTexels = GetPathTextureSizeTexels( material );
		float length = 0.0f;
		uvs[0] = 0.0f;

		for ( int i = 1; i < pathPoints.Count; i++ )
		{
			length += pathPoints[i].Distance( pathPoints[i - 1] );
			uvs[i] = (length * texelsPerUnit) / materialSizeTexels;
		}

		return uvs;
	}

	static Vector2 BuildTextureUv( float pathParam, float circumferenceParam ) => new( pathParam, circumferenceParam );

	float[] BuildCircumferenceUvs( int sides )
	{
		var uvs = new float[sides];
		for ( int i = 0; i < sides; i++ )
		{
			var ratio = i / (float)sides;
			uvs[i] = ratio * DefaultTextureRepeatsCircumference;
		}

		return uvs;
	}

	static float GetCircumferenceUvAtRingEnd( float firstUv )
	{
		return firstUv + DefaultTextureRepeatsCircumference;
	}

	static float GetPathTextureSizeTexels( Material material )
	{
		var texture = material?.FirstTexture;
		var size = texture is not null && texture.IsValid ? texture.Width : 0;
		return Math.Max( 1.0f, size );
	}

	List<Vector3> BuildPathPoints()
	{
		if ( _points.Count <= 1 )
			return [.. _points];

		if ( PathDetail <= 0 && MathF.Abs( Slack ) <= 0.0001f )
			return [.. _points];

		var minStepsForSlack = MathF.Abs( Slack ) > 0.0001f ? 2 : 1;
		var steps = Math.Max( minStepsForSlack, PathDetail + 1 );
		var points = new List<Vector3>( (_points.Count - 1) * steps + 1 );

		for ( int i = 0; i < _points.Count - 1; i++ )
		{
			var p0 = _points[Math.Max( i - 1, 0 )];
			var p1 = _points[i];
			var p2 = _points[i + 1];
			var p3 = _points[Math.Min( i + 2, _points.Count - 1 )];

			for ( int s = 0; s < steps; s++ )
			{
				var t = s / (float)steps;
				var sag = 4.0f * t * (1.0f - t);
				points.Add( CatmullRom( p0, p1, p2, p3, t ) + (Vector3.Down * (sag * Slack)) );
			}
		}

		points.Add( _points[^1] );
		return points;
	}

	static Vector3 CatmullRom( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		var t2 = t * t;
		var t3 = t2 * t;
		return 0.5f * (
			(2.0f * p1) +
			(-p0 + p2) * t +
			(2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
			(-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
		);
	}

	static Vector3 BuildTangent( IReadOnlyList<Vector3> points, int i )
	{
		if ( points.Count < 2 ) return Vector3.Forward;
		if ( i == 0 ) return (points[1] - points[0]).Normal;
		if ( i == points.Count - 1 ) return (points[^1] - points[^2]).Normal;

		var a = (points[i] - points[i - 1]).Normal;
		var b = (points[i + 1] - points[i]).Normal;
		var tangent = (a + b).Normal;

		return tangent.LengthSquared > 0.0001f ? tangent : b;
	}

	static Vector3 BuildInitialNormal( Vector3 tangent )
	{
		var up = MathF.Abs( tangent.Dot( Vector3.Up ) ) > 0.98f ? Vector3.Right : Vector3.Up;
		return tangent.Cross( up ).Normal;
	}

	static Vector3 BuildNormalFromPrevious( Vector3 tangent, Vector3 previousNormal )
	{
		var projected = previousNormal - tangent * previousNormal.Dot( tangent );
		if ( projected.LengthSquared > 0.0001f )
			return projected.Normal;

		return BuildInitialNormal( tangent );
	}

	public override Widget CreateWidget() => new CableEditorWidget( this );

	[Range( 0.5f, 64f, slider: false ), Step( 0.5f ), Title( "Radius" ), WideMode]
	public float Size
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			BuildPreview();
		}
	} = 8f;

	[Range( 3, 32, slider: false ), Step( 1 ), Title( "Subdivisions" ), WideMode]
	public int Subdivisions
	{
		get;
		set
		{
			var clamped = Math.Clamp( value, 3, 32 );
			if ( field == clamped ) return;
			field = clamped;
			BuildPreview();
		}
	} = 8;

	[Range( 0, 16, slider: false ), Step( 1 ), Title( "Spacing" ), WideMode]
	public int PathDetail
	{
		get;
		set
		{
			var clamped = Math.Clamp( value, 0, 16 );
			if ( field == clamped ) return;
			field = clamped;
			BuildPreview();
		}
	} = 6;

	[Range( -512f, 512f, slider: false ), Step( 0.5f ), Title( "Slack" ), WideMode]
	public float Slack
	{
		get;
		set
		{
			var clamped = Math.Clamp( value, -512f, 512f );
			if ( MathF.Abs( field - clamped ) < 0.0001f ) return;
			field = clamped;
			BuildPreview();
		}
	} = DefaultSlack;

	[Description( "When enabled, closes the cable with caps at the start and end." )]
	public bool CapEnds
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			BuildPreview();
		}
	} = true;

	[Title( "Offset From Surface" ), Description( "When enabled, new points are pushed outward from the hit surface by the cable size to reduce clipping." )]
	public bool OffsetFromSurface
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
		}
	} = true;

	class CableEditorWidget : ToolSidebarWidget
	{
		readonly CableEditor _editor;

		public CableEditorWidget( CableEditor editor )
		{
			_editor = editor;
			Layout.Margin = 0;

			var group = AddGroup( "Cable Properties" );
			var so = editor.GetSerialized();
			group.Add( ControlSheetRow.Create( so.GetProperty( nameof( editor.Size ) ) ) );
			group.Add( ControlSheetRow.Create( so.GetProperty( nameof( editor.Subdivisions ) ) ) );
			group.Add( ControlSheetRow.Create( so.GetProperty( nameof( editor.PathDetail ) ) ) );
			group.Add( ControlSheetRow.Create( so.GetProperty( nameof( editor.Slack ) ) ) );
			group.Add( ControlSheetRow.Create( so.GetProperty( nameof( editor.CapEnds ) ) ) );
			group.Add( ControlSheetRow.Create( so.GetProperty( nameof( editor.OffsetFromSurface ) ) ) );

			Layout.AddStretchCell();
		}

		[Shortcut( "editor.delete", "DEL", typeof( SceneViewWidget ) )]
		public void DeletePoint() => _editor.RemovePoint();
	}
}
