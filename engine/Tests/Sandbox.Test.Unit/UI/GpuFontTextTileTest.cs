using Sandbox;
using Sandbox.UI;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace UITests;

/// <summary>
/// A text block's glyphs are binned into 16px tiles once (<c>GpuFontText.Bin</c>) and appended to each frame's buffer from
/// there, instead of being binned again every frame: each tile must list the same instances either way.
/// </summary>
[TestClass]
[DoNotParallelize]
public class GpuFontTextTileTest
{
	static List<GPUBoxInstance> Instances()
	{
		var list = new List<GPUBoxInstance>();
		var rects = new[] { new Vector4( 0, 0, 10, 10 ), new Vector4( 12, 3, 30, 20 ), new Vector4( 40, 18, 8, 40 ), new Vector4( -5, -5, 100, 6 ), new Vector4( 60, 50, 3, 3 ) };
		foreach ( var rect in rects ) list.Add( new GPUBoxInstance { Rect = rect } );
		return list;
	}

	static List<uint> FrameTiles()
	{
		var flags = BindingFlags.NonPublic | BindingFlags.Static;
		var slots = (Array)typeof( GpuFontText ).GetField( "_slots", flags ).GetValue( null );
		var slot = slots.GetValue( (int)typeof( GpuFontText ).GetField( "_slot", flags ).GetValue( null ) );
		return (List<uint>)slot.GetType().GetField( "Tiles" ).GetValue( slot );
	}

	/// <summary>Each tile's instance indices, read through the placement's offsets as the shader reads them.</summary>
	static List<uint[]> TileLists( GpuFontText.Placement placement, int tilesY )
	{
		var tiles = FrameTiles();
		var lists = new List<uint[]>();
		for ( int t = 0; t < placement.TilesX * tilesY; t++ )
		{
			var from = (int)tiles[placement.TileOffset + t];
			var to = (int)tiles[placement.TileOffset + t + 1];
			lists.Add( tiles.GetRange( from, to - from ).ToArray() );
		}

		return lists;
	}

	[TestMethod]
	public void BinnedOnceMatchesBinnedEachFrame()
	{
		const int width = 70, height = 61;
		const int tilesY = (height + 15) / 16;
		var instances = Instances();

		// Another block first, so neither lands at the start of the frame's buffer
		GpuFontText.Upload( Instances(), 33, 17 );

		var each = GpuFontText.Upload( instances, width, height );
		var eachLists = TileLists( each, tilesY );

		var binned = GpuFontText.Bin( instances, width, height );
		var once = GpuFontText.Upload( instances, binned, width, height );
		var onceLists = TileLists( once, tilesY );

		Assert.AreNotEqual( each.TileOffset, once.TileOffset );
		Assert.AreEqual( each.TilesX, once.TilesX );
		Assert.AreEqual( eachLists.Count, onceLists.Count );
		Assert.IsTrue( eachLists.Any( x => x.Length > 1 ), "some tile lists more than one instance" );
		for ( int t = 0; t < eachLists.Count; t++ )
			CollectionAssert.AreEqual( eachLists[t], onceLists[t], $"tile {t}" );

		// Appending again leaves the kept bins as they were
		var again = GpuFontText.Upload( instances, binned, width, height );
		var againLists = TileLists( again, tilesY );
		for ( int t = 0; t < eachLists.Count; t++ )
			CollectionAssert.AreEqual( eachLists[t], againLists[t], $"tile {t}, appended twice" );
	}
}
