using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

[TestClass]
public class NavSettingsTests
{
	[TestMethod]
	public void CreateGivesValidDefaults()
	{
		var settings = NavSettings.Create( 64, 16, 18, 40, 4, 4 );
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
		var settings = NavSettings.Create( 64, 16, 18, 40, cellSize, cellHeight, tileSize );
		Assert.IsFalse( settings.IsValid( out var reason ) );
		Assert.IsNotNull( reason );
	}
}
