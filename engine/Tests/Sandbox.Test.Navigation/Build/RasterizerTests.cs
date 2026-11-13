using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nav.Build;

namespace Nav.Lib.Tests.Build;

[TestClass]
public class RasterizerTests
{
	const int Size = 48;
	const float Cell = 4, CellHeight = 4;

	static FieldFrame Frame( float minX = -32, float minY = 16, float minZ = -40 ) => new()
	{
		MinX = minX,
		MinY = minY,
		MinZ = minZ,
		MaxX = minX + Size * Cell,
		MaxY = minY + Size * Cell,
		CellSize = Cell,
		InverseCellSize = 1 / Cell,
		InverseCellHeight = 1 / CellHeight
	};

	// The row loop of Rasterizer.Triangle with every row cut by the plain clipper, which is what the
	// shortcut for flat triangles has to equal.
	static void PlainTriangle( SolidField field, in FieldFrame frame, Vector3 v0, Vector3 v1, Vector3 v2, int area, int mergeThreshold )
	{
		var triangleMin = Vector3.Min( Vector3.Min( v0, v1 ), v2 );
		var triangleMax = Vector3.Max( Vector3.Max( v0, v1 ), v2 );
		int w = field.Width, h = field.Height;
		int y0 = Math.Clamp( (int)MathF.Floor( (triangleMin.Y - frame.MinY) * frame.InverseCellSize ), -1, h - 1 );
		int y1 = Math.Clamp( (int)MathF.Floor( (triangleMax.Y - frame.MinY) * frame.InverseCellSize ), 0, h - 1 );

		const int max = Rasterizer.MaxClipVerts;
		Span<Vector3> buffer = stackalloc Vector3[max * 3];
		var rest = buffer.Slice( 0, max );
		var row = buffer.Slice( max, max );
		var other = buffer.Slice( max * 2, max );
		rest[0] = v0;
		rest[1] = v1;
		rest[2] = v2;
		int restCount = 3;

		for ( int y = y0; y <= y1; y++ )
		{
			float rowEnd = frame.MinY + y * frame.CellSize + frame.CellSize;
			Rasterizer.DivideY( rest, restCount, row, out int rowCount, other, out restCount, rowEnd );
			var swap = rest;
			rest = other;
			other = swap;
			if ( rowCount < 3 || y < 0 ) continue;

			float minX = row[0].X, maxX = row[0].X;
			for ( int i = 1; i < rowCount; i++ )
			{
				minX = MathF.Min( minX, row[i].X );
				maxX = MathF.Max( maxX, row[i].X );
			}

			int x0 = (int)MathF.Floor( (minX - frame.MinX) * frame.InverseCellSize );
			int x1 = (int)MathF.Floor( (maxX - frame.MinX) * frame.InverseCellSize );
			if ( x1 < 0 || x0 >= w ) continue;
			Rasterizer.SlopedRow( field, frame, row, rowCount, y * w, Math.Clamp( x0, -1, w - 1 ), Math.Clamp( x1, 0, w - 1 ), area, mergeThreshold );
		}
	}

	// The spans the triangles leave with the flat path against the spans of the plain clipper.
	static void AssertSameSpans( in FieldFrame frame, Vector3[] triangles, string what )
	{
		var fast = new SolidField();
		var plain = new SolidField();
		fast.Reset( Size, Size );
		plain.Reset( Size, Size );
		for ( int t = 0; t < triangles.Length; t += 3 )
		{
			int area = t / 3 % 3 == 0 ? 0 : 1;
			Rasterizer.Triangle( fast, frame, triangles[t], triangles[t + 1], triangles[t + 2], area, 4 );
			PlainTriangle( plain, frame, triangles[t], triangles[t + 1], triangles[t + 2], area, 4 );
		}

		Assert.AreEqual( plain.SpanCount, fast.SpanCount, $"{what}: span count" );
		for ( int column = 0; column < Size * Size; column++ )
		{
			Assert.AreEqual( plain.Counts[column], fast.Counts[column], $"{what}: spans in column {column % Size}, {column / Size}" );
			for ( int i = 0; i < plain.Counts[column]; i++ )
				Assert.AreEqual( plain.Spans[column * plain.Capacity + i], fast.Spans[column * fast.Capacity + i], $"{what}: span {i} of column {column % Size}, {column / Size}" );
		}
	}

	[TestMethod]
	public void FlatPathMatchesClipperOnRandomTriangles()
	{
		var random = new Random( 7 );
		var frame = Frame();
		for ( int round = 0; round < 400; round++ )
		{
			var triangles = new Vector3[3 * 6];
			for ( int t = 0; t < triangles.Length; t += 3 )
			{
				// Exactly flat, nearly flat inside one cell, nearly flat across a cell face, or sloped
				int kind = random.Next( 4 );
				float z = kind == 2 ? random.Next( -8, 8 ) * CellHeight : random.NextSingle() * 60 - 30;
				float spread = kind switch { 0 => 0, 1 => 0.5f, 2 => 0.01f, _ => 20 };
				float reach = random.Next( 3 ) == 0 ? 400 : 60;
				for ( int v = 0; v < 3; v++ )
					triangles[t + v] = new Vector3( frame.MinX + random.NextSingle() * reach - 30, frame.MinY + random.NextSingle() * reach - 30, z + (random.NextSingle() - 0.5f) * spread );
			}
			AssertSameSpans( frame, triangles, $"round {round}" );
		}
	}

	// Edges and corners exactly on cell boundaries are where a shortcut goes wrong: the clipper leaves the
	// cell beyond such an edge empty, and a vertex on a boundary belongs to both sides.
	[TestMethod]
	public void FlatPathMatchesClipperOnGridAlignedShapes()
	{
		var random = new Random( 11 );
		foreach ( var frame in new[] { Frame(), Frame( 0, 0, 0 ), Frame( -1024, 2048, -8 ), Frame( 3.5f, -7.25f, -3 ) } )
		{
			for ( int round = 0; round < 300; round++ )
			{
				var triangles = new List<Vector3>();
				for ( int shape = 0; shape < 4; shape++ )
				{
					// Corners on cell corners, sometimes outside the field, at heights on and off the height grid
					int x0 = random.Next( -4, Size + 2 ), y0 = random.Next( -4, Size + 2 );
					int x1 = x0 + random.Next( 0, 30 ), y1 = y0 + random.Next( 0, 30 );
					float z = random.Next( 3 ) == 0 ? frame.MinZ + random.Next( 0, 20 ) * CellHeight : frame.MinZ + random.NextSingle() * 60;
					Vector3 a = Corner( frame, x0, y0, z ), b = Corner( frame, x1, y0, z ), c = Corner( frame, x1, y1, z ), d = Corner( frame, x0, y1, z );
					switch ( random.Next( 4 ) )
					{
						case 0:
							triangles.AddRange( [a, b, c, a, c, d] );
							break;
						case 1:
							triangles.AddRange( [a, c, b, b, d, c] );
							break;
						case 2:
							triangles.AddRange( [a, b, d] );
							break;
						default:
							// A diagonal edge through cell corners
							triangles.AddRange( [a, Corner( frame, x0 + (y1 - y0), y1, z ), d] );
							break;
					}
				}
				AssertSameSpans( frame, triangles.ToArray(), $"frame {frame.MinX}, round {round}" );
			}
		}
	}

	static Vector3 Corner( in FieldFrame frame, int x, int y, float z ) => new( frame.MinX + x * Cell, frame.MinY + y * Cell, z );

	[TestMethod]
	public void GridAlignedQuadLeavesTheCellsBeyondItsEdgesEmpty()
	{
		var frame = Frame( 0, 0, 0 );
		var field = new SolidField();
		field.Reset( Size, Size );
		Vector3 a = new( 8, 8, 12 ), b = new( 24, 8, 12 ), c = new( 24, 16, 12 ), d = new( 8, 16, 12 );
		Rasterizer.Triangle( field, frame, a, b, c, 1, 4 );
		Rasterizer.Triangle( field, frame, a, c, d, 1, 4 );

		// Cells 2..5 by 2..3 and nothing else
		Assert.AreEqual( 8, field.SpanCount );
		for ( int y = 2; y < 4; y++ )
		{
			for ( int x = 2; x < 6; x++ ) Assert.AreEqual( SolidField.Pack( 3, 4, 1 ), field.Spans[(x + y * Size) * field.Capacity] );
		}
	}

	[TestMethod]
	public void TriangleLeavesSpans()
	{
		var field = new SolidField();
		field.Reset( Size, Size );
		Rasterizer.Triangle( field, Frame( 0, 0, 0 ), new Vector3( 0, 0, 0 ), new Vector3( 40, 0, 0 ), new Vector3( 0, 40, 0 ), 1, 1 );
		Assert.IsTrue( field.SpanCount > 0 );
	}

	[TestMethod]
	public void TriangleWhoseBoxOverlapsButWhichMissesLeavesNothing()
	{
		var field = new SolidField();
		field.Reset( Size, Size );
		Rasterizer.Triangle( field, Frame( 0, 0, 0 ), new Vector3( -40, -40, 22 ), new Vector3( 12, -40, 22 ), new Vector3( -40, 12, 22 ), 1, 1 );
		Assert.AreEqual( 0, field.SpanCount );
	}

	[TestMethod]
	public void TouchingSpansMerge()
	{
		var field = new SolidField();
		field.Reset( 1, 1 );
		field.Add( 0, 0, 10, 1, 0 );
		Assert.AreEqual( 1, field.SpanCount );

		// The tops are far apart, so the merged span takes the newer area
		field.Add( 0, 10, 20, 2, 0 );
		Assert.AreEqual( 1, field.SpanCount );
		Assert.AreEqual( SolidField.Pack( 0, 20, 2 ), field.Spans[0] );
	}

	[TestMethod]
	public void MergedSpanKeepsTheHigherAreaWhenTopsAreClose()
	{
		var field = new SolidField();
		field.Reset( 1, 1 );
		field.Add( 0, 0, 10, 1, 2 );
		field.Add( 0, 5, 11, 0, 2 );
		Assert.AreEqual( SolidField.Pack( 0, 11, 1 ), field.Spans[0] );
	}

	[TestMethod]
	public void ColumnsGrowPastTheirFirstCapacity()
	{
		var field = new SolidField();
		field.Reset( 2, 2 );
		field.Add( 3, 500, 501, 1, 0 );
		for ( int i = 64; i >= 0; i-- ) field.Add( 1, i * 3, i * 3 + 2, 1, 0 );

		Assert.AreEqual( 66, field.SpanCount );
		Assert.AreEqual( 65, field.Counts[1] );
		for ( int i = 0; i < 65; i++ ) Assert.AreEqual( SolidField.Pack( i * 3, i * 3 + 2, 1 ), field.Spans[field.Capacity + i] );
		Assert.AreEqual( SolidField.Pack( 500, 501, 1 ), field.Spans[3 * field.Capacity] );
	}

	[TestMethod]
	public void SpanBridgingSeveralSwallowsThem()
	{
		var field = new SolidField();
		field.Reset( 1, 1 );
		field.Add( 0, 0, 2, 1, 0 );
		field.Add( 0, 10, 12, 1, 0 );
		field.Add( 0, 20, 22, 1, 0 );
		field.Add( 0, 30, 32, 1, 0 );
		field.Add( 0, 11, 21, 0, 0 );

		Assert.AreEqual( 3, field.SpanCount );
		Assert.AreEqual( SolidField.Pack( 0, 2, 1 ), field.Spans[0] );
		// The last span it swallows has the same top, so the higher area of the two wins
		Assert.AreEqual( SolidField.Pack( 10, 22, 1 ), field.Spans[1] );
		Assert.AreEqual( SolidField.Pack( 30, 32, 1 ), field.Spans[2] );
	}
}
