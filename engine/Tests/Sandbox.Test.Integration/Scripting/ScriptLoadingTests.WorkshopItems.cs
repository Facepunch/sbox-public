using Sandbox.Network;
using Sandbox.Tasks;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Package downloads and network requirements share one installed item until the final owner releases it.
	/// </summary>
	[TestMethod]
	public void WorkshopMountsSharePackageFilesAndPreserveCodeOnlyViews()
	{
		var folder = CreateWorkshopContent( "map" );
		WorkshopFileSystem first = null;
		BaseFileSystem packageFiles = null;
		WorkshopFileSystem code = null;
		try
		{
			AsyncPump.RunSynchronously( async () =>
			{
				first = await WorkshopFileSystem.OpenInstalledAsync( 4321, folder, default );
				var package = new RemotePackage { Org = new Package.Organization { Ident = "test" }, Ident = "workshop-map", TypeName = "map", Version = new PackageRevision { Meta = "{\"WorkshopId\":4321}" } };
				packageFiles = await package.Download();
				code = await WorkshopFileSystem.OpenInstalledAsync( 4321, folder, default, codeOnly: true );
			} );

			Assert.IsInstanceOfType<WorkshopFileSystem>( packageFiles );
			Assert.IsTrue( packageFiles.FileExists( "content.txt" ) );
			Assert.IsTrue( code.FileExists( ".bin/package.cll" ) );
			Assert.IsFalse( code.FileExists( "content.txt" ) );
			Assert.IsFalse( packageFiles.FileExists( ".bin/unsafe.dll" ) );
			first.Dispose();
			Assert.IsTrue( WorkshopFileSystem.IsInUse( 4321 ) );
			Assert.AreEqual( "content", packageFiles.ReadAllText( "content.txt" ) );
			packageFiles.Dispose();
			Assert.IsTrue( WorkshopFileSystem.IsInUse( 4321 ) );
			code.Dispose();
			Assert.IsFalse( WorkshopFileSystem.IsInUse( 4321 ) );
		}
		finally
		{
			first?.Dispose();
			packageFiles?.Dispose();
			code?.Dispose();
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A map package registers its Workshop ID instead of becoming a server package requirement.
	/// </summary>
	[TestMethod]
	public void WorkshopMapUsesItemTable()
	{
		var folder = CreateWorkshopContent( "map" );
		var previousItems = WorkshopItems.Current;
		var previousSystem = Networking.System;
		var mounted = new AggregateFileSystem();
		var items = new WorkshopItems( ( id, token ) => WorkshopFileSystem.OpenInstalledAsync( id, folder, token ) );
		try
		{
			Networking.System = null;
			WorkshopItems.Current = items;
			context.FileMount = mounted;
			AsyncPump.RunSynchronously( async () =>
			{
				using var installed = await WorkshopFileSystem.OpenInstalledAsync( 4321, folder, default );
				var package = new RemotePackage { Org = new Package.Organization { Ident = "test" }, Ident = "workshop-map", TypeName = "map", Version = new PackageRevision { Meta = "{\"WorkshopId\":4321}" } };
				Assert.IsNotNull( await package.MountAsync() );
				Assert.IsTrue( package.IsMounted() );
				Assert.AreEqual( Workshop.ItemType.Map, items.StringTable.Entries["4321"].Read<WorkshopItems.ItemInfo>().Type );
				items.RemoveRequirement( 4321 );
				Assert.AreEqual( 0, items.StringTable.Entries.Count );
			} );
		}
		finally
		{
			items.Clear();
			context.FileMount = files;
			mounted.Dispose();
			WorkshopItems.Current = previousItems;
			Networking.System = previousSystem;
			Directory.Delete( folder, true );
		}
	}

	private static string CreateWorkshopContent( string type )
	{
		var folder = Directory.CreateTempSubdirectory( "sbox-workshop-item-" ).FullName;
		File.WriteAllText( Path.Combine( folder, "metadata.json" ), Json.Serialize( new WorkshopItemMetaData { Title = "Test content", TypeName = type, Meta = "{}" } ) );
		File.WriteAllText( Path.Combine( folder, "content.txt" ), "content" );
		Directory.CreateDirectory( Path.Combine( folder, ".bin" ) );
		File.WriteAllText( Path.Combine( folder, ".bin", "package.cll" ), "code archive" );
		File.WriteAllText( Path.Combine( folder, ".bin", "unsafe.dll" ), "not an assembly" );
		return folder;
	}

	/// <summary>
	/// Workshop addons own their network requirements, defer client resources, and release their files on reset.
	/// </summary>
	[TestMethod]
	[DataRow( true, false )]
	[DataRow( false, false )]
	[DataRow( false, true )]
	public void WorkshopAddonLoadsWithoutPackage( bool host, bool migrate )
	{
		var previousSystem = Networking.System;
		var previousGame = Application.GameIdent;
		var mounted = new AggregateFileSystem();
		var folder = Directory.CreateTempSubdirectory( "sbox-workshop-addon-" ).FullName;
		WorkshopFileSystem mount = null;
		int downloads = 0;
		var addons = new WorkshopItems( async ( id, token ) =>
		{
			downloads++;
			mount = await WorkshopFileSystem.OpenInstalledAsync( id, folder, token );
			return mount;
		} );

		try
		{
			Networking.System = null;
			Application.GameIdent = "org.game";
			var scripts = new StringTable( "Scripts", true );
			Game.Scripting.InitializeNetworking( scripts, isClient: false );
			File.WriteAllText( Path.Combine( folder, "metadata.json" ), Json.Serialize( new WorkshopItemMetaData
			{
				TypeName = "addon",
				Meta = "{\"ParentPackage\":\"org.game\"}"
			} ) );
			File.WriteAllText( Path.Combine( folder, "direct-addon.csx" ), "[Global] int WorkshopStarts = 0; WorkshopStarts++; class WorkshopMarker {}" );
			File.WriteAllText( Path.Combine( folder, "content.txt" ), "addon" );
			File.WriteAllText( Path.Combine( folder, "ignored.dll" ), "not an assembly" );
			files.WriteAllText( "content.txt", "game" );
			mounted.Mount( files );
			context.FileMount = mounted;

			AsyncPump.RunSynchronously( async () =>
			{
				if ( host )
				{
					Assert.IsNotNull( await addons.LoadAsync( 1234, Workshop.ItemType.Addon ) );
					Assert.IsNotNull( await addons.LoadAsync( 1234, Workshop.ItemType.Addon ) );
				}
				else
				{
					addons.StringTable.Set( "1234", new WorkshopItems.ItemInfo { Id = 1234, Type = Workshop.ItemType.Addon } );
					Assert.IsTrue( await addons.InstallAllAsync() );
					Assert.IsTrue( addons.HasPending );
					await addons.LoadResourcesAsync();
					Assert.AreEqual( 0, scripts.Entries.Count, "Mounting must not execute scripts before the host is ready." );

					if ( migrate )
					{
						await addons.OnBecameHost();
					}
					else
					{
						addons.StringTable.Set( "1234", new WorkshopItems.ItemInfo { Id = 1234, Type = Workshop.ItemType.Addon, ResourcesReady = true } );
						await addons.LoadResourcesAsync();
					}
					Assert.IsTrue( await addons.InstallAllAsync() );
					await addons.LoadResourcesAsync();
				}

				var requirement = addons.StringTable.Entries["1234"].Read<WorkshopItems.ItemInfo>();
				Assert.AreEqual( 1234UL, requirement.Id );
				Assert.IsTrue( requirement.ResourcesReady );
				Assert.IsFalse( addons.HasPending );
				Assert.IsFalse( addons.HasFailures );
				Assert.IsTrue( scripts.Entries.ContainsKey( "direct-addon.csx" ) );
			} );

			Assert.AreEqual( 1, downloads );
			Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "WorkshopStarts" ) );
			Assert.IsTrue( WorkshopFileSystem.IsInUse( 1234 ) );
			Assert.IsFalse( mount.FileExists( "metadata.json" ) );
			Assert.IsFalse( mount.FileExists( "ignored.dll" ) );
			Assert.AreEqual( "addon", mounted.ReadAllText( "content.txt" ) );
			addons.Clear();
			Assert.IsFalse( WorkshopFileSystem.IsInUse( 1234 ) );
			Assert.IsFalse( mount.IsValid );
			Assert.AreEqual( "game", mounted.ReadAllText( "content.txt" ) );
		}
		finally
		{
			addons.Clear();
			context.FileMount = files;
			mounted.Dispose();
			Directory.Delete( folder, true );
			Networking.System = previousSystem;
			Application.GameIdent = previousGame;
		}
	}

	/// <summary>
	/// Failed host downloads are not required by clients, and clearing a session cancels its pending downloads.
	/// </summary>
	[TestMethod]
	public void WorkshopAddonFailureAndCancellationReleaseRequirements()
	{
		var addons = new WorkshopItems( ( id, token ) => Task.FromException<WorkshopFileSystem>( new IOException( "Unavailable addon" ) ) );
		var pending = new WorkshopItems( async ( id, token ) =>
		{
			await Task.Delay( Timeout.Infinite, token );
			return null;
		} );

		try
		{
			AsyncPump.RunSynchronously( async () =>
			{
				Assert.IsNull( await addons.LoadAsync( 1234, Workshop.ItemType.Addon ) );
				Assert.AreEqual( 0, addons.StringTable.Entries.Count );

				addons.StringTable.Set( "1234", new WorkshopItems.ItemInfo { Id = 1234, Type = Workshop.ItemType.Addon, ResourcesReady = true } );
				Assert.IsFalse( await addons.InstallAllAsync() );
				Assert.IsTrue( addons.HasFailures );

				var loading = pending.LoadAsync( 5678, Workshop.ItemType.Addon );
				Assert.IsTrue( pending.HasPending );
				pending.Clear();
				try
				{
					await loading.WaitAsync( TimeSpan.FromSeconds( 5 ) );
					Assert.Fail( "Clearing the addons must cancel their pending downloads." );
				}
				catch ( OperationCanceledException )
				{
					Assert.IsTrue( loading.IsCanceled );
				}
				Assert.AreEqual( 0, pending.StringTable.Entries.Count );
				Assert.IsFalse( pending.HasPending );
			} );
		}
		finally
		{
			addons.Clear();
			pending.Clear();
		}
	}
}
