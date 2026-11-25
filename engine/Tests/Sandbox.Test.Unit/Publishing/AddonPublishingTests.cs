using Editor;
using Sandbox;
using Sandbox.DataModel;
using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Addon publication permits assets and scripts without allowing compiled gameplay packages.
/// </summary>
[TestClass, DoNotParallelize]
public class AddonPublishingTests
{
	string directory;
	Project project;

	/// <summary>
	/// Create an addon with the generated Code project files and a script asset.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine( Path.GetTempPath(), $"addon_publish_{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( directory, "Code", "Properties" ) );
		Directory.CreateDirectory( Path.Combine( directory, "Assets", "scripts" ) );
		File.WriteAllText( Path.Combine( directory, "Code", "addon.csproj" ), "<Project />" );
		File.WriteAllText( Path.Combine( directory, "Code", "panel.scss" ), "Panel {}" );
		File.WriteAllText( Path.Combine( directory, "Code", "Properties", "launchSettings.json" ), "{}" );
		File.WriteAllText( Path.Combine( directory, "Assets", "scripts", "toy.csx" ), "Log.Info( 1 );" );
		File.WriteAllText( Path.Combine( directory, "Assets", "settings.json" ), "{}" );
		project = new Project( new DirectoryInfo( directory ) )
		{
			Config = new ProjectConfig { Type = "addon", Resources = "*", PublishUsedAssetsOnly = false }
		};
	}

	/// <summary>
	/// Release the project filesystems before removing the temporary project.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		project?.Dispose();
		Directory.Delete( directory, true );
	}

	/// <summary>
	/// Maps and addons collect assets and scripts without Code folders; games still collect both.
	/// </summary>
	[TestMethod]
	[DataRow( "addon", false )]
	[DataRow( "map", false )]
	[DataRow( "game", true )]
	public void ContentPublishingSkipsCodeFolders( string type, bool includesCode )
	{
		project.Config.Type = type;
		var publisher = Sandbox.Tasks.AsyncPump.RunSynchronously( () => ProjectPublisher.FromProject( project ) );
		var expected = new List<string> { "scripts/toy.csx", "settings.json" };

		if ( includesCode )
			expected.Add( "panel.scss" );

		CollectionAssert.AreEquivalent( expected, publisher.Manifest.Assets.Select( x => x.Name ).ToArray() );
	}

	/// <summary>
	/// Addon gameplay sources produce no assemblies and explain why they are skipped.
	/// </summary>
	[TestMethod]
	[DataRow( "Gameplay.cs" )]
	[DataRow( "Panel.RAZOR" )]
	public void GameplaySourcesAreSkippedWithFeedback( string filename )
	{
		File.WriteAllText( Path.Combine( directory, "Code", filename ), "invalid code" );
		var messages = new List<string>();
		var assemblies = Sandbox.Tasks.AsyncPump.RunSynchronously( () => EditorUtility.Projects.Compile( project, messages.Add ) );
		Assert.AreEqual( 0, assemblies.Length );
		StringAssert.Contains( string.Join( "\n", messages ), "will not be published" );
		StringAssert.Contains( string.Join( "\n", messages ), ".csx" );
	}

	/// <summary>
	/// Failed first submissions remain new-item retries; successful IDs survive later failures.
	/// </summary>
	[TestMethod]
	public async Task WorkshopUploadRemembersOnlySuccessfulSubmissions()
	{
		var publisher = await ProjectPublisher.FromProject( project );
		Assert.AreEqual( 0UL, publisher.WorkshopTargetItemId );
		Assert.ThrowsException<Exception>( () => publisher.CompleteWorkshopUpload( 0 ) );
		Assert.AreEqual( 0UL, publisher.WorkshopTargetItemId );

		publisher.CompleteWorkshopUpload( 123 );
		Assert.AreEqual( 123UL, publisher.WorkshopTargetItemId );
		Assert.ThrowsException<Exception>( () => publisher.CompleteWorkshopUpload( 0 ) );
		Assert.AreEqual( 123UL, publisher.WorkshopTargetItemId );
		Assert.AreEqual( 123UL, await ProjectPublisher.GetWorkshopItemIdAsync( project ) );
	}

	/// <summary>
	/// Workshop-only projects prepare their content without a backend package.
	/// </summary>
	[TestMethod]
	[DataRow( "addon" )]
	[DataRow( "map" )]
	public async Task PreparationNeedsNoBackendPackage( string type )
	{
		project.Config.Type = type;
		project.Config.PackageReferences = ["test.content"];
		project.Config.Org = null;
		project.Config.Ident = null;
		var publisher = await ProjectPublisher.FromProject( project );
		await publisher.PrePublish();

		Assert.IsTrue( ProjectPublisher.PublishesOnlyToWorkshop( project ) );
		Assert.AreEqual( 2, publisher.WorkshopFileCount );
		Assert.AreEqual( 0, publisher.Manifest.Assets.Count );

		var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
		var itemMeta = (Dictionary<string, object>)typeof( ProjectPublisher ).GetProperty( "Meta", flags ).GetValue( publisher );
		Assert.IsFalse( itemMeta.ContainsKey( "PackageReferences" ) );
		var metadata = (string)typeof( ProjectPublisher ).GetField( "WorkshopMetadata", flags ).GetValue( publisher );
		Assert.IsNull( System.Text.Json.Nodes.JsonNode.Parse( metadata )["PackageIdent"] );

		// Retrying an uploaded item must finish without posting the empty backend manifest.
		var stage = typeof( ProjectPublisher ).GetField( "workshopStage", flags );
		stage.SetValue( publisher, Enum.Parse( stage.FieldType, "Done" ) );
		await publisher.Publish();
	}

	/// <summary>
	/// Only files physically inside the shipped content roots are omitted from a map.
	/// </summary>
	[TestMethod]
	[DataRow( "core/materials/stone.vmat_c", true )]
	[DataRow( "addons/content/Assets/materials/stone.vmat_c", true )]
	[DataRow( "addons/content_pack/Assets/materials/stone.vmat_c", false )]
	[DataRow( "addons/mymap/Assets/core/materials/stone.vmat_c", false )]
	public void MapSkipsShippedContent( string path, bool shipped )
	{
		Assert.AreEqual( shipped, ProjectPublisher.PackageManifest.IsShippedMapContent( directory, Path.Combine( directory, path ) ) );
	}

}
