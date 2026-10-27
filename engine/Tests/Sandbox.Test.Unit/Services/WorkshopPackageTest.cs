using Sandbox;

namespace ServicesTests;

[TestClass]
public class WorkshopPackageTest
{
	[TestMethod]
	public void ClothingWorkshopIdDoesNotInstallFromWorkshop()
	{
		// Clothing publishes a WorkshopId for its steam inventory item, but its files are on the backend
		var clothing = new RemotePackage { TypeName = "clothing", Version = new PackageRevision { Meta = "{\"WorkshopId\":1234}" } };

		Assert.AreEqual( 1234UL, clothing.WorkshopId );
		Assert.IsFalse( clothing.InstallsFromWorkshop );
	}

	[TestMethod]
	public void MapWithWorkshopIdInstallsFromWorkshop()
	{
		var map = new RemotePackage { TypeName = "map", Version = new PackageRevision { Meta = "{\"WorkshopId\":1234}" } };

		Assert.IsTrue( map.InstallsFromWorkshop );
	}

	[TestMethod]
	public void GameWithWorkshopIdInstallsFromWorkshop()
	{
		var game = new RemotePackage { TypeName = "game", Version = new PackageRevision { Meta = "{\"WorkshopId\":1234}" } };

		Assert.IsTrue( game.InstallsFromWorkshop );
	}

	[TestMethod]
	public void GameWithoutWorkshopIdDownloadsFromBackend()
	{
		// Games published before they went on the workshop
		var game = new RemotePackage { TypeName = "game", Version = new PackageRevision { Meta = "{}" } };

		Assert.IsFalse( game.InstallsFromWorkshop );
	}
}
