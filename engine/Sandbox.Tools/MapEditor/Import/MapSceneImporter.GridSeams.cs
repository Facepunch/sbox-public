using System;
using HalfEdgeMesh;
using N = System.Numerics;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	sealed partial class VmapReader
	{
		readonly Dictionary<SourceNode, OpenPart[]> openTiles = [];

		OpenPart[] GetOpenParts( SourceNode target )
		{
			if ( openTiles.TryGetValue( target, out var existing ) ) return existing;
			var parts = new List<OpenPart>();
			void Visit( SourceNode node )
			{
				for ( int i = 0; i < node.Meshes.Count; i++ )
				{
					var mesh = node.Meshes[i].Mesh;
					var vertices = mesh.HalfEdgeHandles.Where( edge => !edge.Face.IsValid && edge.OppositeEdge.Face.IsValid )
						.Select( edge => edge.Vertex ).Distinct().ToArray();
					if ( vertices.Length > 0 ) parts.Add( new( node, i, vertices ) );
				}
				foreach ( var child in node.Children ) Visit( child );
			}
			Visit( target );
			return openTiles[target] = parts.ToArray();
		}

		void JoinGroundSeams( SourceNode[] cells, int width, int height )
		{
			Dictionary<int, List<SeamMesh>> open = null;
			for ( int i = 0; i < cells.Length; i++ )
			{
				if ( cells[i] is not { } tile ) continue;
				Add( tile, i );
				foreach ( var proxy in tile.Children ) Add( proxy, i );
			}
			if ( open is null ) return;

			// Hammer revisits each pair from both cells in this order. At multi-tile junctions
			// later pairs see the results of earlier averages.
			ReadOnlySpan<(int X, int Y)> neighbors = [(0, 0), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];
			foreach ( var (index, meshes) in open )
			{
				int x = index % width, y = index / width;
				foreach ( var offset in neighbors )
				{
					int nx = x + offset.X, ny = y + offset.Y;
					if ( (uint)nx >= width || (uint)ny >= height || !open.TryGetValue( ny * width + nx, out var others ) ) continue;
					foreach ( var a in meshes )
						foreach ( var b in others )
							if ( a != b ) a.Join( b );
				}
			}

			foreach ( var meshes in open.Values )
			{
				foreach ( var group in meshes.Where( m => m.Changed ).GroupBy( m => m.Instance ) )
				{
					var edited = new Dictionary<SourceNode, SceneMeshData[]>();
					foreach ( var mesh in group )
					{
						var source = mesh.Part.Node;
						if ( !edited.TryGetValue( source, out var parts ) ) edited[source] = parts = source.Meshes.ToArray();
						parts[mesh.Part.Index] = mesh.Bake();
					}
					group.Key.Target = Replace( group.Key.Target, edited );
				}
			}

			void Add( SourceNode instance, int cell )
			{
				var parts = GetOpenParts( instance.Target );
				if ( parts.Length == 0 ) return;
				open ??= [];
				if ( !open.TryGetValue( cell, out var list ) ) open[cell] = list = [];
				var placement = PlacementDelta( instance.Transform, instance.Target );
				foreach ( var part in parts ) list.Add( new SeamMesh( instance, part, placement.ToWorld( part.Node.Transform ) ) );
			}
		}

		static SourceNode Replace( SourceNode node, Dictionary<SourceNode, SceneMeshData[]> edits )
		{
			SourceNode copy = null;
			if ( edits.TryGetValue( node, out var meshes ) )
			{
				copy = node.ShallowCopy();
				copy.Meshes = meshes;
			}
			bool removed = false;
			for ( int i = 0; i < node.Children.Length; i++ )
			{
				var child = Replace( node.Children[i], edits );
				if ( child == node.Children[i] ) continue;
				copy ??= node.ShallowCopy();
				if ( copy.Children == node.Children ) copy.Children = (SourceNode[])node.Children.Clone();
				if ( child.Meshes.Count == 0 && node.Children[i].Meshes.Count > 0 && child.Children.Length == 0 )
				{
					child = null;
					removed = true;
				}
				copy.Children[i] = child;
			}
			if ( removed ) copy.Children = Array.FindAll( copy.Children, child => child is not null );
			return copy ?? node;
		}
	}

	sealed record OpenPart( SourceNode Node, int Index, VertexHandle[] Vertices )
	{
		public SceneMeshData Data => Node.Meshes[Index];

		// A boundary vertex may have several incident faces but only edges with one face participate.
		public readonly BoundaryEdge[][] Edges = BuildEdges( Node.Meshes[Index].Mesh, Vertices );

		static BoundaryEdge[][] BuildEdges( PolygonMesh mesh, VertexHandle[] vertices )
		{
			var indices = vertices.Select( ( v, i ) => (v, i) ).ToDictionary( p => p.v, p => p.i );
			var edges = new BoundaryEdge[vertices.Length][];
			var boundary = new List<BoundaryEdge>();
			for ( int i = 0; i < vertices.Length; i++ )
			{
				boundary.Clear();
				var first = vertices[i].Edge;
				var outgoing = first;
				do
				{
					var incoming = outgoing.OppositeEdge;
					if ( outgoing.Face.IsValid != incoming.Face.IsValid )
					{
						var faceEdge = outgoing.Face.IsValid ? outgoing : incoming;
						var corner = incoming.Face.IsValid ? incoming : mesh.Topology.FindPreviousEdgeInFaceLoop( outgoing );
						boundary.Add( new( indices[outgoing.Vertex], corner, mesh.Topology.GetFullEdgeForHalfEdge( faceEdge ).Index, mesh.GetEdgeSmoothing( faceEdge ) ) );
					}
					outgoing = incoming.NextEdge;
				} while ( outgoing != first );
				edges[i] = boundary.ToArray();
			}
			return edges;
		}
	}

	readonly record struct BoundaryEdge( int End, HalfEdgeHandle Corner, int EdgeIndex, PolygonMesh.EdgeSmoothMode Mode );

	sealed class SeamMesh
	{
		public readonly SourceNode Instance;
		public readonly OpenPart Part;
		readonly Transform transform;
		readonly Vector3[] positions;
		BBox bounds;
		Dictionary<int, N.Vector4> tints;
		Dictionary<HalfEdgeHandle, Vector3> normals;
		(Vector3 Position, int Index)[] sorted;
		public bool Changed { get; private set; }

		public SeamMesh( SourceNode instance, OpenPart part, Transform transform )
		{
			Instance = instance;
			Part = part;
			this.transform = transform;
			positions = part.Vertices.Select( v => transform.PointToWorld( part.Data.Mesh.GetVertexPosition( v ) ) ).ToArray();
			bounds = BBox.FromPoints( positions );
		}

		public void Join( SeamMesh other )
		{
			if ( !bounds.Grow( 1 ).Overlaps( other.bounds ) ) return;
			// Snapshot the search positions once per pair, as Hammer does with its vertex tree.
			var candidates = other.sorted ??= other.positions.Select( ( p, i ) => (p, i) ).OrderBy( entry => entry.p.x ).ToArray();
			for ( int a = 0; a < positions.Length; a++ )
			{
				var p = positions[a];
				int lo = 0, hi = candidates.Length;
				while ( lo < hi )
				{
					int mid = (lo + hi) / 2;
					if ( candidates[mid].Position.x < p.x - 1 ) lo = mid + 1;
					else hi = mid;
				}
				float distance = 1;
				int b = -1;
				for ( int n = lo; n < candidates.Length && candidates[n].Position.x <= p.x + 1; n++ )
				{
					float d = (candidates[n].Position - p).LengthSquared;
					if ( d >= distance ) continue;
					b = candidates[n].Index;
					distance = d;
				}
				if ( b < 0 ) continue;
				var q = other.positions[b];
				if ( !p.Equals( q ) )
				{
					positions[a] = other.positions[b] = (p + q) * 0.5f;
					bounds = bounds.AddPoint( positions[a] );
					other.bounds = other.bounds.AddPoint( positions[a] );
					sorted = other.sorted = null;
					Changed = other.Changed = true;
				}
				SmoothBoundary( a, other, b );
				var sum = SumTint( a, out int count, out var firstTint ) + other.SumTint( b, out int otherCount, out var secondTint );
				var average = sum / (count + otherCount);
				if ( firstTint != average ) SetTint( a, average );
				if ( secondTint != average ) other.SetTint( b, average );
			}
		}

		void SmoothBoundary( int vertex, SeamMesh other, int otherVertex )
		{
			foreach ( var a in Part.Edges[vertex] )
			{
				foreach ( var b in other.Part.Edges[otherVertex] )
				{
					if ( (positions[a.End] - other.positions[b.End]).LengthSquared > 1 ) continue;
					// Hammer reads both edge flags from the source mesh on each directed pass.
					var edge = Part.Data.Mesh.HalfEdgeHandleFromIndex( b.EdgeIndex );
					var mode = !edge.IsValid ? PolygonMesh.EdgeSmoothMode.Default : Part.Data.Mesh.GetEdgeSmoothing( edge.Face.IsValid ? edge : edge.OppositeEdge );
					if ( a.Mode == PolygonMesh.EdgeSmoothMode.Hard || mode == PolygonMesh.EdgeSmoothMode.Hard ) break;
					if ( a.Mode != PolygonMesh.EdgeSmoothMode.Soft || mode != PolygonMesh.EdgeSmoothMode.Soft )
					{
						Part.Data.Mesh.ComputeFaceNormal( a.Corner.Face, out var faceA );
						other.Part.Data.Mesh.ComputeFaceNormal( b.Corner.Face, out var faceB );
						float threshold = MathF.Cos( Math.Min( Part.Data.Smoothing, other.Part.Data.Smoothing ).DegreeToRadian() );
						if ( WorldNormal( faceA ).Dot( other.WorldNormal( faceB ) ) <= threshold + 0.00001f ) break;
					}
					var na = CurrentNormal( a.Corner );
					var nb = other.CurrentNormal( b.Corner );
					if ( !na.AlmostEqual( nb, 0.000001f ) )
					{
						var average = (na + nb).Normal;
						(normals ??= [])[a.Corner] = average;
						(other.normals ??= [])[b.Corner] = average;
						Changed = other.Changed = true;
					}
					break;
				}
			}
		}

		Vector3 WorldNormal( Vector3 local ) => transform.Rotation * (local / transform.SafeScale).Normal;

		Vector3 CurrentNormal( HalfEdgeHandle corner ) => normals?.TryGetValue( corner, out var value ) == true
			? value : WorldNormal( Part.Data.Mesh.ComputeFaceVertexNormal( corner ) );

		N.Vector4 SumTint( int vertex, out int count, out N.Vector4? uniform )
		{
			var data = Part.Data;
			var first = Part.Vertices[vertex].Edge;
			var outgoing = first;
			var sum = N.Vector4.Zero;
			count = 0;
			uniform = null;
			do
			{
				var corner = outgoing.OppositeEdge;
				if ( corner.Face.IsValid )
				{
					var value = tints is not null && tints.TryGetValue( vertex, out var tint ) ? tint : CornerTint( data, corner );
					if ( count == 0 ) uniform = value;
					else if ( uniform != value ) uniform = null;
					sum += value;
					count++;
				}
				outgoing = corner.NextEdge;
			} while ( outgoing != first );
			return sum;
		}

		static N.Vector4 CornerTint( SceneMeshData data, HalfEdgeHandle corner )
		{
			if ( data.SourcePaint?.TryGetValue( corner, out var raw ) == true ) return raw.Tint;
			var color = data.Mesh.GetVertexColor( corner );
			return new N.Vector4( color.r, color.g, color.b, color.a ) / 255f;
		}

		void SetTint( int vertex, N.Vector4 tint )
		{
			(tints ??= [])[vertex] = tint;
			Changed = true;
		}

		public SceneMeshData Bake()
		{
			var source = Part.Data;
			var mesh = new PolygonMesh();
			mesh.MergeMesh( source.Mesh, Transform.Zero, out var vertices, out var corners, out _ );
			for ( int i = 0; i < positions.Length; i++ )
			{
				var old = Part.Vertices[i];
				var vertex = vertices[old];
				if ( !positions[i].Equals( transform.PointToWorld( source.Mesh.GetVertexPosition( old ) ) ) )
					mesh.SetVertexPositionPreserveNormals( vertex, transform.PointToLocal( positions[i] ) );
				if ( tints is null || !tints.TryGetValue( i, out var tint ) ) continue;
				var first = vertex.Edge;
				var outgoing = first;
				do
				{
					var corner = outgoing.OppositeEdge;
					if ( corner.Face.IsValid ) mesh.SetVertexColor( corner, PackVertexPaint( tint ) );
					outgoing = corner.NextEdge;
				} while ( outgoing != first );
			}
			if ( normals is not null )
				foreach ( var (corner, normal) in normals )
					mesh.SetCornerNormal( corners[corner], ((transform.Rotation.Inverse * normal) * transform.Scale).Normal );
			mesh.SetSmoothingAngle( source.Smoothing );
			return source with { Mesh = mesh, SourcePaint = null };
		}
	}
}
