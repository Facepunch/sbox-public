using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

[TestClass]
public class NavSettingsTests
{
	[TestMethod]
	public void CreateGivesValidDefaults()
	{
		var settings = NavSettings.Create( new() { AgentHeight = 64, AgentRadius = 16, AgentClimb = 18, AgentMaxSlope = 40, CellSize = 4, CellHeight = 4, TileSize = 256 } );
		Assert.IsTrue( settings.IsValid( out var reason ), reason );
		Assert.AreEqual( 256, settings.TileSize );
	}

	[TestMethod]
	[DataRow( 0f, 4f, 256 )]
	[DataRow( 4f, 0f, 256 )]
	[DataRow( 4f, 4f, 100 )]
	[DataRow( 4f, 4f, 16 )]
	[DataRow( float.NaN, 4f, 256 )]
	public void InvalidSettingsNameTheReason( float cellSize, float cellHeight, int tileSize )
	{
		var settings = NavSettings.Create( new() { AgentHeight = 64, AgentRadius = 16, AgentClimb = 18, AgentMaxSlope = 40, CellSize = cellSize, CellHeight = cellHeight, TileSize = tileSize } );
		Assert.IsFalse( settings.IsValid( out var reason ) );
		Assert.IsNotNull( reason );
	}
}
