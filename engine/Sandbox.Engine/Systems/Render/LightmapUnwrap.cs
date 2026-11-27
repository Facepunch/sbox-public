namespace Sandbox.Rendering;

/// <summary>
/// Lightmap coordinates for any triangles. Neighbours facing nearly the same way share a flat chart, so a wall of many
/// faces is one chart with no seams, and the charts are packed in rows with a luxel of padding around each.
/// </summary>
internal static class LightmapUnwrap
{
	// Within 45 degrees of the triangle a chart grew from, so nothing is stretched by more than 1.41
	const float ChartCone = 0.7071f;

	/// <summary>
	/// Triangles split into charts, whatever size a luxel ends up: each corner's place in its chart in units, the chart
	/// each triangle is in, -1 for one with no area, and how far each chart reaches.
	/// </summary>
	public sealed record Charts( Vector2[] Uvs, int[] Chart, Vector2[] Extents );

	/// <summary>
	/// Where each corner is in the lightmap, in luxels of a size, and the size the charts fill.
	/// </summary>
	public static Vector2[] Pack( Charts charts, float luxelSize, out Vector2Int size )
	{
		var placed = ShelfPack( Sizes( charts, luxelSize ), out size );
		var uvs = new Vector2[charts.Uvs.Length];

		for ( int i = 0; i < uvs.Length; i++ )
		{
			var chart = charts.Chart[i / 3];
			if ( chart >= 0 ) uvs[i] = charts.Uvs[i] / luxelSize + new Vector2( 1, 1 ) + placed[chart];
		}

		return uvs;
	}

	/// <summary>
	/// The size charts fill in luxels of a size, without placing any corner.
	/// </summary>
	public static Vector2Int Size( Charts charts, float luxelSize )
	{
		ShelfPack( Sizes( charts, luxelSize ), out var size );
		return size;
	}

	static Vector2Int[] Sizes( Charts charts, float luxelSize ) => Array.ConvertAll( charts.Extents,
		x => new Vector2Int( (int)MathF.Ceiling( x.x / luxelSize ) + 2, (int)MathF.Ceiling( x.y / luxelSize ) + 2 ) );

	/// <summary>
	/// Split triangles, three corners each in the units a luxel is measured in, into charts. It's the slow part of
	/// unwrapping, and the same for any luxel size. Triangles in a row with the same group number never split between charts.
	/// </summary>
	// A surface that spirals over itself inside one chart overlaps there. Split charts on overlap if a ramp bakes wrong
	public static Charts Split( ReadOnlySpan<Vector3> corners, ReadOnlySpan<int> groups = default )
	{
		var count = corners.Length / 3;
		var normals = new Vector3[count];
		var areas = new float[count];

		for ( int t = 0; t < count; t++ )
		{
			var cross = Vector3.Cross( corners[t * 3 + 1] - corners[t * 3], corners[t * 3 + 2] - corners[t * 3] );
			areas[t] = cross.Length > 1e-9f ? cross.Length : 0;
			normals[t] = areas[t] > 0 ? cross / areas[t] : default;
		}

		// Which triangle is across each edge
		var neighbours = new int[count * 3];
		Array.Fill( neighbours, -1 );

		var points = new Dictionary<(int, int, int), int>();
		var edges = new Dictionary<(int, int), int>();

		int Point( Vector3 p )
		{
			var key = (BitConverter.SingleToInt32Bits( p.x ), BitConverter.SingleToInt32Bits( p.y ), BitConverter.SingleToInt32Bits( p.z ));
			if ( !points.TryGetValue( key, out var id ) ) points[key] = id = points.Count;
			return id;
		}

		for ( int i = 0; i < count * 3; i++ )
		{
			var a = Point( corners[i] );
			var b = Point( corners[i - i % 3 + (i + 1) % 3] );
			var key = a < b ? (a, b) : (b, a);

			if ( edges.TryGetValue( key, out var other ) )
			{
				neighbours[i] = other / 3;
				neighbours[other] = i / 3;
			}
			else
			{
				edges[key] = i;
			}
		}

		var group = groups.ToArray();
		var chart = new int[count];
		Array.Fill( chart, -1 );

		var uvs = new Vector2[count * 3];
		var extents = new List<Vector2>();
		var members = new List<int>();

		// Grown from the biggest triangles first, so charts follow the large flat surfaces
		foreach ( var seed in Enumerable.Range( 0, count ).OrderByDescending( t => areas[t] ) )
		{
			if ( chart[seed] >= 0 || areas[seed] == 0 ) continue;

			var id = extents.Count;
			var normal = normals[seed];
			members.Clear();

			void Join( int t )
			{
				// With the rest of its group
				var first = t;
				var last = t;
				if ( group.Length > 0 )
				{
					while ( first > 0 && group[first - 1] == group[t] ) first--;
					while ( last < count - 1 && group[last + 1] == group[t] ) last++;
				}

				for ( int i = first; i <= last; i++ )
				{
					if ( chart[i] >= 0 ) continue;
					chart[i] = id;
					members.Add( i );
				}
			}

			Join( seed );

			for ( int i = 0; i < members.Count; i++ )
			{
				for ( int e = 0; e < 3; e++ )
				{
					var next = neighbours[members[i] * 3 + e];
					if ( next >= 0 && chart[next] < 0 && areas[next] > 0 && Vector3.Dot( normals[next], normal ) >= ChartCone )
						Join( next );
				}
			}

			// Laid flat along whichever of the seed's edges makes the smallest rectangle
			Vector3 bestU = default, bestV = default;
			Vector2 bestMins = default, bestMaxs = default;
			var bestArea = float.MaxValue;

			for ( int e = 0; e < 3; e++ )
			{
				var edge = corners[seed * 3 + (e + 1) % 3] - corners[seed * 3 + e];
				edge -= normal * Vector3.Dot( edge, normal );
				if ( edge.Length < 1e-6f ) continue;

				var u = edge.Normal;
				var v = Vector3.Cross( normal, u );
				var mins = new Vector2( float.MaxValue, float.MaxValue );
				var maxs = new Vector2( float.MinValue, float.MinValue );

				foreach ( var t in members )
				{
					for ( int k = 0; k < 3; k++ )
					{
						var p = new Vector2( Vector3.Dot( corners[t * 3 + k], u ), Vector3.Dot( corners[t * 3 + k], v ) );
						mins = Vector2.Min( mins, p );
						maxs = Vector2.Max( maxs, p );
					}
				}

				var area = (maxs.x - mins.x) * (maxs.y - mins.y);
				if ( area >= bestArea ) continue;

				// Lying down, rows pack tighter
				var wide = maxs.x - mins.x >= maxs.y - mins.y;
				bestArea = area;
				bestU = wide ? u : v;
				bestV = wide ? v : u;
				bestMins = wide ? mins : new Vector2( mins.y, mins.x );
				bestMaxs = wide ? maxs : new Vector2( maxs.y, maxs.x );
			}

			foreach ( var t in members )
			{
				for ( int k = 0; k < 3; k++ )
				{
					var p = new Vector2( Vector3.Dot( corners[t * 3 + k], bestU ), Vector3.Dot( corners[t * 3 + k], bestV ) );
					uvs[t * 3 + k] = p - bestMins;
				}
			}

			extents.Add( bestMaxs - bestMins );
		}

		return new Charts( uvs, chart, extents.ToArray() );
	}

	/// <summary>
	/// Pack rectangles in rows, tallest first. Returns each one's corner, and the size they fill.
	/// </summary>
	// Shelf packing leaves gaps under short rectangles. Use a skyline packer if atlases run out of room
	public static Vector2Int[] ShelfPack( IReadOnlyList<Vector2Int> sizes, out Vector2Int size )
	{
		var corners = new Vector2Int[sizes.Count];

		var width = Math.Max( sizes.Select( s => s.x ).Append( 1 ).Max(), (int)MathF.Ceiling( MathF.Sqrt( sizes.Sum( s => (long)s.x * s.y ) ) ) );

		int x = 0, y = 0, rowHeight = 0, used = 0;
		foreach ( var i in Enumerable.Range( 0, sizes.Count ).OrderByDescending( i => sizes[i].y ) )
		{
			if ( x + sizes[i].x > width )
			{
				x = 0;
				y += rowHeight;
				rowHeight = 0;
			}

			corners[i] = new Vector2Int( x, y );
			x += sizes[i].x;
			rowHeight = Math.Max( rowHeight, sizes[i].y );
			used = Math.Max( used, x );
		}

		size = new Vector2Int( used, y + rowHeight );
		return corners;
	}
}
