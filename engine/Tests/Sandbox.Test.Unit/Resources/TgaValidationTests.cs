namespace ResourceTests;

[TestClass]
public class TgaValidationTests
{
	static byte[] Tga( int imageType, int width, int height, int pixelSize, byte[] payload, int colormapLength = 0 ) =>
	[
		0, 0, (byte)imageType,
		0, 0, (byte)(colormapLength & 0xff), (byte)(colormapLength >> 8), 0,
		0, 0, 0, 0,
		(byte)(width & 0xff), (byte)(width >> 8),
		(byte)(height & 0xff), (byte)(height >> 8),
		(byte)pixelSize, 0x20,
		.. payload
	];

	[TestMethod]
	public void ValidPayloadsAreAccepted()
	{
		Assert.IsTrue( Bitmap.IsTgaPayloadValid( Tga( 2, 2, 2, 32, new byte[16] ) ) );
		Assert.IsTrue( Bitmap.IsTgaPayloadValid( Tga( 10, 4, 1, 24, [0x83, 1, 2, 3] ) ) );
	}

	[TestMethod]
	public void TruncatedPayloadsAreRejected()
	{
		Assert.IsFalse( Bitmap.IsTgaPayloadValid( Tga( 2, 2, 2, 32, new byte[15] ) ) );
		Assert.IsFalse( Bitmap.IsTgaPayloadValid( Tga( 10, 128, 1, 32, [0x7f, 1, 2, 3, 4] ) ) );
		Assert.IsFalse( Bitmap.IsTgaPayloadValid( Tga( 10, 4, 1, 32, [0x83, 1, 2] ) ) );
	}

	[TestMethod]
	public void UnsupportedHeadersAreRejected()
	{
		Assert.IsFalse( Bitmap.IsTgaPayloadValid( Tga( 2, 65535, 65535, 32, new byte[16] ) ) );
		Assert.IsFalse( Bitmap.IsTgaPayloadValid( Tga( 10, 1, 1, 16, new byte[4] ) ) );
		Assert.IsFalse( Bitmap.IsTgaPayloadValid( Tga( 3, 1, 1, 8, new byte[2048], colormapLength: 256 ) ) );
	}

	[TestMethod]
	public void CreateFromMalformedRleReturnsNull()
	{
		using var bitmap = Bitmap.CreateFromTgaBytes( Tga( 10, 128, 128, 32, [0x7f, 1, 2, 3, 4] ) );
		Assert.IsNull( bitmap );
	}
}
