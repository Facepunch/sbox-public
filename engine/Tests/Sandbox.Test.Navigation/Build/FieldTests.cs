using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nav.Build;

namespace Nav.Lib.Tests.Build;

[TestClass]
public class FieldTests
{
	[TestMethod]
	public void IsolatedSpanIsALedge()
	{
		var solid = new SolidField();
		solid.Reset( 2, 2 );
		solid.Add( 0, 0, 2, 1, 0 );

		var field = new CompactField();
		Assert.IsTrue( new FieldBuilder().Compact( solid, field, 2, 1, out _ ) );
		Assert.AreEqual( 0, field.SpanCount );
	}

	[TestMethod]
	public void LevelGroundIsWalkableExceptAtTheFieldEdge()
	{
		var solid = new SolidField();
		solid.Reset( 5, 5 );
		for ( int column = 0; column < 25; column++ ) solid.Add( column, 0, 3, 1, 0 );

		var field = new CompactField();
		Assert.IsTrue( new FieldBuilder().Compact( solid, field, 4, 1, out _ ) );
		Assert.AreEqual( 9, field.SpanCount );

		// The centre connects to all four neighbours, a corner of the 3 x 3 block to two
		int centre = field.Index[2 + 2 * 5];
		Assert.AreEqual( 0u, field.Con[centre] & 0xFFFFFF );
		int corner = field.Index[1 + 1 * 5];
		Assert.AreEqual( 63, CompactField.Connection( field.Con[corner], 0 ) );
		Assert.AreEqual( 0, CompactField.Connection( field.Con[corner], 1 ) );
		Assert.AreEqual( 0, CompactField.Connection( field.Con[corner], 2 ) );
		Assert.AreEqual( 63, CompactField.Connection( field.Con[corner], 3 ) );
	}

	[TestMethod]
	public void LowObstacleOnWalkableGroundBecomesWalkable()
	{
		// A not walkable sliver two cells above walkable ground, within the climb of 2
		var solid = new SolidField();
		solid.Reset( 5, 5 );
		for ( int column = 0; column < 25; column++ )
		{
			solid.Add( column, 0, 3, 1, 0 );
			solid.Add( column, 4, 5, 0, 0 );
		}

		var field = new CompactField();
		Assert.IsTrue( new FieldBuilder().Compact( solid, field, 4, 2, out _ ) );

		// The ground has no clearance left and goes, the sliver on top of it is stood on instead, edge columns included:
		// a promoted span is not tested as a ledge
		Assert.AreEqual( 25, field.SpanCount );
		Assert.AreEqual( 5, field.Floor[field.Index[12]] );
	}

	[TestMethod]
	public void LowCeilingIsNotWalkable()
	{
		var solid = new SolidField();
		solid.Reset( 5, 5 );
		for ( int column = 0; column < 25; column++ )
		{
			solid.Add( column, 0, 3, 1, 0 );
			solid.Add( column, 6, 8, 0, 0 );
		}

		var field = new CompactField();
		Assert.IsTrue( new FieldBuilder().Compact( solid, field, 4, 1, out _ ) );
		Assert.AreEqual( 0, field.SpanCount );
	}

	// Connections against a plain reference: the first span of the neighbour column that is within the
	// climb and leaves enough headroom.
	[TestMethod]
	public void ConnectionsMatchFirstMatchReference()
	{
		var random = new Random( 1729 );
		var builder = new FieldBuilder();
		var solid = new SolidField();
		var field = new CompactField();
		int connected = 0;
		for ( int test = 0; test < 200; test++ )
		{
			solid.Reset( 8, 8 );
			for ( int column = 0; column < 64; column++ )
			{
				int height = 0;
				for ( int layer = 0, count = random.Next( 20 ); layer < count; layer++ )
				{
					height += random.Next( 2, 32 );
					solid.Add( column, height, height + 1, random.Next( 4 ) == 0 ? 0 : 1, 0 );
				}
			}

			int walkableHeight = random.Next( 1, 20 ), climb = random.Next( 20, 400 );
			Assert.IsTrue( builder.Compact( solid, field, walkableHeight, climb, out _ ) );
			for ( int y = 0; y < 8; y++ )
			{
				for ( int x = 0; x < 8; x++ )
				{
					for ( int i = field.Index[x + y * 8]; i < field.Index[x + y * 8 + 1]; i++ )
					{
						for ( int direction = 0; direction < 4; direction++ )
						{
							int nx = x + (direction == 0 ? -1 : direction == 2 ? 1 : 0), ny = y + (direction == 1 ? 1 : direction == 3 ? -1 : 0);
							int expected = BuildConfig.NotConnected;
							if ( nx >= 0 && nx < 8 && ny >= 0 && ny < 8 )
							{
								int first = field.Index[nx + ny * 8], end = field.Index[nx + ny * 8 + 1];
								for ( int k = first; k < Math.Min( end, first + BuildConfig.NotConnected ); k++ )
								{
									if ( Math.Min( builder.Tops[i], builder.Tops[k] ) - Math.Max( field.Floor[i], field.Floor[k] ) >= walkableHeight && Math.Abs( field.Floor[i] - field.Floor[k] ) <= climb )
									{
										expected = k - first;
										break;
									}
								}
							}
							Assert.AreEqual( expected, CompactField.Connection( field.Con[i], direction ) );
							if ( expected != BuildConfig.NotConnected ) connected++;
						}
					}
				}
			}
		}
		Assert.IsTrue( connected > 1000, $"Only {connected} connections were checked" );
	}

	[TestMethod]
	public void ErosionRemovesTheRadiusFromEveryEdge()
	{
		var solid = new SolidField();
		solid.Reset( 20, 20 );
		for ( int column = 0; column < 400; column++ ) solid.Add( column, 0, 3, 1, 0 );

		var builder = new FieldBuilder();
		var field = new CompactField();
		Assert.IsTrue( builder.Compact( solid, field, 4, 1, out _ ) );
		Assert.AreEqual( 18 * 18, field.SpanCount );

		// The outer ring of spans is the edge (distance 0), two more rings are closer than 2 cells
		Assert.AreEqual( 14 * 14, builder.Erode( field, 2 ) );
		for ( int y = 1; y < 19; y++ )
		{
			for ( int x = 1; x < 19; x++ )
			{
				bool kept = x >= 3 && x <= 16 && y >= 3 && y <= 16;
				Assert.AreEqual( kept ? 1u : 0u, field.Con[field.Index[x + y * 20]] >> 24, $"{x}, {y}" );
			}
		}
	}

	// Eight stacked layers in every second column: a row holds more spans than it has columns,
	// and every span of a column needs a region of its own.
	[TestMethod]
	public void LayerRegionsHandleMoreSpansThanColumnsInARow()
	{
		var field = new CompactField { Width = 8, Height = 8, Index = new int[65], Floor = new ushort[256], Con = new uint[256], Area = new byte[256] };
		int span = 0;
		for ( int y = 0; y < 8; y++ )
		{
			for ( int x = 0; x < 8; x++ )
			{
				field.Index[x + y * 8] = span;
				if ( x % 2 != 0 ) continue;
				for ( int layer = 0; layer < 8; layer++, span++ )
				{
					field.Floor[span] = (ushort)(layer * 32);
					field.Area[span] = 1;
					uint north = y < 7 ? (uint)layer : 63, south = y > 0 ? (uint)layer : 63;
					field.Con[span] = 63 | north << 6 | 63 << 12 | south << 18;
				}
			}
		}
		field.Index[64] = span;
		field.SpanCount = span;

		var open = new OpenBlocks();
		open.Find( field, 0 );
		Assert.IsTrue( new RegionBuilder().Build( field, open, 0, 0, out var error ), error );
		Assert.IsTrue( field.RegionCount >= 32 );

		// One region per column and layer, the same all the way along y
		for ( int i = 0; i < span; i++ ) Assert.AreEqual( field.Region[i % 32], field.Region[i] );
		var seen = new HashSet<ushort>();
		for ( int i = 0; i < 32; i++ ) Assert.IsTrue( field.Region[i] != 0 && seen.Add( field.Region[i] ) );
	}
}
