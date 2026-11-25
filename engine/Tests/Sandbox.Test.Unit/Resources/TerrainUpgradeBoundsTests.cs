using System.IO;

namespace ResourceTests;

/// <summary>
/// Legacy terrain upgrades copy the heightmap into native pixels sized from the declared map size.
/// </summary>
[TestClass]
public class TerrainUpgradeBoundsTests
{
	[DataTestMethod]
	[DataRow( 7, 57 )]
	[DataRow( 7, 48 )]
	[DataRow( 1, 1 )]
	[DataRow( 65537, 1 )]
	public void HeightmapMustMatchItsDeclaredSize( int size, int samples )
	{
		Assert.ThrowsException<InvalidDataException>( () => { TerrainStorage.ResampleHeightmap( new ushort[samples], size, size - 1 ); } );
	}
}
