using SkiaSharp;
using System;
using System.IO;
using Topten.RichTextKit;

namespace RenderTests;

/// <summary>
/// The face FontManager picks for a family, weight and slant must not depend on the order the
/// font files loaded in. Skia reports the Roboto Condensed files as family "Roboto", so they
/// compete with the normal-width Roboto files for the same style.
/// </summary>
[TestClass]
public class FontManagerTest
{
	const uint HeadTable = 0x68656164; // 'head'

	static string CoreFolder => Path.Combine( Environment.CurrentDirectory, "core" );
	static string FontsFolder => Path.Combine( CoreFolder, "fonts" );

	[DataTestMethod]
	[DataRow( "Roboto", 400, false, "Roboto-Regular.ttf" )]
	[DataRow( "Roboto", 700, false, "Roboto-Bold.ttf" )]
	[DataRow( "Roboto", 300, false, "Roboto-Light.ttf" )]
	[DataRow( "Roboto", 400, true, "Roboto-Italic.ttf" )]
	[DataRow( "Roboto Mono", 400, false, "RobotoMono-Regular.ttf" )]
	[DataRow( "Roboto Mono", 600, false, "RobotoMono-Bold.ttf" )]
	[DataRow( "Roboto Mono", 200, true, "RobotoMono-ThinItalic.ttf" )]
	public void CoreFontsResolveToTheNormalWidthFace( string family, int weight, bool italic, string expectedFile )
	{
		var fonts = new FontManager();
		fonts.LoadAll( new LocalFileSystem( CoreFolder ) );

		AssertIsFile( expectedFile, fonts.TypefaceFromStyle( new Style { FontFamily = family, FontWeight = weight, FontItalic = italic }, false ) );

		fonts.Clear( true );
	}

	[DataTestMethod]
	[DataRow( "Roboto-Regular.ttf", "RobotoCondensed-Regular.ttf" )]
	[DataRow( "RobotoCondensed-Regular.ttf", "Roboto-Regular.ttf" )]
	public void LoadOrderDoesNotChangeTheFace( string firstFile, string secondFile )
	{
		var fonts = new FontManager();
		fonts.LoadAll( FolderWithOnly( firstFile ) );
		fonts.LoadAll( FolderWithOnly( secondFile ) );

		AssertIsFile( "Roboto-Regular.ttf", fonts.TypefaceFromStyle( new Style { FontFamily = "Roboto", FontWeight = 400 }, false ) );

		fonts.Clear( true );
	}

	/// <summary>
	/// The 'head' table carries the whole-file checksum, so it identifies the file a face came from.
	/// </summary>
	static void AssertIsFile( string expectedFile, SKTypeface face )
	{
		using var expected = SKTypeface.FromFile( Path.Combine( FontsFolder, expectedFile ) );

		CollectionAssert.AreEqual( expected.GetTableData( HeadTable ), face.GetTableData( HeadTable ),
			$"expected {expectedFile}, got {face.FamilyName} weight {face.FontWeight} width {face.FontWidth} {face.FontSlant}" );
	}

	static BaseFileSystem FolderWithOnly( string file )
	{
		var fs = new MemoryFileSystem();
		fs.CreateDirectory( "/fonts" );
		fs.WriteAllBytes( $"/fonts/{file}", File.ReadAllBytes( Path.Combine( FontsFolder, file ) ) );
		return fs;
	}
}
