using Borealis.Source2.Materials;
using System.Numerics;

namespace BorealisTests;

/// <summary>
/// <see cref="TextureSheet.CacheRows"/>: a sprite sheet packed as native's sheet cache lays it out, which sprite shaders read
/// their frames from.
/// </summary>
[TestClass]
public class TextureSheetTests
{
	static TextureSheet.Frame Frame( float left, float right ) => new( 1.0f, [new TextureSheet.Image( new Vector4( left, 0, right, 1 ), new Vector4( left, 0, right, 1 ) )] );

	/// <summary>
	/// A looping sequence of two one-second frames, the left and right halves of the texture: three rows of 8 texels, whose
	/// first column pair starts at frame 0 blending towards frame 1, with a time scale of 16 times its two seconds, and whose
	/// middle pair starts at frame 1 blending back to frame 0.
	/// </summary>
	[TestMethod]
	public void SequencesPackAsNativesSheetCache()
	{
		var sheet = new TextureSheet( [new TextureSheet.Sequence( 0, false, false, 2.0f, [Frame( 0.0f, 0.5f ), Frame( 0.5f, 1.0f )] )] );
		var rows = sheet.CacheRows( 8, out var sequences );

		Assert.AreEqual( 1, sequences );
		Assert.AreEqual( 8 * 4 * TextureSheet.RowsPerSequence, rows.Length );

		const int Row = 8 * 4;
		CollectionAssert.AreEqual( new ushort[] { 0, 32, 0 }, rows[0..3], "blend, time scale, clamp" );
		CollectionAssert.AreEqual( new ushort[] { 0, 0, 32767, 65535 }, rows[Row..(Row + 4)], "frame 0" );
		CollectionAssert.AreEqual( new ushort[] { 0, 0, 65535, 65535 }, rows[(Row + 4)..(Row + 8)], "the crop they share" );
		CollectionAssert.AreEqual( new ushort[] { 32767, 0, 65535, 65535 }, rows[(Row * 2)..(Row * 2 + 4)], "frame 1" );

		// The third of four column pairs is half way: frame 1, blending towards frame 0
		CollectionAssert.AreEqual( new ushort[] { 32767, 0, 65535, 65535 }, rows[(Row + 16)..(Row + 20)], "frame 1 at half time" );
		CollectionAssert.AreEqual( new ushort[] { 0, 0, 32767, 65535 }, rows[(Row * 2 + 16)..(Row * 2 + 20)], "frame 0 after it" );
	}

	/// <summary>A sheet whose sequence ids skip one fills the gap with its first sequence, as native's does.</summary>
	[TestMethod]
	public void SkippedSequencesCopyTheFirst()
	{
		var sheet = new TextureSheet( [new TextureSheet.Sequence( 1, true, false, 1.0f, [Frame( 0.0f, 1.0f )] )] );
		var rows = sheet.CacheRows( 4, out var sequences );

		Assert.AreEqual( 2, sequences );
		var block = 4 * 4 * TextureSheet.RowsPerSequence;
		CollectionAssert.AreEqual( rows[0..block], rows[block..(block * 2)] );
	}

	/// <summary>
	/// A sheet as a texture's header reads it (<see cref="TextureHeader.Sheet"/>) packs as the same sheet read from the file
	/// would: each image's cropped and uncropped corners become its rectangles.
	/// </summary>
	[TestMethod]
	public void HeaderSheetsPackAsSheetsReadFromTheFile()
	{
		static SheetImage Image( float left, float right ) => new( new Vector2( left, 0 ), new Vector2( right, 1 ), new Vector2( left, 0 ), new Vector2( right, 1 ) );
		var header = new[]
		{
			new SheetSequence { Id = 0, Clamp = false, TotalTime = 2.0f, Frames = [new SheetFrame( 1.0f, [Image( 0.0f, 0.5f )] ), new SheetFrame( 1.0f, [Image( 0.5f, 1.0f )] )] },
		};

		var from = TextureSheet.From( header );
		var direct = new TextureSheet( [new TextureSheet.Sequence( 0, false, false, 2.0f, [Frame( 0.0f, 0.5f ), Frame( 0.5f, 1.0f )] )] );
		CollectionAssert.AreEqual( direct.CacheRows( 8, out _ ), from.CacheRows( 8, out _ ) );
		Assert.IsNull( TextureSheet.From( [] ) );
		Assert.IsNull( TextureSheet.From( null ) );
	}
}
