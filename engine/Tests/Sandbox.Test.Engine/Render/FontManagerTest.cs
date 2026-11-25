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

	/// <summary>
	/// Icon ligatures keep their shape and atlas entries when panels switch axes and return to a previous setting.
	/// </summary>
	[DataTestMethod]
	[DataRow( "Material Symbols Outlined" )]
	[DataRow( "Material Symbols Rounded" )]
	[DataRow( "Material Symbols Sharp" )]
	[DataRow( "Material Icons" )]
	[DataRow( "Material Icons Round" )]
	[DataRow( "Material Icons Sharp" )]
	public void SymbolVariationsShapeAndDraw( string family )
	{
		var block = new TextBlock { FontMapper = CoreFonts.Value };
		(SKTypeface Typeface, ushort Glyph) Run( string settings, Sandbox.UI.FontOpticalSizing sizing = Sandbox.UI.FontOpticalSizing.Auto )
		{
			Assert.IsTrue( Sandbox.UI.FontVariationSettings.TryParse( settings, out var axes ) );
			block.AddText( "favorite", new Style { FontFamily = family, FontSize = 32, FontWeight = 600, FontVariationSettings = axes, FontOpticalSizing = sizing } );
			var run = block.FontRuns.Single();
			Assert.AreEqual( 1, run.Glyphs.Length );
			Assert.AreNotEqual( (ushort)0, run.Glyphs[0] );
			var glyph = (run.Typeface, run.Glyphs[0]);
			block.Clear();
			return glyph;
		}

		var normal = Run( "normal" );
		var filled = Run( "'FILL' 1, 'wght' 150, 'opsz' 100, 'GRAD' -25" );
		var positions = normal.Typeface.VariationDesignPosition.ToDictionary( p => p.Axis.ToString(), p => p.Value );
		Assert.AreEqual( 600f, positions["wght"] );
		Assert.AreEqual( 32f, positions["opsz"] );
		var noOpticalSizing = Run( "normal", Sandbox.UI.FontOpticalSizing.None ).Typeface;
		Assert.AreEqual( noOpticalSizing.VariationDesignParameters.Single( a => a.Tag.ToString() == "opsz" ).Default,
			noOpticalSizing.VariationDesignPosition.Single( p => p.Axis.ToString() == "opsz" ).Value );
		positions = filled.Typeface.VariationDesignPosition.ToDictionary( p => p.Axis.ToString(), p => p.Value );
		Assert.AreEqual( 1f, positions["FILL"] );
		Assert.AreEqual( 150f, positions["wght"] );
		Assert.AreEqual( 48f, positions["opsz"] );
		Assert.AreEqual( -25f, positions["GRAD"] );
		Assert.AreSame( filled.Typeface, Run( "'FILL' 1, 'wght' 150, 'opsz' 48, 'GRAD' -25, 'XXXX' 99" ).Typeface );
		Assert.AreSame( normal.Typeface, Run( "normal" ).Typeface );

		using var font = new SKFont( normal.Typeface, 32 );
		using var outline = font.GetGlyphPath( normal.Glyph );
		var path = outline.ToSvgPathData();
		var atlasIndex = GpuFontGlyphCache.Get( normal.Typeface, normal.Glyph ).Index;
		foreach ( var setting in new[] { "'FILL' 1", "'wght' 150", "'GRAD' -25", "'opsz' 48" } )
		{
			var icon = Run( setting );
			using var variant = new SKFont( icon.Typeface, 32 );
			using var variantPath = variant.GetGlyphPath( icon.Glyph );
			Assert.AreNotEqual( path, variantPath.ToSvgPathData(), setting );
			Assert.AreNotEqual( atlasIndex, GpuFontGlyphCache.Get( icon.Typeface, icon.Glyph ).Index, setting );
		}
	}

	static string CoreFolder => Path.Combine( Environment.CurrentDirectory, "core" );
	static string FontsFolder => Path.Combine( CoreFolder, "fonts" );

	// Loading every core font is slow; the lookup rows only read, so they share one manager
	static readonly Lazy<FontManager> CoreFonts = new( () =>
	{
		var fonts = new FontManager();
		fonts.LoadAll( new LocalFileSystem( CoreFolder ) );
		return fonts;
	} );

	[ClassCleanup]
	public static void ClassCleanup()
	{
		if ( CoreFonts.IsValueCreated ) CoreFonts.Value.Clear( true );
	}

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
		AssertIsFile( expectedFile, CoreFonts.Value.TypefaceFromStyle( new Style { FontFamily = family, FontWeight = weight, FontItalic = italic }, false ) );
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

	/// <summary>
	/// A font that disappears before the watcher opens it must not prevent a later font in the batch from loading.
	/// </summary>
	[DataTestMethod]
	[DataRow( "/fonts/removed.ttf" )]
	[DataRow( "/fonts/removed/removed.ttf" )]
	[DoNotParallelize]
	public async Task WatcherLoadsNextFontAfterDeletion( string missingPath )
	{
		const string addedPath = "/fonts/added.ttf";
		var fs = new MemoryFileSystem();
		var fonts = new FontManager();

		try
		{
			fs.CreateDirectory( "/fonts" );
			fonts.LoadAll( fs );

			fs.AddChangedFile( missingPath );
			fs.WriteAllBytes( addedPath, File.ReadAllBytes( Path.Combine( FontsFolder, "Roboto-Regular.ttf" ) ) );

			Assert.IsTrue( await FileWatch.TickUntilFileChanged( addedPath ) );
			CollectionAssert.Contains( fonts.FontFamilies.ToArray(), "Roboto" );
		}
		finally
		{
			fonts.Clear( true );
			fs.Dispose();
		}
	}
}
