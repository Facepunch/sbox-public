using System;
using System.IO;
using System.Text.Json;
using Sandbox.DataModel;

namespace ProjectTests;

/// <summary>
/// Installed games can be browsed without loading their code or borrowing their cloud presentation.
/// </summary>
[TestClass]
public class LocalPackagePresentationTest
{
	/// <summary>
	/// Presentation files supply text and ordered artwork without cloud data or project config entries.
	/// </summary>
	[TestMethod]
	public void PresentationFilesStayLocal()
	{
		var root = Path.Combine( Environment.CurrentDirectory, "core", $"test_menu_{Guid.NewGuid():N}" );
		Project.Clear();

		try
		{
			var game = Path.Combine( root, "game" );
			Directory.CreateDirectory( game );
			File.WriteAllText( Path.Combine( game, ".sbproj" ), JsonSerializer.Serialize( new ProjectConfig
			{
				Title = "Local game",
				Org = "local",
				Ident = "game",
				Type = "game",
				Schema = 1
			} ) );
			var presentation = Path.Combine( root, "game", "presentation" );
			Directory.CreateDirectory( Path.Combine( presentation, "screenshots" ) );
			File.WriteAllText( Path.Combine( presentation, "description.md" ), "Local summary\n\nControls <jump> & build" );
			File.WriteAllBytes( Path.Combine( presentation, "thumb-square.png" ), [] );
			File.WriteAllBytes( Path.Combine( presentation, "screenshots", "02.jpg" ), [] );
			File.WriteAllBytes( Path.Combine( presentation, "screenshots", "01.png" ), [] );
			File.WriteAllText( Path.Combine( presentation, "screenshots", "notes.txt" ), "Not an image" );
			File.WriteAllBytes( Path.Combine( presentation, "screenshots", "source.tga" ), [] );

			Package.Cache( new RemotePackage
			{
				Org = new Package.Organization { Ident = "local", Title = "Cloud organization" },
				Ident = "game",
				Summary = "Cloud summary",
				Thumb = "https://example.com/cloud.png"
			}, false );

			Project.AddAddonPath( root );
			var project = Project.All.Single( x => x.Config.Type == "game" );
			var package = project.Package;
			Assert.AreEqual( "local.game", package.FullIdent );
			Assert.AreEqual( "Local summary", package.Summary );
			Assert.AreEqual( "Local summary<br><br>Controls &lt;jump&gt; &amp; build", package.Description );
			Assert.AreEqual( "local", package.Org.Title );
			Assert.AreEqual( "project:local.game/presentation/thumb-square.png", package.Thumb );
			Assert.AreEqual( package.Thumb, package.ThumbWide );
			Assert.AreEqual( package.Thumb, package.ThumbTall );
			Assert.AreEqual( 2, package.Screenshots.Length );
			Assert.AreEqual( "project:local.game/presentation/screenshots/01.png", package.Screenshots[0].Url );
			Assert.AreEqual( "project:local.game/presentation/screenshots/02.jpg", package.Screenshots[1].Url );

			File.WriteAllBytes( Path.Combine( presentation, "thumb-wide.png" ), [] );
			File.WriteAllBytes( Path.Combine( presentation, "thumb-tall.png" ), [] );

			Project.Clear();
			Project.AddAddonPath( root );
			project = Project.All.Single( x => x.Config.Type == "game" );
			Assert.AreEqual( "project:local.game/presentation/thumb-wide.png", project.Package.ThumbWide );
			Assert.AreEqual( "project:local.game/presentation/thumb-tall.png", project.Package.ThumbTall );

			Directory.Delete( presentation, true );

			Project.Clear();
			Project.AddAddonPath( root );
			project = Project.All.Single( x => x.Config.Type == "game" );
			Assert.IsNull( project.Package.Thumb );
			Assert.IsNull( project.Package.ThumbWide );
			Assert.IsNull( project.Package.ThumbTall );
			Assert.IsNull( project.Package.Description );
			Assert.AreEqual( 0, project.Package.Screenshots.Length );
			Assert.IsNull( project.Package.Summary );
		}
		finally
		{
			Package.ClearCache( "local.game" );
			Project.Clear();
			Directory.Delete( root, true );
		}
	}

}
