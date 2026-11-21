using System;
using System.IO;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	readonly record struct Triangle( Vector3 A, Vector3 B, Vector3 C )
	{
		public Vector3 Normal => (B - A).Cross( C - A ).Normal;
	}

	sealed class OverlayFace
	{
		public Vector3[] Points;
		public Vector2[] UV;
		public int[] Indices;
		public Vector3 Normal;
		public bool Convex;

		Vector3 basisOrigin, basisU, basisV;
		Vector2 basisOffset;
		bool hasBasis;

		public Vector2 TextureCoordinate( Vector3 point )
		{
			if ( Convex && Points.Length == 4 && QuadCoordinates( Points, point ) is { } quad )
				return Vector2.Lerp( Vector2.Lerp( UV[0], UV[3], quad.y ), Vector2.Lerp( UV[1], UV[2], quad.y ), quad.x );

			for ( int i = 0; i < Indices.Length; i += 3 )
			{
				int a = Indices[i], b = Indices[i + 1], c = Indices[i + 2];
				var x = Points[a] - point;
				var y = Points[b] - point;
				var z = Points[c] - point;
				float area = (Points[b] - Points[a]).Cross( Points[c] - Points[a] ).Length;
				if ( area == 0 ) continue;
				float u = y.Cross( z ).Length / area, v = z.Cross( x ).Length / area, w = x.Cross( y ).Length / area;
				if ( MathF.Abs( 1 - (u + v + w) ) < 0.001f ) return u * UV[a] + v * UV[b] + w * UV[c];
			}

			// Clipping can leave a point just outside the guide polygon. Extend its texture plane.
			if ( !hasBasis )
			{
				Span<Vector3> positions = stackalloc Vector3[3];
				Span<Vector2> coords = stackalloc Vector2[3];
				PolygonMesh.GetTextureBasisVertices( Points, UV, positions, coords );
				PolygonMesh.CalcTextureBasisFromUVs( positions, coords, out basisU, out basisV );
				basisOrigin = positions[0];
				basisOffset = coords[0];
				hasBasis = true;
			}
			var delta = point - basisOrigin;
			return basisOffset + new Vector2( delta.Dot( basisU ), delta.Dot( basisV ) );
		}
	}

	// Invert the bilinear surface of a convex quad, using Hammer's parallel-edge tolerances.
	internal static Vector2? QuadCoordinates( Vector3[] quad, Vector3 point )
	{
		var delta = point - quad[0];
		var u = quad[1] - quad[0];
		var v = quad[3] - quad[0];
		var otherU = quad[2] - quad[3];
		var otherV = quad[2] - quad[1];

		static Vector2? Inside( Vector2 uv ) => uv.x is >= 0 and <= 1 && uv.y is >= 0 and <= 1 ? uv : null;
		Vector2? Parallel( Vector3 x, Vector3 y )
		{
			var normal = x.Cross( y );
			float denominator = normal.LengthSquared;
			if ( denominator == 0 ) return null;
			return Inside( new( delta.Cross( y ).Dot( normal ) / denominator, x.Cross( delta ).Dot( normal ) / denominator ) );
		}

		if ( u.Normal.Dot( otherU.Normal ) > 0.99f && MathF.Abs( u.Length - otherU.Length ) < 0.1f ) return Parallel( u, v );
		var averageU = (u.Normal + otherU.Normal) * ((u.Length + otherU.Length) * 0.25f);
		var averageV = (v.Normal + otherV.Normal) * ((v.Length + otherV.Length) * 0.25f);
		bool flip = v.Normal.Dot( otherV.Normal ) > 0.99f;
		if ( flip ) { (u, v) = (v, u); otherU = otherV; }

		var n = u.Cross( v ).Abs();
		int drop = n.x > n.y && n.x > n.z ? 0 : n.y > n.z ? 1 : 2;
		int i = (drop + 1) % 3, j = (drop + 2) % 3;
		if ( MathF.Abs( u[i] ) < MathF.Abs( u[j] ) ) (i, j) = (j, i);
		double Cross( Vector3 x, Vector3 y ) => x[i] * y[j] - x[j] * y[i];
		var bend = otherU - u;
		double a = Cross( bend, v );
		if ( Math.Abs( a ) < 0.1 ) return Parallel( averageU, averageV );
		double b = Cross( delta, bend ) - Cross( v, u ), c = Cross( delta, u );
		double discriminant = b * b - 4 * a * c;
		if ( discriminant < 0 ) return null;
		double root = Math.Sqrt( discriminant );
		Vector2? Solve( double t )
		{
			if ( t is < 0 or > 1 ) return null;
			double divisor = u[i] + t * bend[i];
			if ( Math.Abs( divisor ) < 1e-5 ) return null;
			float s = (float)((delta[i] - v[i] * t) / divisor);
			return Inside( flip ? new( (float)t, s ) : new( s, (float)t ) );
		}
		return Solve( (-b + root) / (2 * a) ) ?? Solve( (-b - root) / (2 * a) );
	}

	static void ClipTriangles( List<Triangle> input, List<Triangle> output, Vector3 origin, Vector3 normal )
	{
		output.Clear();
		normal = normal.Normal;
		float offset = -normal.Dot( origin );
		Span<Vector3> points = stackalloc Vector3[3];
		Span<Vector3> clipped = stackalloc Vector3[4];
		foreach ( var triangle in input )
		{
			points[0] = triangle.A;
			points[1] = triangle.B;
			points[2] = triangle.C;
			int count = 0;
			var previous = points[2];
			float from = previous.Dot( normal ) + offset;
			foreach ( var point in points )
			{
				float to = point.Dot( normal ) + offset;
				if ( from <= 0 ) clipped[count++] = previous;
				if ( from * to < 0 ) clipped[count++] = previous + (-from / (to - from)) * (point - previous);
				previous = point;
				from = to;
			}
			for ( int j = 1; j + 1 < count; j++ ) output.Add( new( clipped[0], clipped[j], clipped[j + 1] ) );
		}
	}

	// A placed overlay can land on different geometry at every instance location.
	// Store the clipped surfaces on the placement while keeping its editable definition shared.
	static void BakeOverlays( SourceNode world, HashSet<SourceNode> definitions )
	{
		var projection = new OverlayProjection();
		var path = new HashSet<SourceNode>();
		void Visit( SourceNode node, Transform placement, SourceNode owner, bool editorOnly, bool forceHidden, SourceNode owningWorld, Transform worldPlacement )
		{
			if ( !path.Add( node ) ) throw new InvalidDataException( "Recursive overlay instance." );
			var transform = placement.ToWorld( node.Transform );
			editorOnly |= node.EditorOnly;
			forceHidden |= node.Hidden;
			if ( node.Overlay is { } element )
			{
				// Explicit target IDs belong to the overlay's source map. Other modes see the root world.
				bool targeted = element.Get( "projectionMode", 0 ) == 3;
				var surfaces = ProjectOverlay( element, transform, targeted ? owningWorld : world, definitions, targeted ? worldPlacement : Transform.Zero, projection );
				if ( owner is null ) node.Meshes = surfaces;
				else if ( surfaces.Count > 0 )
				{
					owner.ProjectedOverlays ??= [];
					owner.ProjectedOverlays.Add( new SourceNode
					{
						Name = node.Name,
						TypeString = node.TypeString,
						EditorOnly = editorOnly,
						ForceHidden = forceHidden,
						Kind = NodeKind.GroupOrMesh,
						Transform = transform,
						Meshes = surfaces,
						Children = []
					} );
				}
			}
			if ( node.Target is { } target )
			{
				var delta = PlacementDelta( transform, target );
				bool external = !string.IsNullOrEmpty( node.TargetMapPath );
				Visit( target, delta, owner ?? node, editorOnly, forceHidden, external ? target : owningWorld, external ? delta : worldPlacement );
			}
			foreach ( var child in node.Children )
				if ( !definitions.Contains( child ) ) Visit( child, placement, owner, editorOnly, forceHidden, owningWorld, worldPlacement );
			path.Remove( node );
		}
		Visit( world, Transform.Zero, null, false, false, world, Transform.Zero );
	}

	static Transform PlacementDelta( Transform transform, SourceNode target ) =>
		transform.WithScale( 1 ).ToWorld( target.Transform.WithScale( 1 ).ToLocal( Transform.Zero ) );

	// Overlays in the same world repeatedly query the same placed meshes.
	sealed class OverlayReceiver( PolygonMesh mesh, Transform placement )
	{
		readonly BBox bounds = mesh.CalculateBounds( placement );
		List<(BBox Bounds, Vector3[] Points)> faces;

		public void Collect( BBox query, List<Triangle> triangles )
		{
			if ( !query.Overlaps( bounds ) ) return;
			faces ??= ReadFaces();
			foreach ( var face in faces )
			{
				if ( !query.Overlaps( face.Bounds ) ) continue;
				var points = face.Points;
				var indices = PolygonTriangulation.TriangulateFace( points );
				for ( int i = 0; i < indices.Length; i += 3 )
					triangles.Add( new( points[indices[i]], points[indices[i + 1]], points[indices[i + 2]] ) );
			}
		}

		List<(BBox, Vector3[])> ReadFaces()
		{
			var result = new List<(BBox, Vector3[])>( mesh.Topology.FaceCount );
			foreach ( var face in mesh.FaceHandles )
			{
				var material = mesh.GetFaceMaterial( face );
				if ( material is not null && (material.Flags.GetInt( "tools.toolsmaterial" ) != 0 || material.Flags.GetInt( "mapbuilder.nonsolid" ) != 0) ) continue;
				var points = new Vector3[mesh.Topology.ComputeNumEdgesInFace( face )];
				var edge = face.Edge;
				BBox faceBounds = default;
				for ( int i = 0; i < points.Length; i++, edge = edge.NextEdge )
				{
					var point = placement.PointToWorld( mesh.GetVertexPosition( edge.Vertex ) );
					points[i] = point;
					faceBounds = i == 0 ? new BBox( point, point ) : faceBounds.AddPoint( point );
				}
				result.Add( (faceBounds, points) );
			}
			return result;
		}
	}

	sealed class OverlayProjection
	{
		public readonly Dictionary<Element, SceneMeshData> Guides = [];
		public readonly Dictionary<(PolygonMesh, Transform), OverlayReceiver> Receivers = [];
		public readonly List<Triangle> Runtime = [], Editor = [], Clipped = [], Buffer = [];
	}

	static IReadOnlyList<SceneMeshData> ProjectOverlay( Element element, Transform overlay, SourceNode world, HashSet<SourceNode> definitions, Transform worldPlacement,
		OverlayProjection projection )
	{
		if ( !projection.Guides.TryGetValue( element, out var guide ) )
			projection.Guides.Add( element, guide = ReadVmapMesh( element, null ) );
		float depth = element.Get( "projectionFar", 128f );
		int mode = element.Get( "projectionMode", 0 );
		int renderOrder = element.Get( "renderOrder", 0 );
		bool disabledInLowQuality = element.Get( "disabledInLowQuality", false );
		bool backfaces = element.Get( "projectOnBackFaces", false );
		var targets = mode == 3 && element.ContainsKey( "projectionTargets" ) ? element.Numbers<int>( "projectionTargets" ).ToArray().ToHashSet() : null;
		projection.Runtime.Clear();
		projection.Editor.Clear();
		var bounds = new BBox { Mins = new Vector3( float.MaxValue ), Maxs = new Vector3( float.MinValue ) };
		var groups = new Dictionary<Material, List<OverlayFace>>();
		foreach ( var handle in guide.Mesh.FaceHandles )
		{
			var material = guide.Mesh.GetFaceMaterial( handle );
			guide.Mesh.GetFaceVerticesConnectedToFace( handle, out var edges );
			var points = edges.Select( edge => overlay.PointToWorld( guide.Mesh.GetVertexPosition( edge.Vertex ) ) ).ToArray();
			// Hammer exports grid-snapped guide vertices and rebuilds their projection planes at compile time.
			for ( int i = 0; i < points.Length; i++ )
			{
				static float Snap( float a ) => MathF.Floor( a * 8 + 0.5f ) / 8;
				points[i] = new( Snap( points[i].x ), Snap( points[i].y ), Snap( points[i].z ) );
			}
			var normal = PolygonTriangulation.PolygonNormal( points );
			var face = new OverlayFace { Points = points, UV = edges.Select( guide.Mesh.GetTextureCoord ).ToArray(), Indices = PolygonTriangulation.Triangulate( points, normal ), Normal = normal };
			if ( face.Indices.Length == 0 ) continue;
			var min = face.UV[0]; var max = min;
			for ( int i = 0; i < points.Length; i++ )
			{
				min = Vector2.Min( min, face.UV[i] ); max = Vector2.Max( max, face.UV[i] );
			}
			var extrusion = normal * (depth > 0 ? depth : float.MaxValue);
			foreach ( var point in points ) bounds = bounds.AddPoint( point ).AddPoint( point - extrusion );
			var center = (min + max) * 0.5f;
			for ( int i = 0; i < face.UV.Length; i++ ) face.UV[i] -= new Vector2( (int)center.x, (int)center.y );
			face.Convex = true;
			for ( int i = 0; i < points.Length; i++ )
				if ( new Triangle( points[i], points[(i + 1) % points.Length], points[(i + 2) % points.Length] ).Normal.Dot( face.Normal ) < -0.001f ) face.Convex = false;
			if ( !groups.TryGetValue( material, out var faces ) ) groups.Add( material, faces = [] );
			faces.Add( face );
		}
		if ( groups.Count == 0 ) return [];
		bounds = bounds.Grow( 1 );
		var path = new HashSet<SourceNode>();
		void Visit( SourceNode node, Transform placement, bool selected, bool editorOnly )
		{
			if ( node.Hidden ) return;
			if ( !path.Add( node ) ) throw new InvalidDataException( "Recursive overlay projection target." );
			editorOnly |= node.EditorOnly;
			var receivers = editorOnly ? projection.Editor : projection.Runtime;
			selected |= targets?.Contains( node.NodeId ) == true;
			var transform = placement.ToWorld( node.Transform );
			if ( node.TypeString != "CMapStaticOverlay" && (mode != 3 || selected) )
			{
				if ( mode != 2 && !node.CulledProp )
				{
					for ( int i = 0; i < node.Meshes.Count; i++ )
					{
						var mesh = node.Meshes[i].Mesh;
						var key = (mesh, transform);
						if ( !projection.Receivers.TryGetValue( key, out var receiver ) )
							projection.Receivers.Add( key, receiver = new( mesh, transform ) );
						receiver.Collect( bounds, receivers );
					}
				}
				if ( mode != 1 && node.Kind == NodeKind.Entity && node.Entity.TypeName == "prop_static" )
					AddModelReceivers( node.Entity, transform, bounds, receivers );
			}
			if ( node.Target is { } target )
			{
				var delta = PlacementDelta( transform, target );
				if ( mode != 3 || selected || string.IsNullOrEmpty( node.TargetMapPath ) )
					Visit( target, delta, selected, editorOnly );
			}
			foreach ( var child in node.Children )
				if ( !definitions.Contains( child ) ) Visit( child, placement, selected, editorOnly );
			path.Remove( node );
		}
		Visit( world, worldPlacement, false, false );

		var result = new List<SceneMeshData>( groups.Count );
		foreach ( var (material, faces) in groups )
		{
			ProjectFaces( material, faces, projection.Runtime, false );
			ProjectFaces( material, faces, projection.Editor, true );
		}
		return result;

		void ProjectFaces( Material material, List<OverlayFace> faces, List<Triangle> receivers, bool editorOnly )
		{
			if ( receivers.Count == 0 ) return;
			var mesh = new PolygonMesh();
			var normal = faces.Aggregate( Vector3.Zero, ( sum, f ) => sum + f.Normal ).Normal;
			var center = faces[0].Points.Aggregate( Vector3.Zero, ( sum, p ) => sum + p ) / faces[0].Points.Length;
			var clipped = projection.Clipped;
			var buffer = projection.Buffer;
			foreach ( var face in faces )
			{
				void Project( ReadOnlySpan<Vector3> polygon )
				{
					clipped.Clear();
					foreach ( var tri in receivers )
						if ( backfaces || tri.Normal.Dot( normal ) > 0.00001f ) clipped.Add( tri );
					void Clip( Vector3 point, Vector3 plane )
					{
						ClipTriangles( clipped, buffer, point, plane );
						(clipped, buffer) = (buffer, clipped);
					}
					if ( depth > 0 )
					{
						Clip( polygon[0], normal );
						Clip( polygon[0] - normal * depth, -normal );
					}
					var prev = polygon[^1];
					foreach ( var p in polygon )
					{
						Clip( prev, (-normal).Cross( (p - prev).Normal ) );
						prev = p;
					}
					foreach ( var tri in clipped )
					{
						var f = mesh.AddFace( mesh.AddVertex( overlay.PointToLocal( tri.A ) ), mesh.AddVertex( overlay.PointToLocal( tri.B ) ), mesh.AddVertex( overlay.PointToLocal( tri.C ) ) );
						mesh.SetFaceMaterial( f, material );
						void Corner( HalfEdgeMesh.HalfEdgeHandle edge, Vector3 p )
						{
							mesh.SetTextureCoord( edge, face.TextureCoordinate( p - normal * normal.Dot( p - center ) ) );
							mesh.SetVertexColor( edge, Color.White );
						}
						Corner( f.Edge, tri.A ); Corner( f.Edge.NextEdge, tri.B ); Corner( f.Edge.NextEdge.NextEdge, tri.C );
					}
				}
				if ( face.Convex ) Project( face.Points );
				else for ( int i = 0; i < face.Indices.Length; i += 3 ) Project( [face.Points[face.Indices[i]], face.Points[face.Indices[i + 1]], face.Points[face.Indices[i + 2]]] );
			}
			if ( !mesh.FaceHandles.Any() ) return;
			mesh.SetSmoothingAngle( 0 );
			result.Add( new( mesh, 0, SceneMeshPhysics.None, guide.Tint, false, guide.Tags,
				EditorOnly: editorOnly, RenderOrder: renderOrder, DisabledInLowQuality: disabledInLowQuality ) );
		}
	}
	static void AddModelReceivers( MapLoader.ObjectEntry entity, Transform transform, BBox bounds, List<Triangle> receivers )
	{
		if ( entity.GetResource<Model>( "model" ) is not { IsError: false } model || !bounds.Overlaps( model.Bounds.Transform( transform ) ) ) return;

		foreach ( var (i, d) in PropDrawCalls( model, entity ) )
		{
			if ( !model.TryReadDrawCall( i, d, out var vertices, out var indices, out _ ) ) continue;
			for ( int j = 0; j + 2 < indices.Length; j += 3 )
			{
				var tri = new Triangle( transform.PointToWorld( vertices[indices[j]].Position ), transform.PointToWorld( vertices[indices[j + 1]].Position ), transform.PointToWorld( vertices[indices[j + 2]].Position ) );
				if ( bounds.Overlaps( BBox.FromPositionAndSize( tri.A, 0 ).AddPoint( tri.B ).AddPoint( tri.C ) ) ) receivers.Add( tri );
			}
		}
	}
}
