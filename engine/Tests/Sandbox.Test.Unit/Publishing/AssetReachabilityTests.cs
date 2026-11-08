using Editor;
using Sandbox.DataModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using static Editor.ProjectPublisher.PackageManifest;

[TestClass]
public class AssetReachabilityTests
{
	static HashSet<string> Roots( string[] assets, string[] configured = null, string patterns = null )
		=> FindPublishRoots( assets, x => [x], configured ?? [], patterns );

	[TestMethod]
	public void ResourcesFolderIsAnExplicitRoot()
	{
		var roots = Roots( ["Resources/models/hat.vmdl", "resources/items/hat.item", "models/unused.vmdl", "old/resources/hat.vmdl", "resources_backup/hat.vmdl"] );
		CollectionAssert.AreEquivalent( new[] { "Resources/models/hat.vmdl", "resources/items/hat.item" }, roots.ToArray() );
	}

	[TestMethod]
	public void AllConfiguredScenesAreRoots()
	{
		var config = new ProjectConfig();
		config.SetMeta( "StartupScene", "scenes/start.scene" );
		config.SetMeta( "MapStartupScene", "scenes/map.scene" );
		config.SetMeta( "DedicatedServerStartupScene", "scenes/server.scene" );
		config.SetMeta( "SystemScene", "scenes/system.scene" );
		string[] scenes = ["scenes/start.scene", "scenes/map.scene", "scenes/server.scene", "scenes/system.scene"];
		var roots = Roots( [.. scenes, "scenes/unused.scene"], GetConfiguredScenePaths( config ).ToArray() );
		CollectionAssert.AreEquivalent( scenes, roots.ToArray() );
	}

	[TestMethod]
	public void MissingStartupSettingUsesRuntimeDefault()
	{
		var config = new ProjectConfig();
		Assert.IsFalse( config.PublishUsedAssetsOnly );
		CollectionAssert.AreEqual( new[] { "start.scene" }, Roots( ["start.scene", "other.scene"], GetConfiguredScenePaths( config ).ToArray() ).ToArray() );
	}

	[TestMethod]
	public void SettingsUseExactPathsNotFilenamesOrPrefixes()
	{
		var paths = GetSettingsPaths( """
			{ "scenes/unused.scene": false, "Scenes": ["SCENES\\start.scene"],
			  "Other": "hat.vmdl", "Pattern": "models/*", "Folder": "textures/", "Number": 1 }
			""" ).ToArray();
		var roots = Roots( ["scenes/start.scene", "scenes/unused.scene", "models/hat.vmdl", "textures/test.vtex"], paths );
		CollectionAssert.AreEqual( new[] { "scenes/start.scene" }, roots.ToArray() );
	}

	[TestMethod]
	public void SettingsSupportFileReferencesCommentsAndTrailingCommas()
	{
		var paths = GetSettingsPaths( """
			{ // Serialized settings
			  "Data": { "$reference_type": "file", "Path": "data/items.json" },
			  "Image": "ui/cursor.png",
			  "External": "https://example.com/icon.png",
			  "Outside": "../outside.json",
			}
			""" ).ToArray();
		CollectionAssert.AreEquivalent( new[] { "data/items.json", "ui/cursor.png" }, paths );
	}

	[TestMethod]
	public void InvalidSettingsReportTheFileAndContinueReadingOtherSettings()
	{
		var manifest = new ProjectPublisher.PackageManifest();
		var malformed = new ProjectPublisher.ProjectFile
		{
			Name = "ProjectSettings/Broken.config",
			Contents = System.Text.Encoding.UTF8.GetBytes( "{ not json" )
		};
		Assert.AreEqual( 0, manifest.GetSettingsPaths( malformed ).Count() );
		Assert.AreEqual( 1, manifest.Errors.Count );
		StringAssert.Contains( manifest.Errors[0], malformed.Name );
		var valid = new ProjectPublisher.ProjectFile
		{
			Name = "ProjectSettings/Valid.config",
			Contents = System.Text.Encoding.UTF8.GetBytes( "{\"Model\":\"models/hat.vmdl\"}" )
		};
		CollectionAssert.AreEqual( new[] { "models/hat.vmdl" }, manifest.GetSettingsPaths( valid ).ToArray() );
		Assert.AreEqual( 1, manifest.Errors.Count );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MissingOrLockedSettingsReportAnError( bool locked )
	{
		var path = Path.Combine( Path.GetTempPath(), $"publish_settings_{Guid.NewGuid():N}.config" );
		try
		{
			File.WriteAllText( path, "{}" );
			if ( !locked ) File.Delete( path );
			using var stream = locked ? File.Open( path, FileMode.Open, FileAccess.ReadWrite, FileShare.None ) : null;
			var manifest = new ProjectPublisher.PackageManifest();
			var file = new ProjectPublisher.ProjectFile { Name = "ProjectSettings/Test.config", AbsolutePath = path };
			Assert.AreEqual( 0, manifest.GetSettingsPaths( file ).Count() );
			Assert.AreEqual( 1, manifest.Errors.Count );
			StringAssert.Contains( manifest.Errors[0], file.Name );
		}
		finally
		{
			File.Delete( path );
		}
	}

	[TestMethod]
	public async Task IncludedSettingsHaveCanonicalNamesAndResourceReferences()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"publish_settings_{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( directory, "ProjectSettings" ) );
		try
		{
			File.WriteAllText( Path.Combine( directory, "ProjectSettings", "Test.config" ), "{\"Model\":\"models/hat.vmdl\"}" );
			var manifest = new ProjectPublisher.PackageManifest();
			await manifest.IncludeFiles( directory, "ProjectSettings/*", default );
			var file = manifest.Assets.Single();
			Assert.AreEqual( "ProjectSettings/Test.config", file.Name );
			CollectionAssert.AreEqual( new[] { "models/hat.vmdl" }, manifest.GetSettingsPaths( file ).ToArray() );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public async Task LegacyResourceRulesKeepSpecialCharactersLiteral()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"publish_legacy_{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( directory, "folder" ) );
		try
		{
			foreach ( var name in new[] { "#data.txt", "!data.txt", "data.txt", "folder/hidden.txt" } )
				File.WriteAllText( Path.Combine( directory, name ), "data" );
			const string rules = "// comment\n#data.txt\n!data.txt\nfolder/";
			var legacy = new ProjectPublisher.PackageManifest();
			await legacy.IncludeFiles( directory, rules, default, legacyRules: true );
			CollectionAssert.AreEquivalent( new[] { "#data.txt", "!data.txt" }, legacy.Assets.Select( x => x.Name ).ToArray() );
			var optedIn = new ProjectPublisher.PackageManifest();
			await optedIn.IncludeFiles( directory, rules, default );
			CollectionAssert.AreEqual( new[] { "folder/hidden.txt" }, optedIn.Assets.Select( x => x.Name ).ToArray() );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void BlanketWildcardAlwaysIncludesMatches()
	{
		var roots = Roots( ["ui/logo.png", "textures/unused.png", "models/unused.vmdl"], patterns: "*.png" );
		CollectionAssert.AreEquivalent( new[] { "ui/logo.png", "textures/unused.png" }, roots.ToArray() );
	}

	[TestMethod]
	public void WildcardsRespectTheWholePattern()
	{
		var roots = Roots( ["icons/icon_a.png", "icons/icon_ab.png", "icons/icon_a.jpg", "icons/other.png", "ui/icon_a.png"], patterns: "icons/icon_?.png" );
		CollectionAssert.AreEqual( new[] { "icons/icon_a.png" }, roots.ToArray() );
	}

	[TestMethod]
	public void IncludesIgnoreCommentsAndMatchCaseInsensitively()
	{
		var roots = Roots( ["models/hat.vmdl", "models/unused.vmdl"], patterns: "  // models/*\r\n  MODELS\\HAT.VMDL \r\n" );
		CollectionAssert.AreEqual( new[] { "models/hat.vmdl" }, roots.ToArray() );
	}

	[TestMethod]
	public void SourceAndCompiledPathsCanExplicitlyIncludeAnAsset()
	{
		foreach ( var pattern in new[] { "sounds/theme.mp3", "sounds/theme.vsnd", "sounds/theme.vsnd_c" } )
		{
			var roots = FindPublishRoots( new[] { "theme" }, _ => ["sounds/theme.mp3", "sounds/theme.vsnd", "sounds/theme.vsnd_c"], [], pattern );
			Assert.IsTrue( roots.Contains( "theme" ), pattern );
		}
	}

	[TestMethod]
	public void AssetsOutsideExplicitRootsAreOmitted()
	{
		Assert.AreEqual( 0, Roots( ["models/hat.vmdl", "items/hat.item", "scenes/unused.scene"] ).Count );
	}

	[TestMethod]
	public async Task DependenciesOutsideResourcesAreIncludedOnceEvenWithCycles()
	{
		var edges = new Dictionary<string, string[]>
		{
			["resources/hat.prefab"] = ["models/hat.vmdl", "materials/hat.vmat"],
			["models/hat.vmdl"] = ["materials/hat.vmat"],
			["materials/hat.vmat"] = ["textures/hat.vtex", "resources/hat.prefab"],
			["textures/hat.vtex"] = []
		};
		var visited = new List<string>();
		var used = await FindReachableAssets( new[] { "resources/hat.prefab" }, asset =>
		{
			visited.Add( asset );
			return Task.FromResult<IEnumerable<string>>( edges[asset] );
		} );
		CollectionAssert.AreEquivalent( edges.Keys.ToArray(), used.ToArray() );
		CollectionAssert.AreEquivalent( edges.Keys.ToArray(), visited );
	}

	[TestMethod]
	public async Task DependencyCollectionHonorsCancellation()
	{
		using var cancel = new CancellationTokenSource();
		cancel.Cancel();
		await Assert.ThrowsExceptionAsync<OperationCanceledException>( () => FindReachableAssets( new[] { "start.scene" }, _ => Task.FromResult<IEnumerable<string>>( [] ), cancel.Token ) );
	}

	[TestMethod]
	public async Task ExplicitLooseFileIncludesAreNeverFiltered()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"publish_includes_{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( directory, "ui" ) );
		try
		{
			File.WriteAllText( Path.Combine( directory, "ui", "unused.png" ), "image" );
			File.WriteAllText( Path.Combine( directory, "ui", "other.txt" ), "text" );
			var manifest = new ProjectPublisher.PackageManifest();
			await manifest.IncludeFiles( directory, " // *.txt\n *.png", default );
			CollectionAssert.AreEqual( new[] { "ui/unused.png" }, manifest.Assets.Select( x => x.Name ).ToArray() );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	[DataRow( "resources/data.json", true )]
	[DataRow( "resources/ui/logo.png", true )]
	[DataRow( "resources/fonts/test.woff2", true )]
	[DataRow( "resources/test.dll", false )]
	[DataRow( "resources/test.cs", false )]
	[DataRow( "resources/test.fbx", false )]
	[DataRow( "resources/test.vmdl.meta", false )]
	[DataRow( "resources/test.scene_d", false )]
	[DataRow( "resources/test.vmdl_c", false )]
	[DataRow( "resources/.cache/test.png", false )]
	public void ResourcesLooseFilesExcludeCodeAndAssetSidecars( string path, bool allowed )
	{
		Assert.AreEqual( allowed, ResourceFileAllowed( path ) );
	}
}
