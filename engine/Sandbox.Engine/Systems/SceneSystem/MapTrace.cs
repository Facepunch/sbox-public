using Borealis.Source2.Models;
using Borealis.Source2.World;
using N3 = System.Numerics.Vector3;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox;

/// <summary>
/// A map's render meshes for <see cref="SceneWorld.Trace"/>, as native's scene world traced its world objects
/// (<c>CSceneWorld::MeshTrace</c> over each object's <c>CModel::BuildRayTraceObject</c>): every LOD 0 triangle of every
/// model the map places, whatever its mesh groups, in a bounding volume hierarchy per model, and the placed models in one
/// over their world bounds. Built the first time something traces the map; the models are read by Borealis.Source2.
/// </summary>
internal sealed class MapTrace
{
	/// <summary>A hit: where along the ray, the triangle and its corners (in the model's space), and the material.</summary>
	public struct Hit
	{
		public float Fraction;
		public Vector3 Position;
		public Vector3 Normal;
		public int Triangle;
		public Vector2 Uv;
		public string Material;
		public Transform Transform;
		public Vector3 V0, V1, V2;
	}

	readonly ModelTriangles[] models;
	readonly NMatrix[] toWorld;
	readonly NMatrix[] toModel;
	readonly Transform[] transforms;
	readonly Bvh instances;

	public MapTrace( MapGeometry map, Func<string, ModelFile> readModel )
	{
		var timer = System.Diagnostics.Stopwatch.StartNew();
		var byPath = new Dictionary<string, ModelTriangles>( StringComparer.OrdinalIgnoreCase );
		var placed = new List<(ModelTriangles Model, NMatrix World)>();
		var origin = NMatrix.CreateTranslation( map.Origin.x, map.Origin.y, map.Origin.z );

		ModelTriangles Model( string path )
		{
			if ( string.IsNullOrEmpty( path ) ) return null;
			if ( byPath.TryGetValue( path, out var made ) ) return made;
			ModelFile file = null;
			try
			{
				file = readModel( path );
			}
			catch ( Exception e )
			{
				Log.Warning( $"Can't trace {path}: {e.Message}" );
			}

			made = file is null ? null : ModelTriangles.Read( file );
			byPath[path] = made;
			return made;
		}

		foreach ( var node in map.World.Nodes )
		{
			foreach ( var o in node.SceneObjects )
			{
				if ( Model( o.Model ) is { } model ) placed.Add( (model, o.Transform * origin) );
			}

			// An aggregate's model is in world space already
			foreach ( var a in node.Aggregates )
			{
				if ( Model( a.Model ) is { } model ) placed.Add( (model, origin) );
			}
		}

		models = new ModelTriangles[placed.Count];
		toWorld = new NMatrix[placed.Count];
		toModel = new NMatrix[placed.Count];
		transforms = new Transform[placed.Count];
		var mins = new N3[placed.Count];
		var maxs = new N3[placed.Count];
		for ( int i = 0; i < placed.Count; i++ )
		{
			var (model, world) = placed[i];
			models[i] = model;
			toWorld[i] = world;
			NMatrix.Invert( world, out toModel[i] );
			transforms[i] = MapGeometry.ToTransform( world, Vector3.Zero );
			(mins[i], maxs[i]) = Bvh.Transform( model.Tree.Min, model.Tree.Max, world );
		}

		instances = Bvh.Build( mins, maxs );
		Log.Info( $"Render-mesh trace for {map.Folder}: {placed.Count} placed models, {byPath.Values.Sum( x => x?.Count ?? 0 )} triangles in {byPath.Count} models, built in {timer.Elapsed.TotalMilliseconds:F0} ms" );
	}

	/// <summary>The nearest hit from <paramref name="start"/> to <paramref name="end"/> nearer than <paramref name="best"/>'s fraction.</summary>
	public bool Trace( Vector3 start, Vector3 end, int cullMode, ref Hit best )
	{
		var found = false;
		var hit = best;
		Visit( start, end, cullMode, ( in Hit h ) =>
		{
			if ( h.Fraction >= hit.Fraction ) return;
			hit = h;
			found = true;
		}, nearest: true, () => hit.Fraction );

		if ( found ) best = hit;
		return found;
	}

	/// <summary>Every hit from <paramref name="start"/> to <paramref name="end"/>.</summary>
	public void TraceAll( Vector3 start, Vector3 end, int cullMode, List<Hit> hits )
	{
		Visit( start, end, cullMode, ( in Hit h ) => hits.Add( h ), nearest: false, () => 1 );
	}

	delegate void HitHandler( in Hit hit );

	void Visit( Vector3 start, Vector3 end, int cullMode, HitHandler handle, bool nearest, Func<float> limit )
	{
		var origin = new N3( start.x, start.y, start.z );
		var delta = new N3( end.x, end.y, end.z ) - origin;
		if ( delta.LengthSquared() < 1e-8f ) return;

		var candidates = new List<int>();
		instances.Query( origin, delta, 1, candidates );

		foreach ( var i in candidates )
		{
			var localOrigin = N3.Transform( origin, toModel[i] );
			var localDelta = N3.TransformNormal( delta, toModel[i] );
			var model = models[i];

			if ( nearest )
			{
				var t = limit();
				if ( model.Nearest( localOrigin, localDelta, cullMode, ref t, out var triangle, out var u, out var v ) )
					handle( MakeHit( i, triangle, t, u, v, origin, delta ) );
			}
			else
			{
				model.All( localOrigin, localDelta, cullMode, ( triangle, t, u, v ) => handle( MakeHit( i, triangle, t, u, v, origin, delta ) ) );
			}
		}
	}

	Hit MakeHit( int instance, int triangle, float t, float u, float v, N3 origin, N3 delta )
	{
		var model = models[instance];
		var (a, b, c) = model.Corners( triangle );
		var normal = N3.Normalize( N3.TransformNormal( N3.Cross( b - a, c - a ), NMatrix.Transpose( toModel[instance] ) ) );

		// Native's hit position: on the triangle, c + (a - c) u + (b - c) v, placed
		var local = c + (a - c) * u + (b - c) * v;
		var position = N3.Transform( local, toWorld[instance] );
		return new Hit
		{
			Fraction = t,
			Position = new Vector3( position.X, position.Y, position.Z ),
			Normal = new Vector3( normal.X, normal.Y, normal.Z ),
			Triangle = triangle,
			Uv = new Vector2( u, v ),
			Material = model.MaterialOf( triangle ),
			Transform = transforms[instance],
			V0 = new Vector3( a.X, a.Y, a.Z ),
			V1 = new Vector3( b.X, b.Y, b.Z ),
			V2 = new Vector3( c.X, c.Y, c.Z ),
		};
	}

	/// <summary>A model's LOD 0 triangles, as native's <c>CModel::BuildRayTraceObject</c> takes them, in a hierarchy.</summary>
	internal sealed class ModelTriangles
	{
		N3[] corners;
		int[] materialOf;
		string[] materials;
		public Bvh Tree;

		public int Count => corners.Length / 3;

		public (N3, N3, N3) Corners( int triangle ) => (corners[triangle * 3], corners[triangle * 3 + 1], corners[triangle * 3 + 2]);

		public string MaterialOf( int triangle ) => materials[materialOf[triangle]];

		public static ModelTriangles Read( ModelFile file )
		{
			var corners = new List<N3>();
			var materialOf = new List<int>();
			var materials = new List<string>();

			for ( int m = 0; m < file.Meshes.Count; m++ )
			{
				if ( !file.DrawsAtLod( m, 0 ) ) continue;
				var mesh = file.Meshes[m];
				foreach ( var call in mesh.DrawCalls )
				{
					if ( call.Primitive != PrimitiveKind.Triangles || call.IndexCount < 3 || call.VertexBuffers.Count == 0 ) continue;
					var (vbIndex, vbOffset) = call.VertexBuffers[0];
					var (ibIndex, ibOffset) = call.IndexBuffer;
					if ( vbIndex >= mesh.VertexBuffers.Count || ibIndex >= mesh.IndexBuffers.Count ) continue;

					var vertices = mesh.VertexBuffers[vbIndex];
					var indices = mesh.IndexBuffers[ibIndex];
					if ( vertices.Find( "POSITION" ) is not { } position ) continue;

					var material = materials.Count;
					materials.Add( file.MaterialFor( call.Material, 0 ) );

					var stride = Math.Max( vertices.Stride, 1 );
					var firstVertex = vbOffset / stride + call.BaseVertex;
					var size = Math.Max( indices.Size, 1 );
					var data = indices.Data;
					for ( int i = 0; i + 2 < call.IndexCount; i += 3 )
					{
						for ( int k = 0; k < 3; k++ )
						{
							var at = ibOffset + (call.StartIndex + i + k) * size;
							var index = size == 2 ? BitConverter.ToUInt16( data, at ) : (int)BitConverter.ToUInt32( data, at );
							var p = vertices.Read( position, firstVertex + index );
							corners.Add( new N3( p.X, p.Y, p.Z ) );
						}

						materialOf.Add( material );
					}
				}
			}

			return FromCorners( corners.ToArray(), materialOf.ToArray(), materials.ToArray() );
		}

		/// <summary>
		/// Triangles from an indexed mesh with no materials, three indices each, in index order: a model made in code's trace
		/// mesh, as native's <c>BuildRayTraceObject</c> took it. Triangle <c>i</c> is indices <c>3i..3i+2</c>, which is what
		/// <c>PolygonMesh.TriangleToFace</c> reads a hit's triangle by.
		/// </summary>
		public static ModelTriangles FromIndexed( ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices )
		{
			var count = indices.Length / 3;
			var corners = new N3[count * 3];
			for ( int i = 0; i < corners.Length; i++ )
			{
				var p = vertices[indices[i]];
				corners[i] = new N3( p.x, p.y, p.z );
			}

			return FromCorners( corners, new int[count], [null] );
		}

		/// <summary>Triangles from their corners, three each, with each triangle's index into <paramref name="materials"/>.</summary>
		public static ModelTriangles FromCorners( N3[] corners, int[] materialOf, string[] materials )
		{
			var result = new ModelTriangles { corners = corners, materialOf = materialOf, materials = materials };
			var count = result.Count;
			var mins = new N3[count];
			var maxs = new N3[count];
			for ( int t = 0; t < count; t++ )
			{
				var (a, b, c) = result.Corners( t );
				mins[t] = N3.Min( a, N3.Min( b, c ) );
				maxs[t] = N3.Max( a, N3.Max( b, c ) );
			}

			result.Tree = Bvh.Build( mins, maxs );
			return result;
		}

		/// <summary>The nearest triangle the segment crosses before <paramref name="t"/> (a fraction of the segment).</summary>
		public bool Nearest( N3 origin, N3 delta, int cullMode, ref float t, out int triangle, out float u, out float v )
		{
			var candidates = new List<int>();
			Tree.Query( origin, delta, t, candidates );
			triangle = -1;
			u = v = 0;
			foreach ( var i in candidates )
			{
				if ( !Intersect( i, origin, delta, cullMode, out var hit, out var hu, out var hv ) || hit >= t ) continue;
				t = hit;
				triangle = i;
				u = hu;
				v = hv;
			}

			return triangle >= 0;
		}

		/// <summary>Every triangle the segment crosses.</summary>
		public void All( N3 origin, N3 delta, int cullMode, Action<int, float, float, float> each )
		{
			var candidates = new List<int>();
			Tree.Query( origin, delta, 1, candidates );
			foreach ( var i in candidates )
			{
				if ( Intersect( i, origin, delta, cullMode, out var hit, out var hu, out var hv ) ) each( i, hit, hu, hv );
			}
		}

		/// <summary>
		/// Where along the segment it crosses a triangle, with native's barycentrics (<c>u</c> the first corner's weight,
		/// <c>v</c> the second's). Cull mode 1 keeps only back faces, 2 only front faces (the ray against the normal,
		/// <c>RTE_CULL_FRONT</c> and <c>RTE_CULL_BACK</c>).
		/// </summary>
		internal bool Intersect( int triangle, N3 origin, N3 delta, int cullMode, out float t, out float u, out float v )
		{
			t = u = v = 0;
			var (a, b, c) = Corners( triangle );
			var normal = N3.Cross( b - a, c - a );
			var facing = N3.Dot( delta, normal );
			if ( cullMode == 1 && facing <= 0 ) return false;
			if ( cullMode == 2 && facing >= 0 ) return false;

			// Möller-Trumbore, with the weights of a (wa) and b (wb) kept for native's u and v
			var e1 = b - a;
			var e2 = c - a;
			var p = N3.Cross( delta, e2 );
			var det = N3.Dot( e1, p );
			if ( MathF.Abs( det ) < 1e-12f ) return false;
			var inv = 1 / det;
			var s = origin - a;
			var wb = N3.Dot( s, p ) * inv;
			if ( wb < 0 || wb > 1 ) return false;
			var q = N3.Cross( s, e1 );
			var wc = N3.Dot( delta, q ) * inv;
			if ( wc < 0 || wb + wc > 1 ) return false;
			t = N3.Dot( e2, q ) * inv;
			if ( t < 0 || t > 1 ) return false;
			u = 1 - wb - wc;
			v = wb;
			return true;
		}
	}

	/// <summary>A bounding volume hierarchy over boxes, built by splitting at the median along the widest axis.</summary>
	internal sealed class Bvh
	{
		struct Node
		{
			public N3 Min, Max;
			public int Start, Count, Right;
		}

		Node[] nodes;
		int[] order;

		public N3 Min => nodes.Length > 0 ? nodes[0].Min : default;
		public N3 Max => nodes.Length > 0 ? nodes[0].Max : default;

		public static Bvh Build( N3[] mins, N3[] maxs )
		{
			var tree = new Bvh { order = new int[mins.Length] };
			for ( int i = 0; i < mins.Length; i++ ) tree.order[i] = i;
			var nodes = new List<Node>( Math.Max( 1, mins.Length / 2 ) );
			if ( mins.Length > 0 ) tree.Split( nodes, mins, maxs, 0, mins.Length );
			tree.nodes = nodes.ToArray();
			return tree;
		}

		int Split( List<Node> nodes, N3[] mins, N3[] maxs, int start, int count )
		{
			var min = new N3( float.MaxValue );
			var max = new N3( float.MinValue );
			var cmin = new N3( float.MaxValue );
			var cmax = new N3( float.MinValue );
			for ( int i = start; i < start + count; i++ )
			{
				var o = order[i];
				min = N3.Min( min, mins[o] );
				max = N3.Max( max, maxs[o] );
				var centre = (mins[o] + maxs[o]) * 0.5f;
				cmin = N3.Min( cmin, centre );
				cmax = N3.Max( cmax, centre );
			}

			var index = nodes.Count;
			nodes.Add( new Node { Min = min, Max = max, Start = start, Count = count } );
			if ( count <= 4 ) return index;

			var extent = cmax - cmin;
			var axis = extent.X > extent.Y && extent.X > extent.Z ? 0 : extent.Y > extent.Z ? 1 : 2;
			float Centre( int o ) => axis == 0 ? mins[o].X + maxs[o].X : axis == 1 ? mins[o].Y + maxs[o].Y : mins[o].Z + maxs[o].Z;
			Array.Sort( order, start, count, Comparer<int>.Create( ( a, b ) => Centre( a ).CompareTo( Centre( b ) ) ) );

			var half = count / 2;
			Split( nodes, mins, maxs, start, half );
			var right = Split( nodes, mins, maxs, start + half, count - half );
			nodes[index] = new Node { Min = min, Max = max, Start = start, Count = 0, Right = right };
			return index;
		}

		/// <summary>The items whose boxes the segment from <paramref name="origin"/> along <paramref name="delta"/> crosses before <paramref name="limit"/>.</summary>
		public void Query( N3 origin, N3 delta, float limit, List<int> into )
		{
			if ( nodes.Length == 0 ) return;
			static float Inverse( float d ) => 1 / (MathF.Abs( d ) < 1e-20f ? 1e-20f : d);
			var inverse = new N3( Inverse( delta.X ), Inverse( delta.Y ), Inverse( delta.Z ) );
			Span<int> stack = stackalloc int[128];
			var depth = 0;
			stack[depth++] = 0;
			while ( depth > 0 )
			{
				var index = stack[--depth];
				ref readonly var node = ref nodes[index];
				if ( !Crosses( node.Min, node.Max, origin, inverse, limit ) ) continue;

				if ( node.Count > 0 )
				{
					for ( int i = node.Start; i < node.Start + node.Count; i++ ) into.Add( order[i] );
					continue;
				}

				// The left child follows its parent; the right is where the split put it
				if ( depth + 2 > stack.Length ) continue;
				stack[depth++] = node.Right;
				stack[depth++] = index + 1;
			}
		}

		static bool Crosses( N3 min, N3 max, N3 origin, N3 inverse, float limit )
		{
			var t1 = (min - origin) * inverse;
			var t2 = (max - origin) * inverse;
			var near = N3.Min( t1, t2 );
			var far = N3.Max( t1, t2 );
			var enter = MathF.Max( MathF.Max( near.X, near.Y ), MathF.Max( near.Z, 0 ) );
			var exit = MathF.Min( MathF.Min( far.X, far.Y ), MathF.Min( far.Z, limit ) );
			return enter <= exit;
		}

		/// <summary>A box through a row-vector transform, as the box around its corners.</summary>
		public static (N3 Min, N3 Max) Transform( N3 min, N3 max, NMatrix m )
		{
			var outMin = new N3( float.MaxValue );
			var outMax = new N3( float.MinValue );
			for ( int i = 0; i < 8; i++ )
			{
				var corner = new N3( (i & 1) != 0 ? max.X : min.X, (i & 2) != 0 ? max.Y : min.Y, (i & 4) != 0 ? max.Z : min.Z );
				var p = N3.Transform( corner, m );
				outMin = N3.Min( outMin, p );
				outMax = N3.Max( outMax, p );
			}

			return (outMin, outMax);
		}
	}
}
