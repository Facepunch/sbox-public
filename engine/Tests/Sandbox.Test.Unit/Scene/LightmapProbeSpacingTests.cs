using System;

namespace SceneTests;

/// <summary>
/// Covers probe placement and bake limits without allocating textures.
/// </summary>
[TestClass]
public class LightmapProbeSpacingTests
{
	/// <summary>
	/// Flatgrass fits more than 64 probes across at the minimum spacing.
	/// </summary>
	[TestMethod]
	public void FitsWholeCellsAtMinimumSpacing()
	{
		var lightmap = new BakedLightmap( null ) { ProbeSpacing = 8 };

		Assert.AreEqual( 16, lightmap.ProbeSpacing );
		Assert.AreEqual( new Vector3Int( 121, 121, 34 ), lightmap.GetProbeGridSize( new Vector3( 1936, 1936, 556 ), false, 0 ) );
		Assert.AreEqual( new Vector3Int( 1, 1, 1 ), lightmap.GetProbeGridSize( new Vector3( 1, 16, 31 ), false, 0 ) );
	}

	/// <summary>
	/// Ambient cubes store six slices per vertical cell and share the trace buffer with surface luxels.
	/// </summary>
	[TestMethod]
	public void RejectsGridsThatExceedBakeLimits()
	{
		var lightmap = new BakedLightmap( null ) { ProbeSpacing = 16 };
		var extent = new Vector3( 16, 16, 8192 );

		Assert.ThrowsException<InvalidOperationException>( () => lightmap.GetProbeGridSize( extent, false, 0 ) );
		Assert.AreEqual( new Vector3Int( 1, 1, 512 ), lightmap.GetProbeGridSize( extent, true, 0 ) );
		Assert.ThrowsException<InvalidOperationException>( () => lightmap.GetProbeGridSize( extent, true, 30_000_000 ) );
	}
}
