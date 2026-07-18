using HalfEdgeMesh;

namespace Editor.MeshEditor;

public enum PathExtrudeOrigin
{
	/// <summary>
	/// The profile keeps its world position, offset only by rotation onto the path.
	/// </summary>
	Absolute,

	/// <summary>
	/// The profile is centered on the path start using its object's origin.
	/// </summary>
	[Title( "Object Local" )] ObjectLocal
}

/// <summary>
/// Extrudes a profile edge along a path edge, creating a new mesh.
/// </summary>
[Alias( "tools.path-extrude-tool" )]
public partial class PathExtrudeTool( List<IGrouping<MeshComponent, MeshEdge>> edgeGroups, MeshEdge[] edges ) : EditorTool
{
	GameObject _preview;
	MeshComponent _profileComponent;

	public static bool CanExtrude( List<IGrouping<MeshComponent, MeshEdge>> edgeGroups, MeshEdge[] edges )
	{
		if ( edgeGroups.Count != 2 )
			return false;

		if ( edges.Any( x => !x.IsValid() ) )
			return false;

		// at least one group must be fully open (the profile)
		return edgeGroups.Any( g => g.All( x => x.IsOpen ) );
	}

	public void UpdateMesh()
	{
		if ( !TryBuildMesh( ProfileOrigin, out var mesh, out _profileComponent ) )
		{
			DestroyPreview();
			return;
		}

		using var scope = SceneEditorSession.Scope();

		if ( !_preview.IsValid() )
		{
			_preview = new GameObject( true, "Path Extrude Preview" )
			{
				WorldTransform = _profileComponent.WorldTransform,
				Parent = _profileComponent.GameObject.Parent,
				Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden
			};

			var mc = _preview.Components.Create<MeshComponent>();
			mc.SmoothingAngle = 40;
		}

		var meshComponent = _preview.Components.Get<MeshComponent>();
		meshComponent.Mesh = mesh;
		meshComponent.RebuildMesh();
	}

	void DestroyPreview()
	{
		_preview?.Destroy();
		_preview = null;
	}

	public void Apply()
	{
		DestroyPreview();

		if ( !TryBuildMesh( ProfileOrigin, out var mesh, out var component ) )
		{
			GoBack();
			return;
		}

		using var scope = SceneEditorSession.Scope();

		var undo = SceneEditorSession.Active.UndoScope( "Path Extrude" ).WithGameObjectCreations();
		if ( DeleteSource )
			undo = undo.WithGameObjectDestructions( component.GameObject );

		using ( undo.Push() )
		{
			var go = new GameObject( true, component.GameObject.Name )
			{
				WorldTransform = component.WorldTransform,
				Parent = component.GameObject.Parent
			};

			var meshComponent = go.Components.Create<MeshComponent>();
			meshComponent.Mesh = mesh;
			meshComponent.SmoothingAngle = 40;

			SceneEditorSession.Active.Selection.Set( go );

			if ( DeleteSource )
				component.GameObject.Destroy();
		}

		GoBack();
	}

	public void Cancel() => GoBack();

	void GoBack() => EditorToolManager.SetSubTool( nameof( EdgeTool ) );

	public override void OnDisabled()
	{
		DestroyPreview();
	}

	/// <summary>
	/// Builds the extruded mesh without committing anything to the scene.
	/// </summary>
	bool TryBuildMesh( PathExtrudeOrigin profileOrigin, out PolygonMesh newMesh, out MeshComponent profileComponent )
	{
		newMesh = null;
		profileComponent = null;

		if ( !CanExtrude( edgeGroups, edges ) )
			return false;

		var openGroups = edgeGroups.Where( g => g.All( x => x.IsOpen ) ).ToList();

		// If both groups are wires, selection order decides: profile first, then path.
		var profileGroup = openGroups.Count == 1 ? openGroups[0] : edgeGroups[0];
		var pathGroup = edgeGroups.First( g => g != profileGroup );

		var component = profileGroup.Key;
		var mesh = component.Mesh;
		var transform = component.WorldTransform;

		// Dedupe opposite half-edges, winding away from the bordering face if there is one.
		var profileEdges = profileGroup
			.GroupBy( e => Math.Min( e.Handle.Index, mesh.GetOppositeHalfEdge( e.Handle ).Index ) )
			.Select( g => g.First() )
			.Select( e =>
			{
				mesh.GetFacesConnectedToEdge( e.Handle, out var hFaceA, out var hFaceB );

				var hEdge = hFaceA != FaceHandle.Invalid ? mesh.GetOppositeHalfEdge( e.Handle ) : e.Handle;
				mesh.GetEdgeVertices( hEdge, out var a, out var b );

				return (A: a, B: b, SourceFace: hFaceA != FaceHandle.Invalid ? hFaceA : hFaceB);
			} )
			.ToList();

		if ( profileEdges.All( x => x.SourceFace == FaceHandle.Invalid ) )
		{
			var remaining = profileEdges.ToList();
			profileEdges = [remaining[0]];
			remaining.RemoveAt( 0 );

			while ( remaining.Count > 0 )
			{
				var tail = profileEdges[^1].B;
				var idx = remaining.FindIndex( x => x.A == tail || x.B == tail );
				if ( idx < 0 )
					break;

				var next = remaining[idx];
				remaining.RemoveAt( idx );
				profileEdges.Add( next.A == tail ? next : (next.B, next.A, next.SourceFace) );
			}

			profileEdges.AddRange( remaining );
		}

		Vector3 ProfileVertexPos( VertexHandle v ) => transform.PointToWorld( mesh.GetVertexPosition( v ) );

		var chainCenter = profileEdges
			.Aggregate( Vector3.Zero, ( acc, e ) => acc + ProfileVertexPos( e.A ) + ProfileVertexPos( e.B ) )
			/ (profileEdges.Count * 2);

		var profileCenter = profileOrigin == PathExtrudeOrigin.ObjectLocal ? transform.Position : chainCenter;

		// Order the path edges into a polyline, chaining by vertex handle.

		var pathMesh = pathGroup.Key.Mesh;
		var pathTransform = pathGroup.Key.WorldTransform;

		var segments = pathGroup.Select( e =>
		{
			pathMesh.GetEdgeVertices( e.Handle, out var a, out var b );
			return a.Index < b.Index ? (A: a, B: b) : (A: b, B: a);
		} )
		.Distinct()
		.ToList();

		Vector3 VertexPos( VertexHandle v ) => pathTransform.PointToWorld( pathMesh.GetVertexPosition( v ) );

		// Open chains must start at a true endpoint (vertex used once); loops can start anywhere.
		var allEnds = segments.SelectMany( s => new[] { s.A, s.B } ).ToList();
		var candidates = allEnds.GroupBy( v => v ).Where( g => g.Count() == 1 ).Select( g => g.Key ).ToList();
		if ( candidates.Count == 0 )
			candidates = allEnds.Distinct().ToList();

		var startVertex = candidates.MinBy( v => VertexPos( v ).DistanceSquared( chainCenter ) );
		var pathVertices = new List<VertexHandle> { startVertex };

		while ( segments.Count > 0 )
		{
			var index = segments.FindIndex( s => s.A == pathVertices[^1] || s.B == pathVertices[^1] );
			if ( index < 0 )
				return false;

			pathVertices.Add( segments[index].A == pathVertices[^1] ? segments[index].B : segments[index].A );
			segments.RemoveAt( index );
		}

		var isClosedLoop = pathVertices.Count > 2 && pathVertices[0] == pathVertices[^1];
		var pathPoints = pathVertices.Select( VertexPos ).ToList();

		// Best-fit plane normal of the profile chain (Newell-style, sign-consistent).

		var profileNormal = Vector3.Zero;

		foreach ( var (a, b, _) in profileEdges )
		{
			var cross = (ProfileVertexPos( a ) - chainCenter).Cross( ProfileVertexPos( b ) - chainCenter );
			profileNormal += cross.Dot( profileNormal ) < 0 ? -cross : cross;
		}

		profileNormal = profileNormal.Normal;

		// Point the normal along the first tangent so FromToRotation takes the short way.
		if ( profileNormal.Dot( pathPoints[1] - pathPoints[0] ) < 0 )
			profileNormal = -profileNormal;

		Vector3 Tangent( int i ) => i < pathPoints.Count - 1
			? (pathPoints[i + 1] - pathPoints[i]).Normal
			: isClosedLoop ? (pathPoints[1] - pathPoints[0]).Normal
			: (pathPoints[i] - pathPoints[i - 1]).Normal;

		Vector3 InTangent( int i ) => i > 0
			? (pathPoints[i] - pathPoints[i - 1]).Normal
			: isClosedLoop ? (pathPoints[0] - pathPoints[^2]).Normal
			: Tangent( 0 );

		// Double-reflection transport - rotation minimizing, so the profile doesn't accumulate twist.
		Vector3 Sweep( Vector3 point, int i )
		{
			static Vector3 Reflect( Vector3 v, Vector3 axis )
				=> v - axis * (2f / axis.Dot( axis ) * axis.Dot( v ));

			var segment = pathPoints[i] - pathPoints[i - 1];
			var reflected = Reflect( point - pathPoints[i - 1], segment );
			var reflectedTangent = Reflect( Tangent( i - 1 ), segment );

			var correction = Tangent( i ) - reflectedTangent;
			if ( correction.Dot( correction ) > 1e-8f )
				reflected = Reflect( reflected, correction );

			return pathPoints[i] + reflected;
		}

		// Shear a ring point onto the corner's bisector plane, keeping strip width.
		Vector3 Mitre( Vector3 point, int i )
		{
			var dir = Tangent( i );

			var normal = (InTangent( i ) + dir).Normal;
			if ( normal.IsNearZeroLength )
				normal = dir;

			return point + dir * (pathPoints[i] - point).Dot( normal ) / dir.Dot( normal );
		}

		var result = new PolygonMesh { Transform = mesh.Transform };

		var ringVerts = new Dictionary<(int Ring, VertexHandle Vertex), VertexHandle>();
		var ringPositions = new Dictionary<(int Ring, VertexHandle Vertex), Vector3>();

		VertexHandle GetRingVertex( int i, VertexHandle v )
		{
			if ( ringVerts.TryGetValue( (i, v), out var existing ) )
				return existing;

			Vector3 position;

			if ( i == 0 )
			{
				// Place the profile at the path start, rotated onto the first tangent.
				position = ProfileVertexPos( v );

				if ( profileOrigin == PathExtrudeOrigin.ObjectLocal )
					position += pathPoints[0] - profileCenter;

				var dir = Tangent( 0 );
				position = profileNormal.IsNearZeroLength
					? position + dir * (pathPoints[0] - position).Dot( dir )
					: pathPoints[0] + Rotation.FromToRotation( profileNormal, dir ) * (position - pathPoints[0]);
			}
			else if ( isClosedLoop && i == pathPoints.Count - 1 )
			{
				// Closed loops reuse the first ring so the seam lines up.
				position = ringPositions[(0, v)];
			}
			else
			{
				position = Sweep( ringPositions[(i - 1, v)], i );
			}

			ringPositions[(i, v)] = position;
			return ringVerts[(i, v)] = result.AddVertex( transform.PointToLocal( Mitre( position, i ) ) );
		}

		var facesAdded = 0;

		foreach ( var (a, b, hSourceFace) in profileEdges )
		{
			for ( var i = 1; i < pathPoints.Count; i++ )
			{
				var hFace = result.AddFace(
					GetRingVertex( i - 1, a ),
					GetRingVertex( i - 1, b ),
					GetRingVertex( i, b ),
					GetRingVertex( i, a ) );

				if ( !hFace.IsValid )
					continue;

				facesAdded++;

				if ( hSourceFace != FaceHandle.Invalid )
					result.SetFaceMaterial( hFace, mesh.GetFaceMaterial( hSourceFace ) );

				result.TextureAlignToGrid( result.Transform, hFace );
			}
		}

		if ( facesAdded == 0 )
			return false;

		if ( isClosedLoop )
		{
			var seamVerts = ringVerts
				.Where( x => x.Key.Ring == 0 || x.Key.Ring == pathPoints.Count - 1 )
				.Select( x => x.Value )
				.ToList();

			result.MergeVerticesWithinDistance( seamVerts, 0.01f, false, false, out _ );
		}

		newMesh = result;
		profileComponent = component;
		return true;
	}
}
