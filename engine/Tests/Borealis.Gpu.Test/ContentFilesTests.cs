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
}
