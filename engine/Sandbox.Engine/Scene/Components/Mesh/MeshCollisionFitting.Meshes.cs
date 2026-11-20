namespace Sandbox;

internal sealed partial class MeshCollisionFitting
{
	internal static void AddSurfaceShape( ModelBuilder builder, MeshComponent.CollisionType collision, List<Vector3> vertices, List<int> corners, byte material, Surface surface, Transform placement, float error )
	{
		var (points, faces) = SimplifySurface( vertices, corners, placement, error );
		var triangleMaterials = collision == MeshComponent.CollisionType.Mesh ? new byte[faces.Length / 3] : [];
		Array.Fill( triangleMaterials, material );
		PolygonMesh.AddCollisionShape( builder, collision, points, faces, triangleMaterials, placement, surface );
	}

	internal static (Vector3[] Points, int[] Indices) SimplifySurface( List<Vector3> vertices, List<int> corners, Transform placement, float error )
	{
		if ( error <= 0 )
		{
			var surface = new List<Vector3>();
			var used = new Dictionary<int, int>();
			var indices = new int[corners.Count];
			for ( int i = 0; i < corners.Count; i++ )
			{
				int index = corners[i];
				if ( !used.TryGetValue( index, out int local ) )
				{
					used.Add( index, local = surface.Count );
					surface.Add( placement.PointToWorld( vertices[index] ) );
				}
				indices[i] = local;
			}
			return (surface.ToArray(), indices);
		}

		var weld = new WeldedVertices();
		var remap = new Dictionary<int, int>();
		var triangles = new List<int>( corners.Count );
		for ( int i = 0; i < corners.Count; i += 3 )
		{
			int a = VertexIndex( corners[i] ), b = VertexIndex( corners[i + 1] ), c = VertexIndex( corners[i + 2] );
			if ( a == b || a == c || b == c )
				continue;
			triangles.Add( a );
			triangles.Add( b );
			triangles.Add( c );
		}

		if ( triangles.Count == 0 ) return ([], []);
		var points = weld.Points.ToArray();
		var faces = triangles.ToArray();
		if ( points.Length >= 4 && faces.Length >= 6 )
			(points, faces) = new MeshCollisionFitting( points, faces, error * error ).Run();
		var world = new Vector3[points.Length];
		for ( int i = 0; i < points.Length; i++ )
			world[i] = new( (float)points[i].X, (float)points[i].Y, (float)points[i].Z );
		return (world, faces);

		int VertexIndex( int index )
		{
			if ( !remap.TryGetValue( index, out int result ) )
				remap.Add( index, result = weld.Add( placement.PointToWorld( vertices[index] ) ) );
			return result;
		}
	}

	sealed class WeldedVertices
	{
		const float Tolerance = 1f / 32f;
		public readonly List<Point> Points = [];
		readonly Dictionary<(int, int, int), List<int>> cells = [];
		public int Add( Vector3 position )
		{
			var cell = ((int)MathF.Floor( position.x / Tolerance ), (int)MathF.Floor( position.y / Tolerance ), (int)MathF.Floor( position.z / Tolerance ));
			for ( int z = -1; z <= 1; z++ )
				for ( int y = -1; y <= 1; y++ )
					for ( int x = -1; x <= 1; x++ )
						if ( cells.TryGetValue( (cell.Item1 + x, cell.Item2 + y, cell.Item3 + z), out var candidates ) )
							foreach ( int i in candidates )
							{
								var p = Points[i];
								if ( Math.Abs( p.X - position.x ) <= Tolerance && Math.Abs( p.Y - position.y ) <= Tolerance && Math.Abs( p.Z - position.z ) <= Tolerance )
									return i;
							}

			int index = Points.Count;
			if ( !cells.TryGetValue( cell, out var bucket ) )
				cells.Add( cell, bucket = [] );
			bucket.Add( index );
			Points.Add( new( position.x, position.y, position.z ) );
			return index;
		}
	}
}
