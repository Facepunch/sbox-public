using System.Numerics;

namespace Nav.Build;

/// <summary>
/// Where the field sits in the world.
/// </summary>
internal struct FieldFrame
{
	public float MinX, MinY, MinZ;
	public float MaxX, MaxY;
	public float CellSize, InverseCellSize, InverseCellHeight;
}

internal static class Rasterizer
{
	public const int MaxClipVerts = 8;

	/// <summary>
	/// Clips the triangle to every cell it touches and adds the height range of each piece as a span.
	/// The triangle is cut into rows first, then each row into columns, and the cut points of one
	/// cell are the inputs of the next, so the heights depend on that order.
	/// </summary>
	public static void Triangle( SolidField field, in FieldFrame frame, Vector3 v0, Vector3 v1, Vector3 v2, int area, int mergeThreshold )
	{
		var triangleMin = Vector3.Min( Vector3.Min( v0, v1 ), v2 );
		var triangleMax = Vector3.Max( Vector3.Max( v0, v1 ), v2 );

		int w = field.Width;
		int h = field.Height;
		float cellSize = frame.CellSize;
		float inverseCellSize = frame.InverseCellSize;

		int y0 = (int)MathF.Floor( (triangleMin.Y - frame.MinY) * inverseCellSize );
		int y1 = (int)MathF.Floor( (triangleMax.Y - frame.MinY) * inverseCellSize );

		// Row -1 cuts off what lies before the field
		y0 = Math.Clamp( y0, -1, h - 1 );
		y1 = Math.Clamp( y1, 0, h - 1 );

		int flatMin = 0, flatMax = 0;
		bool flat = FlatSpan( frame, triangleMin.Z, triangleMax.Z, w + h, out flatMin, out flatMax );

		Span<Vector3> buffer = stackalloc Vector3[MaxClipVerts * 4];
		var rest = buffer.Slice( 0, MaxClipVerts );
		var row = buffer.Slice( MaxClipVerts, MaxClipVerts );
		var a = buffer.Slice( MaxClipVerts * 2, MaxClipVerts );
		var b = buffer.Slice( MaxClipVerts * 3, MaxClipVerts );

		rest[0] = v0;
		rest[1] = v1;
		rest[2] = v2;
		int restCount = 3;

		for ( int y = y0; y <= y1; y++ )
		{
			float rowEnd = frame.MinY + y * cellSize + cellSize;
			DivideY( rest, restCount, row, out int rowCount, a, out restCount, rowEnd );
			var swap = rest;
			rest = a;
			a = swap;

			if ( rowCount < 3 || y < 0 ) continue;

			float minX = row[0].X, maxX = row[0].X;
			for ( int i = 1; i < rowCount; i++ )
			{
				minX = MathF.Min( minX, row[i].X );
				maxX = MathF.Max( maxX, row[i].X );
			}

			int x0 = (int)MathF.Floor( (minX - frame.MinX) * inverseCellSize );
			int x1 = (int)MathF.Floor( (maxX - frame.MinX) * inverseCellSize );
			if ( x1 < 0 || x0 >= w ) continue;

			x0 = Math.Clamp( x0, -1, w - 1 );
			x1 = Math.Clamp( x1, 0, w - 1 );

			if ( flat ) FlatRow( field, frame, row, rowCount, y * w, x0, x1, flatMin, flatMax, area, mergeThreshold );
			else SlopedRow( field, frame, row, rowCount, y * w, x0, x1, area, mergeThreshold );
		}
	}

	// Cuts a row into columns. Only x and z of the points matter from here on, and of a cell's piece
	// only its lowest and highest point.
	public static void SlopedRow( SolidField field, in FieldFrame frame, Span<Vector3> row, int rowCount, int rowStart, int x0, int x1, int area, int mergeThreshold )
	{
		Span<Vector2> buffer = stackalloc Vector2[MaxClipVerts * 2];
		var rest = buffer.Slice( 0, MaxClipVerts );
		var next = buffer.Slice( MaxClipVerts, MaxClipVerts );
		for ( int i = 0; i < rowCount; i++ ) rest[i] = new Vector2( row[i].X, row[i].Z );
		int restCount = rowCount;

		float cellSize = frame.CellSize;
		for ( int x = x0; x <= x1; x++ )
		{
			float columnEnd = frame.MinX + x * cellSize + cellSize;
			int count = 0, nextCount = 0;
			float min = float.PositiveInfinity, max = float.NegativeInfinity;
			if ( restCount > 0 )
			{
				var previous = rest[restCount - 1];
				float previousDelta = columnEnd - previous.X;
				for ( int i = 0; i < restCount; i++ )
				{
					var current = rest[i];
					float delta = columnEnd - current.X;
					if ( (delta >= 0) != (previousDelta >= 0) )
					{
						float s = previousDelta / (previousDelta - delta);
						var cut = previous + (current - previous) * s;
						next[nextCount++] = cut;
						count++;
						min = MathF.Min( min, cut.Y );
						max = MathF.Max( max, cut.Y );
						if ( delta > 0 )
						{
							count++;
							min = MathF.Min( min, current.Y );
							max = MathF.Max( max, current.Y );
						}
						else if ( delta < 0 )
						{
							next[nextCount++] = current;
						}
					}
					else if ( delta >= 0 )
					{
						count++;
						min = MathF.Min( min, current.Y );
						max = MathF.Max( max, current.Y );
						if ( delta == 0 ) next[nextCount++] = current;
					}
					else
					{
						next[nextCount++] = current;
					}

					previous = current;
					previousDelta = delta;
				}
			}

			var swap = rest;
			rest = next;
			next = swap;
			restCount = nextCount;

			if ( count < 3 || x < 0 ) continue;

			int spanMin = Math.Clamp( (int)MathF.Floor( (min - frame.MinZ) * frame.InverseCellHeight ), 0, BuildConfig.MaxSpanHeight - 1 );
			int spanMax = Math.Clamp( (int)MathF.Ceiling( (max - frame.MinZ) * frame.InverseCellHeight ), spanMin + 1, BuildConfig.MaxSpanHeight );
			field.Add( rowStart + x, spanMin, spanMax, area, mergeThreshold );
		}
	}

	/// <summary>
	/// A triangle that stays inside one cell height gives every cell the same span, so only the cells
	/// at the ends of a row, where its outline decides whether a piece exists at all, need clipping.
	/// </summary>
	static bool FlatSpan( in FieldFrame frame, float minZ, float maxZ, int cuts, out int spanMin, out int spanMax )
	{
		float low = (minZ - frame.MinZ) * frame.InverseCellHeight;
		float high = (maxZ - frame.MinZ) * frame.InverseCellHeight;
		if ( minZ != maxZ )
		{
			// Every cut may move a height by a rounding step. Stay clear of the cell faces by that much.
			float drift = MathF.Max( MathF.Abs( minZ ), MathF.Abs( maxZ ) ) * (cuts + 8) * (1f / (1 << 22));
			low = (minZ - drift - frame.MinZ) * frame.InverseCellHeight;
			high = (maxZ + drift - frame.MinZ) * frame.InverseCellHeight;
			if ( MathF.Floor( low ) != MathF.Floor( high ) || MathF.Ceiling( low ) != MathF.Ceiling( high ) )
			{
				spanMin = spanMax = 0;
				return false;
			}
		}

		spanMin = Math.Clamp( (int)MathF.Floor( low ), 0, BuildConfig.MaxSpanHeight - 1 );
		spanMax = Math.Clamp( (int)MathF.Ceiling( high ), spanMin + 1, BuildConfig.MaxSpanHeight );
		return true;
	}

	// A cut point of a flat row. Its x is that of the last column line, left of every line still to come.
	const float Cut = float.NegativeInfinity;

	// A flat row only has to find out which columns get a piece of three or more points. That is
	// decided by which side of each column line the row's vertices are on, so the cutting is done on
	// their x alone and the cut points are not computed.
	static void FlatRow( SolidField field, in FieldFrame frame, Span<Vector3> row, int rowCount, int rowStart, int x0, int x1, int spanMin, int spanMax, int area, int mergeThreshold )
	{
		Span<float> buffer = stackalloc float[MaxClipVerts * 2];
		var rest = buffer.Slice( 0, MaxClipVerts );
		var next = buffer.Slice( MaxClipVerts, MaxClipVerts );
		for ( int i = 0; i < rowCount; i++ ) rest[i] = row[i].X;
		int restCount = rowCount;

		float cellSize = frame.CellSize;
		int x = x0;
		while ( x <= x1 )
		{
			float columnEnd = frame.MinX + x * cellSize + cellSize;
			int count = 0, nextCount = 0;
			if ( restCount > 0 )
			{
				float previousDelta = columnEnd - rest[restCount - 1];
				for ( int i = 0; i < restCount; i++ )
				{
					float current = rest[i];
					float delta = columnEnd - current;
					if ( (delta >= 0) != (previousDelta >= 0) )
					{
						next[nextCount++] = Cut;
						count++;
						if ( delta > 0 ) count++;
						else if ( delta < 0 ) next[nextCount++] = current;
					}
					else if ( delta >= 0 )
					{
						count++;
						if ( delta == 0 ) next[nextCount++] = current;
					}
					else
					{
						next[nextCount++] = current;
					}
					previousDelta = delta;
				}
			}

			var swap = rest;
			rest = next;
			next = swap;
			restCount = nextCount;

			if ( count >= 3 && x >= 0 ) field.Add( rowStart + x, spanMin, spanMax, area, mergeThreshold );
			x++;

			// What is left is two cut points and, in one stretch, the vertices ahead. While those are two
			// columns or more away every column cuts it into the same thing again: a piece of four points.
			float nearest = float.PositiveInfinity;
			int cuts = 0, changes = 0;
			bool previousCut = restCount > 0 && rest[restCount - 1] == Cut;
			for ( int i = 0; i < restCount; i++ )
			{
				bool cut = rest[i] == Cut;
				if ( cut ) cuts++;
				else nearest = MathF.Min( nearest, rest[i] );
				if ( cut != previousCut ) changes++;
				previousCut = cut;
			}
			if ( cuts != 2 || changes != 2 ) continue;

			int ahead = Math.Min( (int)MathF.Floor( (nearest - frame.MinX) * frame.InverseCellSize ) - 2, x1 );
			for ( int c = Math.Max( x, 0 ); c <= ahead; c++ ) field.Add( rowStart + c, spanMin, spanMax, area, mergeThreshold );
			x = Math.Max( x, ahead + 1 );
		}
	}

	// Splits a convex polygon at y = offset. A vertex on the line goes to both sides.
	public static void DivideY( Span<Vector3> source, int count, Span<Vector3> below, out int belowCount, Span<Vector3> above, out int aboveCount, float offset )
	{
		int nb = 0, na = 0;
		if ( count == 0 )
		{
			belowCount = aboveCount = 0;
			return;
		}

		var previous = source[count - 1];
		float previousDelta = offset - previous.Y;
		for ( int i = 0; i < count; i++ )
		{
			var current = source[i];
			float delta = offset - current.Y;
			if ( (delta >= 0) != (previousDelta >= 0) )
			{
				float s = previousDelta / (previousDelta - delta);
				var cut = previous + (current - previous) * s;
				below[nb++] = cut;
				above[na++] = cut;
				if ( delta > 0 ) below[nb++] = current;
				else if ( delta < 0 ) above[na++] = current;
			}
			else if ( delta >= 0 )
			{
				below[nb++] = current;
				if ( delta == 0 ) above[na++] = current;
			}
			else
			{
				above[na++] = current;
			}

			previous = current;
			previousDelta = delta;
		}

		belowCount = nb;
		aboveCount = na;
	}
}
