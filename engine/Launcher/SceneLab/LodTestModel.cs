namespace Sandbox.SceneLab;

/// <summary>
/// A model built at runtime whose LODs are different shapes - a sphere, then a cube, then an
/// octahedron - so which LOD an object drew with is obvious at a glance, and any disagreement with
/// native shows up in a parity check as a different silhouette. Core has no static model with LODs.
/// </summary>
internal static class LodTestModel
{
	/// <summary>
	/// What Scene Lab scenes name it by in place of a model path.
	/// </summary>
	public const string Path = "procedural/lod_test";

	const float Size = 20;

	public static Model Create()
	{
		var material = Material.Load( "materials/dev/primary_white.vmat" );

		return Model.Builder
			.WithName( "lod_test" )
			.AddMesh( Sphere( material ), 0 )
			.AddMesh( Cube( material ), 1 )
			.AddMesh( Octahedron( material ), 2 )
			// The LOD metric is 50 over the width in pixels of a half unit sphere - bigger is further
			.WithLodDistance( 1, 60 )
			.WithLodDistance( 2, 150 )
			.Create();
	}

	static Mesh Build( Material material, List<Vertex> vertices, List<int> indices )
	{
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = BBox.FromPositionAndSize( Vector3.Zero, Size * 2 );
		return mesh;
	}

	static Vertex MakeVertex( Vector3 position, Vector3 normal )
	{
		var tangent = MathF.Abs( normal.z ) < 0.9f ? Vector3.Cross( Vector3.Up, normal ).Normal : Vector3.Forward;
		return new Vertex( position, normal, tangent, Vector4.Zero ) { Color = Color32.White };
	}

	static Mesh Sphere( Material material )
	{
		const int rings = 16, segments = 32;
		var vertices = new List<Vertex>();
		var indices = new List<int>();

		for ( int r = 0; r <= rings; r++ )
		{
			var phi = MathF.PI * r / rings;
			for ( int s = 0; s <= segments; s++ )
			{
				var theta = MathF.Tau * s / segments;
				var normal = new Vector3( MathF.Sin( phi ) * MathF.Cos( theta ), MathF.Sin( phi ) * MathF.Sin( theta ), MathF.Cos( phi ) );
				vertices.Add( MakeVertex( normal * Size, normal ) );
			}
		}

		for ( int r = 0; r < rings; r++ )
		{
			for ( int s = 0; s < segments; s++ )
			{
				int a = r * (segments + 1) + s, b = a + segments + 1;
				indices.AddRange( [a, b, a + 1, a + 1, b, b + 1] );
			}
		}

		return Build( material, vertices, indices );
	}

	static Mesh Cube( Material material )
	{
		var vertices = new List<Vertex>();
		var indices = new List<int>();
		Vector3[] normals = [Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down];

		foreach ( var normal in normals )
		{
			// Two axes across the face, ordered so the winding faces out
			var u = MathF.Abs( normal.z ) > 0.5f ? Vector3.Forward : Vector3.Up;
			var v = Vector3.Cross( normal, u );
			var start = vertices.Count;

			vertices.Add( MakeVertex( (normal - u - v) * Size * 0.9f, normal ) );
			vertices.Add( MakeVertex( (normal + u - v) * Size * 0.9f, normal ) );
			vertices.Add( MakeVertex( (normal + u + v) * Size * 0.9f, normal ) );
			vertices.Add( MakeVertex( (normal - u + v) * Size * 0.9f, normal ) );
			indices.AddRange( [start, start + 1, start + 2, start, start + 2, start + 3] );
		}

		return Build( material, vertices, indices );
	}

	static Mesh Octahedron( Material material )
	{
		var vertices = new List<Vertex>();
		var indices = new List<int>();
		Vector3[] axes = [Vector3.Forward, Vector3.Left, Vector3.Backward, Vector3.Right];

		for ( int i = 0; i < 4; i++ )
		{
			foreach ( var pole in new[] { Vector3.Up, Vector3.Down } )
			{
				var a = axes[i] * Size;
				var b = axes[(i + 1) % 4] * Size;
				var c = pole * Size;
				var normal = Vector3.Cross( b - a, c - a ).Normal;

				// Face out, whichever pole this is
				if ( Vector3.Dot( normal, a + b + c ) < 0 ) (a, b) = (b, a);
				normal = Vector3.Cross( b - a, c - a ).Normal;

				var start = vertices.Count;
				vertices.Add( MakeVertex( a, normal ) );
				vertices.Add( MakeVertex( b, normal ) );
				vertices.Add( MakeVertex( c, normal ) );
				indices.AddRange( [start, start + 1, start + 2] );
			}
		}

		return Build( material, vertices, indices );
	}
}
