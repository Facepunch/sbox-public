using System;

namespace TextureTests;

[TestClass]
public class TextureMipDataValidationTest
{
	[TestMethod]
	[DataRow( 767 )]
	[DataRow( 769 )]
	[DataRow( 1019 )]
	[DataRow( 1021 )]
	[DataRow( 340 )]
	[DataRow( 584 )]
	public void ArrayRejectsIncompleteOrIncorrectMipData( int bytes )
	{
		Assert.ThrowsException<Exception>( () => Texture.CreateArray( 8, 8, 3 )
			.WithMips( 4 ).WithData( new byte[bytes] ).Finish() );
	}

	[TestMethod]
	[DataRow( 255 )]
	[DataRow( 257 )]
	[DataRow( 339 )]
	[DataRow( 341 )]
	public void OrdinaryTextureRejectsIncorrectMipData( int bytes )
	{
		Assert.ThrowsException<Exception>( () => Texture.Create( 8, 8 )
			.WithMips( 4 ).WithData( new byte[bytes] ).Finish() );
	}

	[TestMethod]
	[DataRow( 1535 )]
	[DataRow( 1537 )]
	[DataRow( 2039 )]
	[DataRow( 2041 )]
	public void CubemapRejectsIncorrectMipData( int bytes )
	{
		Assert.ThrowsException<Exception>( () => Texture.CreateCube( 8, 8 )
			.WithMips( 4 ).WithData( new byte[bytes] ).Finish() );
	}

	[TestMethod]
	[DataRow( -1 )]
	[DataRow( 65 )]
	public void DeclaredLengthCannotExceedBufferBounds( int length )
	{
		var bytes = new byte[64];
		Assert.ThrowsException<Exception>( () => Texture.CreateArray( 4, 4, 1 ).WithData( bytes, length ) );
	}
}
