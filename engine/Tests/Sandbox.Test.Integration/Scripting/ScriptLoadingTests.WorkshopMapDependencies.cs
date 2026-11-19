using Sandbox.Network;
using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Maps load package dependencies before their resources and release those requirements on failure or teardown.
	/// </summary>
	[DataTestMethod]
	[DataRow( false, true, false )]
	[DataRow( true, true, false )]
	[DataRow( false, false, false )]
	[DataRow( false, true, true )]
	public void WorkshopMapPreservesDependencyRequirements( bool withCode, bool validMap, bool clearSession )
	{
		var previousSystem = Networking.System;
		var previousPackages = ServerPackages.Current;
		var previousItems = WorkshopItems.Current;
		var downloadsField = typeof( ServerPackages ).GetField( "Downloads", BindingFlags.Static | BindingFlags.NonPublic );
		var previousDownloads = downloadsField.GetValue( null );
		var mounted = new AggregateFileSystem();
		var items = new WorkshopItems();
		var folder = CreateWorkshopContent( validMap ? "map" : "addon" );

		try
		{
			Networking.System = null;
			var packages = new ServerPackages();
			WorkshopItems.Current = items;
			var scripts = new StringTable( "Scripts", true );
			Game.Scripting.InitializeNetworking( scripts, isClient: false );
			files.WriteAllText( "dependency.csx", "[Global] int DependencyStarts = 0; DependencyStarts++; class WorkshopMapDependency { public static int Read() => 42; }" );
			File.WriteAllText( Path.Combine( folder, "map.csx" ), "[Global] int MapSawDependency = 0; MapSawDependency = WorkshopMapDependency.Read();" );
			mounted.Mount( files );
			context.FileMount = mounted;

			const string dependencyIdent = "test.map-dependency";
			var activePackage = CreateNetworkTestPackage();
			var downloads = (IDictionary<string, ServerPackages.PackageDownload>)downloadsField.GetValue( null );
			downloads.Add( dependencyIdent, new ServerPackages.PackageDownload { ident = dependencyIdent, activePackage = activePackage, IsDownloading = clearSession, IsMounted = !clearSession } );
			var map = new RemotePackage
			{
				Org = new Package.Organization { Ident = "test" },
				Ident = "workshop-map",
				Title = "Workshop map",
				TypeName = "map",
				Version = new PackageRevision { Meta = "{\"WorkshopId\":4321}" },
				PackageReferences = [dependencyIdent, dependencyIdent]
			};

			AsyncPump.RunSynchronously( async () =>
			{
				using var installed = await WorkshopFileSystem.OpenInstalledAsync( 4321, folder, default );
				Assert.IsFalse( map.IsMounted(), "A shared file lease alone must not mark the map ready." );
				var loading = map.MountAsync( withCode );
				if ( clearSession )
				{
					Assert.IsFalse( loading.IsCompleted );
					Assert.IsTrue( packages.StringTable.Entries.ContainsKey( dependencyIdent ) );
					items.Clear();
					await AssertWorkshopRequestCancelled( loading );
					Assert.IsFalse( map.IsMounted() );
					return;
				}

				var content = await loading;
				Assert.AreEqual( validMap, content is not null );
				Assert.AreEqual( validMap, map.IsMounted() );
			} );

			if ( validMap && !clearSession )
			{
				Assert.IsNull( Game.Scripting.LastError, Game.Scripting.LastError?.ToString() );
				Assert.IsTrue( scripts.Entries.ContainsKey( "map.csx" ), "The map script must compile against its dependency's declarations." );
				Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "DependencyStarts" ) );
				Assert.AreEqual( 42, Game.Scripting.Globals.Get<int>( "MapSawDependency" ) );
				Assert.IsTrue( packages.StringTable.Entries[dependencyIdent].Read<ServerPackages.PackageInfo>().ResourcesReady );
				Assert.AreEqual( 1, packages.StringTable.Entries.Count, "Only the dependency belongs in the package table." );
				Assert.AreEqual( Workshop.ItemType.Map, items.StringTable.Entries["4321"].Read<WorkshopItems.ItemInfo>().Type );
				map.RemoveMountRequirement();
				Assert.AreEqual( 0, items.StringTable.Entries.Count, "Releasing the map must remove its Workshop requirement." );
				Assert.AreEqual( 0, packages.StringTable.Entries.Count, "Duplicate package references must only install once." );
			}
			else
			{
				Assert.AreEqual( 0, items.StringTable.Entries.Count );
				Assert.AreEqual( 0, packages.StringTable.Entries.Count, "Failed map loading must release its dependency requirements." );
			}
		}
		finally
		{
			items.Clear();
			context.FileMount = files;
			mounted.Dispose();
			WorkshopItems.Current = previousItems;
			Networking.System = previousSystem;
			downloadsField.SetValue( null, previousDownloads );
			typeof( ServerPackages ).GetProperty( nameof( ServerPackages.Current ) ).SetValue( null, previousPackages );
			Directory.Delete( folder, true );
		}
	}
}
