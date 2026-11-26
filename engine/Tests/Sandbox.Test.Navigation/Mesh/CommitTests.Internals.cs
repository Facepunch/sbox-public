using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// A test of what the C# library does with the tile object it is handed: it publishes that object.
/// </summary>
public partial class CommitTests
{
	[TestMethod]
	public void CommittingTheSameTileObjectAgainPublishesANewBuild()
	{
		var mesh = GridMesh.Create();
		var tile = GridMesh.Flat( 0, 0 );
		mesh.CommitTiles( [tile] );
		var first = mesh.State.Find( 0, 0 )!;
		mesh.CommitTiles( [tile] );
		var second = mesh.State.Find( 0, 0 )!;
		Assert.AreNotSame( first, second );
		Assert.AreNotEqual( first.Serial, second.Serial );
		Assert.AreSame( tile, first );
	}
}
