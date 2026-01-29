namespace TestTexture;

[TestClass]
public class BitmapLoadingTests
{
	/// <summary>
	/// Minimal IES file content for testing.
	/// This is a simplified IES file with a single point light distribution.
	/// </summary>
	private static readonly string MinimalIesContent = """
		IESNA:LM-63-2002
		[TEST] Minimal test IES
		[MANUFAC] Test
		TILT=NONE
		1 100 1.0 3 2 1 1 0 0 0 1.0 1.0 100
		0 45 90
		0 180
		100 80 50
		100 80 50
		""";

	/// <summary>
	/// Creates a minimal valid TGA file (uncompressed, 2x2 pixels, 32-bit BGRA).
	/// </summary>
	private static byte[] CreateMinimalTga()
	{
		// TGA Header (18 bytes)
		var header = new byte[]
		{
			0,      // ID length
			0,      // Color map type (no color map)
			2,      // Image type (uncompressed true-color)
			0, 0, 0, 0, 0, // Color map specification (unused)
			0, 0,   // X origin
			0, 0,   // Y origin
			2, 0,   // Width (2 pixels)
			2, 0,   // Height (2 pixels)
			32,     // Bits per pixel
			0x20    // Image descriptor (top-left origin, 8 bits alpha)
		};

		// Pixel data: 4 pixels, 4 bytes each (BGRA)
		var pixels = new byte[]
		{
			255, 0, 0, 255,     // Blue
			0, 255, 0, 255,     // Green
			0, 0, 255, 255,     // Red
			255, 255, 255, 255  // White
		};

		var tga = new byte[header.Length + pixels.Length];
		header.CopyTo( tga, 0 );
		pixels.CopyTo( tga, header.Length );
		return tga;
	}

	[TestMethod]
	public void LoadIesFromBytes()
	{
		var bytes = System.Text.Encoding.ASCII.GetBytes( MinimalIesContent );

		using var bitmap = Sandbox.Bitmap.CreateFromIesBytes( bytes );

		Assert.IsNotNull( bitmap, "Bitmap should be created from IES bytes" );
		Assert.IsTrue( bitmap.IsValid, "Bitmap should be valid" );
		Assert.AreEqual( 512, bitmap.Width, "IES bitmap should be 512 pixels wide" );
		Assert.AreEqual( 512, bitmap.Height, "IES bitmap should be 512 pixels tall" );
	}

	[TestMethod]
	public void LoadTgaFromBytes()
	{
		var bytes = CreateMinimalTga();

		using var bitmap = Sandbox.Bitmap.CreateFromTgaBytes( bytes );

		Assert.IsNotNull( bitmap, "Bitmap should be created from TGA bytes" );
		Assert.IsTrue( bitmap.IsValid, "Bitmap should be valid" );
		Assert.AreEqual( 2, bitmap.Width, "TGA bitmap should be 2 pixels wide" );
		Assert.AreEqual( 2, bitmap.Height, "TGA bitmap should be 2 pixels tall" );
	}

	[TestMethod]
	public void IesIsDetectedCorrectly()
	{
		var validIes = System.Text.Encoding.ASCII.GetBytes( "IESNA:LM-63-2002\n" );
		var invalidData = System.Text.Encoding.ASCII.GetBytes( "Not an IES file" );

		Assert.IsTrue( Sandbox.Bitmap.IsIes( validIes ), "Valid IES header should be detected" );
		Assert.IsFalse( Sandbox.Bitmap.IsIes( invalidData ), "Invalid data should not be detected as IES" );
		Assert.IsFalse( Sandbox.Bitmap.IsIes( null ), "Null data should not be detected as IES" );
		Assert.IsFalse( Sandbox.Bitmap.IsIes( [] ), "Empty data should not be detected as IES" );
	}

	[TestMethod]
	public void TgaIsDetectedCorrectly()
	{
		var validTga = CreateMinimalTga();
		var invalidData = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

		Assert.IsTrue( Sandbox.Bitmap.IsTga( validTga ), "Valid TGA header should be detected" );
		Assert.IsFalse( Sandbox.Bitmap.IsTga( invalidData ), "Invalid data should not be detected as TGA" );
		Assert.IsFalse( Sandbox.Bitmap.IsTga( null ), "Null data should not be detected as TGA" );
		Assert.IsFalse( Sandbox.Bitmap.IsTga( [] ), "Empty data should not be detected as TGA" );
	}
}
