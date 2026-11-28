using Sandbox;
using System.Collections.Generic;

namespace ServicesTests;

[TestClass]
public class WorkshopPackageTest
{
	/// <summary>
	/// A main preview returned by handle alone counts as existing Workshop artwork during publishing.
	/// </summary>
	[TestMethod]
	public void PreviewHandleWithoutUrlCountsAsExistingArtwork()
	{
		var item = new Workshop.Item { Id = 123, PreviewHandle = 456 };

		Assert.AreEqual( "workshop://123/thumb", item.Thumbnail );
		Assert.IsTrue( item.HasImages );
	}

	/// <summary>
	/// Steam supplies addon identity; publisher-supplied package names and item IDs cannot redirect a download.
	/// </summary>
	[TestMethod]
	public void AddonLoadsFromWorkshopMetadataWithoutBackendIdentity()
	{
		var item = new Workshop.Item
		{
			Id = 1234,
			Title = "Rust props",
			Tags = ["addon"],
			KeyValues = new Dictionary<string, string> { ["package"] = "other.package" },
			Metadata = System.Text.Json.JsonSerializer.Serialize( new WorkshopItemMetaData
			{
				TypeName = "addon",
				PackageIdent = "other.package",
				Meta = "{\"WorkshopId\":999,\"ParentPackage\":\"org.game\"}",
				ApiVersion = 123
			} )
		};

		Assert.IsTrue( Workshop.IsAddonForGame( item, "org.game" ) );
		Assert.IsFalse( Workshop.IsAddonForGame( item, "org.other" ) );
	}

	/// <summary>
	/// Missing or invalid metadata cannot turn a Workshop item into a loadable addon.
	/// </summary>
	[TestMethod]
	[DataRow( null )]
	[DataRow( "bad json" )]
	[DataRow( "{}" )]
	[DataRow( "{\"TypeName\":\"game\"}" )]
	[DataRow( "{\"TypeName\":\"addon\",\"Meta\":\"[]\"}" )]
	[DataRow( "{\"TypeName\":\"addon\",\"Meta\":\"{\\\"ParentPackage\\\":123}\"}" )]
	public void InvalidAddonMetadataIsRejected( string metadata )
	{
		Assert.IsFalse( Workshop.IsAddonForGame( new Workshop.Item { Id = 1234, Tags = ["addon"], Metadata = metadata }, "org.game" ) );
	}

	/// <summary>
	/// An addon needs a real Steam ID and metadata targeting the current game.
	/// </summary>
	[TestMethod]
	[DataRow( "addon", 1234, "org.game", true )]
	[DataRow( "Addon", 1234, "ORG.GAME#42", true )]
	[DataRow( "addon", 1234, "https://sbox.game/org/game", true )]
	[DataRow( "game", 1234, "org.game", false )]
	[DataRow( "library", 1234, "org.game", false )]
	[DataRow( "addon", 0, "org.game", false )]
	[DataRow( "addon", 1234, "org.other", false )]
	[DataRow( "addon", 1234, "other.game", false )]
	[DataRow( "addon", 1234, null, false )]
	[DataRow( null, 0, null, false )]
	public void AddonRequiresValidSteamIdAndTargetGame( string type, int workshopId, string parent, bool expected )
	{
		var item = new Workshop.Item
		{
			Id = (ulong)workshopId,
			Tags = ["addon"],
			Metadata = System.Text.Json.JsonSerializer.Serialize( new WorkshopItemMetaData
			{
				TypeName = type,
				Meta = System.Text.Json.JsonSerializer.Serialize( new { ParentPackage = parent } )
			} )
		};

		Assert.AreEqual( expected, Workshop.IsAddonForGame( item, "org.game" ) );
	}

	/// <summary>
	/// A Workshop mount exposes assets and code archives, but not publisher metadata or executable files.
	/// </summary>
	[TestMethod]
	[DataRow( "models/prop.vmdl_c", true )]
	[DataRow( "scripts/example.csx", true )]
	[DataRow( "metadata.json", false )]
	[DataRow( ".bin/package.cll", true )]
	[DataRow( "plugin.dll", false )]
	[DataRow( "plugin.dll ", false )]
	[DataRow( "../outside.txt", false )]
	public void WorkshopMountFiltersFiles( string path, bool expected )
	{
		Assert.AreEqual( expected, WorkshopFileSystem.IsContentFile( path ) );
	}

	/// <summary>
	/// The publishing wizard sends addons and games to Steam while retaining the standalone and library exclusions.
	/// </summary>
	[TestMethod]
	[DataRow( "addon", false, true )]
	[DataRow( "game", false, true )]
	[DataRow( "addon", true, false )]
	[DataRow( "game", true, false )]
	[DataRow( "library", false, false )]
	public void ProjectUsesExpectedPublisher( string type, bool standaloneOnly, bool workshop )
	{
		var project = new Project( (string)null )
		{
			Config = new() { Type = type, IsStandaloneOnly = standaloneOnly }
		};

		Assert.AreEqual( workshop, Editor.ProjectPublisher.PublishesToWorkshop( project ) );
	}

	[TestMethod]
	public void ClothingWorkshopIdDoesNotInstallFromWorkshop()
	{
		// Clothing publishes a WorkshopId for its steam inventory item, but its files are on the backend
		var clothing = new RemotePackage { TypeName = "clothing", Version = new PackageRevision { Meta = "{\"WorkshopId\":1234}" } };

		Assert.AreEqual( 1234UL, clothing.WorkshopId );
		Assert.IsFalse( clothing.InstallsFromWorkshop );
	}

	/// <summary>
	/// Remote map packages cannot be mounted or installed, including packages that advertise a Workshop ID.
	/// </summary>
	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1234 )]
	public async System.Threading.Tasks.Task MapPackageCannotInstall( int workshopId )
	{
		var map = new RemotePackage
		{
			Org = new Package.Organization { Ident = "test" },
			Ident = "remote-map",
			TypeName = "map",
			Version = new PackageRevision { Meta = Json.Serialize( new { WorkshopId = workshopId } ) }
		};

		Assert.IsFalse( map.InstallsFromWorkshop );
		Assert.IsNull( await map.MountAsync() );
		await Assert.ThrowsExceptionAsync<System.InvalidOperationException>( () => PackageManager.InstallAsync(
			new PackageLoadOptions { Package = map, PackageIdent = map.FullIdent } ) );
	}

	[TestMethod]
	public void GameWithWorkshopIdInstallsFromWorkshop()
	{
		var game = new RemotePackage { TypeName = "game", Version = new PackageRevision { Meta = "{\"WorkshopId\":1234}" } };

		Assert.IsTrue( game.InstallsFromWorkshop );
	}

	/// <summary>
	/// Published addons use Steam for their content, including when the package type uses different casing.
	/// </summary>
	[TestMethod]
	[DataRow( "addon" )]
	[DataRow( "Addon" )]
	public void AddonWithWorkshopIdInstallsFromWorkshop( string type )
	{
		var addon = new RemotePackage { TypeName = type, Version = new PackageRevision { Meta = "{\"WorkshopId\":1234}" } };

		Assert.IsTrue( addon.InstallsFromWorkshop );
	}

	/// <summary>
	/// Packages published before the Workshop migration still use backend downloads.
	/// </summary>
	[TestMethod]
	[DataRow( "game" )]
	[DataRow( "addon" )]
	public void PackageWithoutWorkshopIdDownloadsFromBackend( string type )
	{
		var package = new RemotePackage { TypeName = type, Version = new PackageRevision { Meta = "{}" } };

		Assert.IsFalse( package.InstallsFromWorkshop );
	}
}
