using System;
using System.IO;
using System.Text.Json;
using Sandbox.DataModel;

namespace ProjectTests;

/// <summary>
/// Addon paths register projects and activate engine addons according to their type.
/// </summary>
[TestClass]
public class ProjectDiscoveryTest
{
	/// <summary>
	/// Addon paths populate one project list across project types, tolerate broken configs and reuse known projects.
	/// </summary>
	[TestMethod]
	public void AddonPathsPopulateSharedProjectList()
	{
		var root = Path.Combine( Environment.CurrentDirectory, "core", $"test_addons_{Guid.NewGuid():N}" );
		Project.Clear();

		try
		{
			WriteProject( root, "game", "game", "test_game.sbproj" );
			WriteProject( root, "content", "content", ".sbproj" );
			WriteProject( root, "library", "library", ".sbproj" );
			WriteProject( root, "nested/game", "game", ".sbproj" );
			WriteProject( root, "more/second", "game", "second.sbproj" );
			WriteProject( root, "menu", "menu", ".sbproj" );
			Directory.CreateDirectory( Path.Combine( root, "broken" ) );
			File.WriteAllText( Path.Combine( root, "broken", ".sbproj" ), "invalid json" );

			Project.AddAddonPath( root );
			Assert.AreEqual( 4, Project.All.Count );
			var game = Project.All.Single( x => x.Config.Ident == "game" );
			Assert.IsTrue( Project.All.Any( x => x.Config.Type == "content" ) );
			Assert.IsTrue( Project.All.Where( x => !x.IsBuiltIn ).All( x => !x.Active && !x.HasCompiler ) );
			Assert.IsFalse( Project.Libraries.Any() );
			var menu = Project.All.Single( x => x.IsBuiltIn );
			Assert.IsTrue( menu.Active );
			Assert.IsTrue( menu.HasCompiler );

			Project.AddAddonPath( Path.Combine( root, "more" ) );
			Assert.AreEqual( 5, Project.All.Count );
			Assert.IsTrue( Project.All.Any( x => x.Config.Ident == "second" ) );

			Project.AddAddonPath( Path.Combine( root, "." ) );
			Project.AddAddonPath( Path.Combine( root, "more" ) );
			Project.AddAddonPath( Path.Combine( root, "missing" ) );
			Assert.AreEqual( 5, Project.All.Count );
			Assert.AreSame( game, Project.All.Single( x => x.Config.Ident == "game" ) );
			Assert.AreSame( menu, Project.All.Single( x => x.IsBuiltIn ) );

			var library = Project.AddFromFile( Path.Combine( root, "library", ".sbproj" ) );
			Assert.IsTrue( library.Active && library.HasCompiler );
			Assert.AreSame( library, Project.Libraries.Single() );
		}
		finally
		{
			Project.Clear();
			Directory.Delete( root, true );
		}
	}

	/// <summary>
	/// Project configs select editor, client and headless environments without special project names.
	/// </summary>
	[TestMethod]
	[DataRow( false, false, false, true )]
	[DataRow( false, true, false, false )]
	[DataRow( true, false, true, true )]
	[DataRow( true, true, true, true )]
	public void LoadingRulesMatchEnvironment( bool isEditor, bool isHeadless, bool loadTools, bool loadMenu )
	{
		foreach ( var type in new[] { "game", "library", "addon", "content" } )
		{
			Assert.IsTrue( new ProjectConfig { Type = type }.ShouldLoad( isEditor, isHeadless ) );
		}

		Assert.AreEqual( loadTools, new ProjectConfig { Type = "tool" }.ShouldLoad( isEditor, isHeadless ) );
		Assert.AreEqual( loadMenu, new ProjectConfig { Type = "menu" }.ShouldLoad( isEditor, isHeadless ) );
	}

	static void WriteProject( string root, string folder, string type, string filename )
	{
		var directory = Path.Combine( root, folder );
		Directory.CreateDirectory( Path.Combine( directory, "Code" ) );
		File.WriteAllText( Path.Combine( directory, filename ), JsonSerializer.Serialize( new ProjectConfig
		{
			Title = folder,
			Org = "local",
			Ident = Path.GetFileName( folder ),
			Type = type,
			Schema = 1
		} ) );
	}
}
