namespace ResourceTests;

[TestClass]
public class MorphAtlasSizeTests
{
	[TestMethod]
	public void RejectsSizeThatWrapsInt32()
	{
		const int vertexCount = 0x3B0000;
		const int morphCount = 0x456C8;

		var rows = (vertexCount - 1) / 512 + 1;
		Assert.IsTrue( unchecked(rows * morphCount * 2) is > 0 and <= short.MaxValue, "the 32-bit height wraps into a small value" );

		Assert.IsFalse( Mesh.TryGetMorphAtlasSize( vertexCount, morphCount, out _, out _ ) );
	}

	[TestMethod]
	[DataRow( 512, 16383, true )]
	[DataRow( 512, 16384, false )]
	[DataRow( 0x3B0000, 2, true )]
	[DataRow( 0x3B0000, 3, false )]
	[DataRow( int.MaxValue, 1, false )]
	[DataRow( 0, 1, false )]
	[DataRow( 1, 0, false )]
	[DataRow( -1, 1, false )]
	[DataRow( 1, -1, false )]
	public void FitsInt16TextureDimensions( int vertexCount, int morphCount, bool fits )
	{
		Assert.AreEqual( fits, Mesh.TryGetMorphAtlasSize( vertexCount, morphCount, out var width, out var height ) );

		if ( fits )
		{
			Assert.IsTrue( width is > 0 and <= 512 );
			Assert.IsTrue( height is > 0 and <= short.MaxValue );
		}
	}

	[TestMethod]
	public void TypicalMeshSize()
	{
		Assert.IsTrue( Mesh.TryGetMorphAtlasSize( 5000, 52, out var width, out var height ) );
		Assert.AreEqual( 512, width );
		Assert.AreEqual( 10 * 52 * 2, height );

		Assert.IsTrue( Mesh.TryGetMorphAtlasSize( 3, 1, out width, out height ) );
		Assert.AreEqual( 3, width );
		Assert.AreEqual( 2, height );
	}
}
