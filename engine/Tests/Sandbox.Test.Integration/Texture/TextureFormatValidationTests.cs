using System;

namespace TextureTests;

[TestClass]
public class TextureFormatValidationTests
{
	[TestMethod]
	public void UndefinedFormatsAreRejected()
	{
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Texture.Create( 4, 4, (ImageFormat)5000 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Texture.Create( 4, 4, (ImageFormat)(-50) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Texture.CreateRenderTarget().WithFormat( (ImageFormat)5000 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Texture.CreateRenderTarget().WithFormat( ImageFormat.None ).WithSize( 4, 4 ).Create() );
	}

	[TestMethod]
	public void DefaultFormatStillResolves()
	{
		using var texture = Texture.Create( 4, 4, ImageFormat.Default ).Finish();
		Assert.AreEqual( ImageFormat.RGBA8888, texture.ImageFormat );
	}
}
