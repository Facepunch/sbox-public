using Borealis.Source2;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Finding compiled files: the folders first, then the host's resolver (a game package's files, named by hash on disk).
/// </summary>
[TestClass]
public class ContentFilesTests
{
	[TestMethod]
	public void FindAsksTheResolverAfterTheFolders()
	{
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-content-{Guid.NewGuid():N}" );
		var loose = Path.Combine( folder, "loose" );
		var cache = Path.Combine( folder, "cache" );
		Directory.CreateDirectory( Path.Combine( loose, "shaders" ) );
		Directory.CreateDirectory( cache );
		try
		{
			File.WriteAllText( Path.Combine( loose, "shaders", "both.shader_c" ), "loose" );
			var hashed = Path.Combine( cache, "hud_glow.0123abcd.shader_c" );
			File.WriteAllText( hashed, "package" );
			var both = Path.Combine( cache, "both.4567cdef.shader_c" );
			File.WriteAllText( both, "package" );

			var asked = new List<string>();
			var files = new ContentFiles( loose )
			{
				Resolve = path =>
				{
					asked.Add( path );
					return path switch
					{
						"shaders/hud_glow.shader_c" => hashed,
						"shaders/both.shader_c" => both,
						"shaders/gone.shader_c" => Path.Combine( cache, "gone.shader_c" ),
						_ => null,
					};
				},
			};

			Assert.AreEqual( hashed, files.Find( "shaders/hud_glow.shader" ), "a package's file is where the resolver says" );
			Assert.AreEqual( "package", System.Text.Encoding.UTF8.GetString( files.TryRead( "/shaders/hud_glow.shader" ) ) );
			Assert.AreEqual( Path.GetFullPath( Path.Combine( loose, "shaders", "both.shader_c" ) ), Path.GetFullPath( files.Find( "shaders/both.shader" ) ), "loose files win" );
			Assert.IsNull( files.Find( "shaders/gone.shader" ), "a resolved path that isn't on disk isn't found" );
			Assert.IsNull( files.Find( "shaders/none.shader" ) );
			CollectionAssert.DoesNotContain( asked, "shaders/both.shader_c", "the resolver isn't asked for what a folder has" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Fallback folders (the rest of the engine's GAME search path: the menu's compiled files) are searched last: a folder
	/// or a package's file the resolver finds wins over them, and what only they have is found there.
	/// </summary>
	[TestMethod]
	public void FallbacksAreSearchedLast()
	{
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-content-{Guid.NewGuid():N}" );
		var loose = Path.Combine( folder, "loose" );
		var menu = Path.Combine( folder, "menu" );
		var cache = Path.Combine( folder, "cache" );
		Directory.CreateDirectory( Path.Combine( loose, "models" ) );
		Directory.CreateDirectory( Path.Combine( menu, "models" ) );
		Directory.CreateDirectory( cache );
		try
		{
			File.WriteAllText( Path.Combine( loose, "models", "both.vmdl_c" ), "loose" );
			File.WriteAllText( Path.Combine( menu, "models", "both.vmdl_c" ), "menu" );
			File.WriteAllText( Path.Combine( menu, "models", "packaged.vmdl_c" ), "menu" );
			File.WriteAllText( Path.Combine( menu, "models", "grass.vmdl_c" ), "menu" );
			var packaged = Path.Combine( cache, "packaged.0123abcd.vmdl_c" );
			File.WriteAllText( packaged, "package" );

			var files = new ContentFiles( loose ) { Resolve = path => path == "models/packaged.vmdl_c" ? packaged : null };
			files.AddFallback( menu );
			files.AddFallback( Path.Combine( folder, "missing" ) );

			Assert.AreEqual( 1, files.Fallbacks.Count, "a folder that isn't there isn't searched" );
			Assert.AreEqual( "loose", System.Text.Encoding.UTF8.GetString( files.TryRead( "models/both.vmdl" ) ), "the folders win" );
			Assert.AreEqual( packaged, files.Find( "models/packaged.vmdl" ), "a package's file wins" );
			Assert.AreEqual( Path.GetFullPath( Path.Combine( menu, "models", "grass.vmdl_c" ) ), Path.GetFullPath( files.Find( "models/grass.vmdl" ) ), "what only a fallback has is found there" );
			Assert.IsNull( files.Find( "models/none.vmdl" ) );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>The game's content searches the menu's compiled files last, as the engine's GAME search path does.</summary>
	[TestMethod]
	public void TheGameSearchesTheMenusCompiledFilesLast()
	{
		var files = ContentFiles.FindGame();
		if ( files.Roots.Count == 0 ) Assert.Inconclusive( "No game folder" );
		var transients = Path.Combine( Path.GetDirectoryName( files.Roots[0] ), "addons", "menu", "Transients" );
		if ( !Directory.Exists( transients ) ) Assert.Inconclusive( "The menu's compiled files aren't built" );

		Assert.AreEqual( Path.GetFullPath( transients ), files.Fallbacks[^1] );
		CollectionAssert.DoesNotContain( files.Roots.ToList(), Path.GetFullPath( transients ), "the folders stay core and citizen" );
	}
}
