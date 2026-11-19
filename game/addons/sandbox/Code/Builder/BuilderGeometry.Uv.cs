namespace Sandbox;

public sealed partial class BuilderGeometry
{
	enum UvLayout
	{
		Planar,
		Strip,
		Polar,
		Fan
	}

	readonly record struct Chart( UvLayout Layout, int Start, int Columns, int Rows, bool Closed, Vector3 Axis )
	{
		public int Node( int u, int v ) => Start + v * (Columns + 1) + u;
		public int Count => (Columns + 1) * (Rows + 1);
	}

	readonly List<Chart> charts = new( 16 );
	readonly List<Vector3> chartNodes = new( 2048 );

	readonly List<int> cornerNodes = new( 4096 );

	const int NoNode = -1;

	Vector3[] nodePositions = new Vector3[2048];
	Vector2[] nodeUvs = new Vector2[2048];
	Vector3[] nodeTangents = new Vector3[2048];
	Vector3[] nodeBitangents = new Vector3[2048];
	int[] nodeCharts = new int[2048];
	float[] chartMirror = new float[16];
	float[] rowLengths = new float[64];

	Chart AddChart( UvLayout layout, int columns, int rows, bool closed, Vector3 axis = default )
	{
		var chart = new Chart( layout, chartNodes.Count, columns, rows, closed, axis );
		for ( int i = 0; i < chart.Count; i++ ) chartNodes.Add( default );

		charts.Add( chart );
		return chart;
	}

	void SetNode( in Chart chart, int u, int v, Vector3 point ) => chartNodes[chart.Node( u, v )] = point;

	Vector3 NodePoint( in Chart chart, int u, int v ) => chartNodes[chart.Node( u, v )];

	void ChartQuad( in Chart chart, int u, int v, bool reverse = false )
	{
		int a = chart.Node( u, v ), b = chart.Node( u + 1, v ), c = chart.Node( u + 1, v + 1 ), d = chart.Node( u, v + 1 );
		if ( reverse ) (a, b, c, d) = (d, c, b, a);

		var unwrapped = chart.Layout != UvLayout.Planar;
		ChartTriangle( a, b, c, unwrapped );
		ChartTriangle( a, c, d, unwrapped );
	}

	void ChartTriangle( int a, int b, int c, bool unwrapped )
	{
		Triangle( chartNodes[a], chartNodes[b], chartNodes[c] );
		var corner = cornerNodes.Count - 3;
		if ( !unwrapped ) return;

		cornerNodes[corner] = a;
		cornerNodes[corner + 1] = b;
		cornerNodes[corner + 2] = c;
	}

	void UnwrapCharts( Vector3 scale, Vector3 offset )
	{
		var count = chartNodes.Count;
		if ( count == 0 ) return;

		EnsureNodeBuffers( count );
		for ( int i = 0; i < count; i++ )
		{
			nodePositions[i] = chartNodes[i] * scale + offset;
			nodeTangents[i] = default;
			nodeBitangents[i] = default;
		}

		if ( chartMirror.Length < charts.Count ) chartMirror = new float[charts.Count];
		for ( int c = 0; c < charts.Count; c++ )
		{
			var chart = charts[c];
			chartMirror[c] = 0;
			for ( int i = chart.Start; i < chart.Start + chart.Count; i++ ) nodeCharts[i] = c;

			switch ( chart.Layout )
			{
				case UvLayout.Strip: UnwrapStrip( chart ); break;
				case UvLayout.Polar: UnwrapPolar( chart, (chart.Axis / scale).Normal ); break;
				case UvLayout.Fan: UnwrapFan( chart ); break;
			}
		}

		for ( int t = 0; t * 3 + 2 < cornerNodes.Count; t++ )
		{
			int a = cornerNodes[t * 3], b = cornerNodes[t * 3 + 1], c = cornerNodes[t * 3 + 2];
			if ( a == NoNode || !TryUvFrame( a, b, c, out var tangent, out var bitangent ) ) continue;

			var normal = Vector3.Cross( nodePositions[b] - nodePositions[a], nodePositions[c] - nodePositions[a] );
			chartMirror[nodeCharts[a]] += Vector3.Dot( normal, Vector3.Cross( tangent, bitangent ) );

			AddFrame( a, tangent, bitangent );
			AddFrame( b, tangent, bitangent );
			AddFrame( c, tangent, bitangent );
		}

		for ( int c = 0; c < charts.Count; c++ )
		{
			var chart = charts[c];
			if ( chart.Layout == UvLayout.Polar && chart.Closed ) JoinPolarSeam( chart );

			// Seen from its front, a chart must not come out mirrored compared to flat faces. Strips keep V running
			// down the wall and flip across instead; the other layouts have no up, so V is flipped
			if ( chartMirror[c] <= 0 ) continue;

			var across = chart.Layout == UvLayout.Strip;
			for ( int i = chart.Start; i < chart.Start + chart.Count; i++ )
			{
				if ( across )
				{
					nodeUvs[i].x = -nodeUvs[i].x;
					nodeTangents[i] = -nodeTangents[i];
				}
				else
				{
					nodeUvs[i].y = -nodeUvs[i].y;
					nodeBitangents[i] = -nodeBitangents[i];
				}
			}
		}
	}

	void JoinPolarSeam( in Chart chart )
	{
		for ( int v = 0; v <= chart.Rows; v++ )
		{
			int first = chart.Node( 0, v ), last = chart.Node( chart.Columns, v );
			nodeUvs[last] = nodeUvs[first];
			nodeTangents[first] = nodeTangents[last] = nodeTangents[first] + nodeTangents[last];
			nodeBitangents[first] = nodeBitangents[last] = nodeBitangents[first] + nodeBitangents[last];
		}
	}

	void AddFrame( int node, Vector3 tangent, Vector3 bitangent )
	{
		nodeTangents[node] += tangent;
		nodeBitangents[node] += bitangent;
	}

	void EnsureNodeBuffers( int count )
	{
		if ( nodePositions.Length >= count ) return;

		var size = Math.Max( count, nodePositions.Length * 2 );
		nodePositions = new Vector3[size];
		nodeUvs = new Vector2[size];
		nodeTangents = new Vector3[size];
		nodeBitangents = new Vector3[size];
		nodeCharts = new int[size];
	}

	bool TryUvFrame( int a, int b, int c, out Vector3 tangent, out Vector3 bitangent )
	{
		var e1 = nodePositions[b] - nodePositions[a];
		var e2 = nodePositions[c] - nodePositions[a];
		var d1 = nodeUvs[b] - nodeUvs[a];
		var d2 = nodeUvs[c] - nodeUvs[a];

		var determinant = d1.x * d2.y - d2.x * d1.y;
		if ( MathF.Abs( determinant ) < 0.000001f )
		{
			tangent = bitangent = default;
			return false;
		}

		tangent = (e1 * d2.y - e2 * d1.y) / determinant;
		bitangent = (e2 * d1.x - e1 * d2.x) / determinant;
		return true;
	}

	bool TryChartVertex( int corner, Vector3 normal, out Vector2 uv, out Vector3 tangent, out float sign )
	{
		uv = default;
		tangent = default;
		sign = -1;

		var node = cornerNodes[corner];
		if ( node == NoNode ) return false;

		tangent = nodeTangents[node] - normal * Vector3.Dot( nodeTangents[node], normal );
		if ( tangent.IsNearZeroLength ) return false;

		tangent = tangent.Normal;
		sign = Vector3.Dot( Vector3.Cross( normal, tangent ), nodeBitangents[node] ) < 0 ? -1 : 1;
		uv = nodeUvs[node] / TextureUnit;
		return true;
	}

	void UnwrapStrip( in Chart chart )
	{
		var rows = chart.Rows;
		var columns = chart.Columns;
		if ( rowLengths.Length < rows + 1 ) rowLengths = new float[rows + 1];

		var centres = chart.Closed ? columns : columns + 1;
		var previous = Vector3.Zero;
		for ( int v = 0; v <= rows; v++ )
		{
			var centre = Vector3.Zero;
			for ( int u = 0; u < centres; u++ ) centre += nodePositions[chart.Node( u, v )];
			centre /= centres;

			rowLengths[v] = v == 0 ? 0 : rowLengths[v - 1] - centre.Distance( previous );
			previous = centre;
		}

		// Across: the length around each row, centred so rows of different lengths taper evenly
		for ( int v = 0; v <= rows; v++ )
		{
			var around = 0f;
			nodeUvs[chart.Node( 0, v )] = new Vector2( 0, rowLengths[v] );
			for ( int u = 1; u <= columns; u++ )
			{
				around += nodePositions[chart.Node( u, v )].Distance( nodePositions[chart.Node( u - 1, v )] );
				nodeUvs[chart.Node( u, v )] = new Vector2( around, rowLengths[v] );
			}

			for ( int u = 0; u <= columns; u++ ) nodeUvs[chart.Node( u, v )].x -= around * 0.5f;
		}
	}

	void UnwrapPolar( in Chart chart, Vector3 axis )
	{
		var rows = chart.Rows;
		var pole = nodePositions[chart.Node( 0, rows )];

		var angle = 0f;
		var previous = Vector3.Zero;
		for ( int u = 0; u <= chart.Columns; u++ )
		{
			// The angle around the axis, measured on the rim so oval domes keep their real directions
			var direction = nodePositions[chart.Node( u, 0 )] - pole;
			direction = (direction - axis * Vector3.Dot( direction, axis )).Normal;
			if ( u > 0 ) angle += MathF.Atan2( Vector3.Dot( Vector3.Cross( previous, direction ), axis ), Vector3.Dot( previous, direction ) );
			previous = direction;

			var ray = new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) );
			var distance = 0f;
			nodeUvs[chart.Node( u, rows )] = Vector2.Zero;
			for ( int v = rows - 1; v >= 0; v-- )
			{
				distance += nodePositions[chart.Node( u, v )].Distance( nodePositions[chart.Node( u, v + 1 )] );
				nodeUvs[chart.Node( u, v )] = ray * distance;
			}
		}
	}

	void UnwrapFan( in Chart chart )
	{
		var rows = chart.Rows;
		var apex = nodePositions[chart.Node( 0, rows )];

		var angle = 0f;
		var previousRim = nodePositions[chart.Node( 0, 0 )];
		for ( int u = 0; u <= chart.Columns; u++ )
		{
			var rim = nodePositions[chart.Node( u, 0 )];
			if ( u > 0 )
			{
				var a = previousRim - apex;
				var b = rim - apex;
				angle += MathF.Atan2( Vector3.Cross( a, b ).Length, Vector3.Dot( a, b ) );
			}
			previousRim = rim;

			var ray = new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) );
			for ( int v = 0; v <= rows; v++ )
				nodeUvs[chart.Node( u, v )] = ray * nodePositions[chart.Node( u, v )].Distance( apex );
		}
	}
}
