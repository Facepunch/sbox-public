using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

[TestClass]
public class NavSettingsTests
{
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

	static NavSettings Rat( float climb ) => NavSettings.Create( new() { AgentHeight = 12, AgentRadius = 4, AgentClimb = climb, AgentMaxSlope = 40, CellSize = 4, CellHeight = 4, TileSize = 32 } );

	[TestMethod]
	public void ClimbAboveHeightInCellsIsRefused()
	{
		// A floor, a thin wall and a small plate a few cells over the floor. With a climb of more cells than
		// the height, the plate and the floor below it both reach the floor beside them, which one
		// connection per side cannot hold.
		var scene = new MeshBuilder();
		scene.Slab( new Vector3( -100, -100, -4 ), new Vector3( 300, 300, 0 ), 400 );
		scene.Box( new Vector3( 64.5f, -100, 0 ), new Vector3( 67.5f, 300, 240 ) );
		scene.Box( new Vector3( 20.5f, 40.5f, 16.5f ), new Vector3( 23.5f, 43.5f, 19.5f ) );

		Assert.IsFalse( Rat( 16 ).IsValid( out var reason ) );
		StringAssert.Contains( reason, "AgentClimb" );
		Assert.ThrowsException<ArgumentException>( () => new NavMesh( Rat( 16 ), NavMeshOptions.Create( Rat( 16 ) ) ) );
		Assert.IsFalse( NavSettings.Create( new() { AgentHeight = 64, AgentRadius = 16, AgentClimb = 68, AgentMaxSlope = 40, CellSize = 4, CellHeight = 4, TileSize = 256 } ).IsValid( out _ ) );

		// As many cells is the most there is.
		foreach ( float climb in new[] { 12f, 15.9f } )
		{
			Assert.IsTrue( Rat( climb ).IsValid( out reason ), reason );
			var mesh = new NavMesh( Rat( climb ), NavMeshOptions.Create( Rat( climb ) ) );
			var failures = new List<(int X, int Y, string Error)>();
			Assert.AreEqual( 0, mesh.Build( new NavBuilder(), scene.Vertices.ToArray(), scene.Indices.ToArray(), failures ), string.Join( "; ", failures ) );
			Assert.IsTrue( mesh.GetStats().Polygons > 0 );
		}
	}
}
