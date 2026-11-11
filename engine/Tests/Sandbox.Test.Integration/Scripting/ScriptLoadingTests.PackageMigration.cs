using Sandbox.Network;
using Sandbox.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Promotion finishes pending packages before releasing resources, including downloads spanning the handoff.
	/// Failed installs disappear from the requirements instead of leaving future joiners waiting indefinitely.
	/// </summary>
	[DataTestMethod]
	[DataRow( false, true )]
	[DataRow( true, true )]
	[DataRow( true, false )]
	public void ScriptTablePackageMigrationCompletesPendingRequirements( bool inFlight, bool succeeds )
	{
		var previousSystem = Networking.System;
		var previousPackages = ServerPackages.Current;
		var downloadsField = typeof( ServerPackages ).GetField( "Downloads", BindingFlags.Static | BindingFlags.NonPublic );
		var previousDownloads = downloadsField.GetValue( null );
		var resourceLock = (SemaphoreSlim)typeof( ServerPackages ).GetField( "_resourceLoadSem", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null );

		try
		{
			Networking.System = new NetworkSystem( "package-migration-client", Game.TypeLibrary );
			var packages = new ServerPackages();
			const string ident = "test.pending";
			const string readyIdent = "test.ready";
			packages.StringTable.Set( ident, new ServerPackages.PackageInfo() );
			packages.StringTable.Set( readyIdent, new ServerPackages.PackageInfo { ResourcesReady = true, WorkshopVersion = 123 } );
			files.WriteAllText( "migration.csx", "[Global] int MigratedStarts = 0; MigratedStarts++; class MigratedScript {}" );
			files.WriteAllText( "migration.server.csx", "class OriginalHostOnlyScript {}" );
			var download = new ServerPackages.PackageDownload
			{
				ident = ident,
				activePackage = succeeds ? CreateNetworkTestPackage() : null,
				IsDownloading = inFlight,
				IsMounted = !inFlight && succeeds
			};
			var downloads = (IDictionary<string, ServerPackages.PackageDownload>)downloadsField.GetValue( null );
			downloads.Add( ident, download );
			var scripts = new StringTable( "Scripts", true );
			Game.Scripting.InitializeNetworking( scripts, isClient: true );

			AsyncPump.RunSynchronously( async () =>
			{
				var clientInstall = ServerPackages.DownloadAndMount( ident, reloadResources: false ).AsTask();
				await resourceLock.WaitAsync();
				Task promotion = Task.CompletedTask;
				try
				{
					// Match promotion's role change before adopting the replicated tables.
					Networking.System = null;
					Game.Scripting.InitializeNetworking( scripts, isClient: false );
					promotion = packages.OnBecameHost();
					Assert.IsFalse( promotion.IsCompleted, "Promotion must finish the pending install and resource scan." );
					download.IsMounted = succeeds;
					download.IsDownloading = false;
					await clientInstall;

					if ( succeeds )
					{
						Assert.IsFalse( packages.StringTable.Entries[ident].Read<ServerPackages.PackageInfo>().ResourcesReady,
							"A download started as a client cannot release resources before the promoted host scans scripts." );
						Assert.IsNull( Game.TypeLibrary.GetType( "MigratedScript" ) );
					}
				}
				finally
				{
					download.IsDownloading = false;
					resourceLock.Release();
					await clientInstall;
					await promotion;
				}
			} );

			if ( succeeds )
			{
				Assert.IsTrue( packages.StringTable.Entries[ident].Read<ServerPackages.PackageInfo>().ResourcesReady );
				Assert.IsNotNull( Game.TypeLibrary.GetType( "MigratedScript" ) );
				Assert.IsTrue( scripts.Entries.ContainsKey( "migration.csx" ) );
				Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "MigratedStarts" ) );
				Assert.IsNull( Game.TypeLibrary.GetType( "OriginalHostOnlyScript" ), "Promotion must not start the original host's server-only scripts." );
				packages.RemoveRequirement( ident );
			}

			Assert.IsFalse( packages.StringTable.Entries.ContainsKey( ident ), "A failed or released requirement cannot block later joins." );
			Assert.AreEqual( 123L, packages.StringTable.Entries[readyIdent].Read<ServerPackages.PackageInfo>().WorkshopVersion );
			packages.RemoveRequirement( readyIdent );
			Assert.IsFalse( packages.StringTable.Entries.ContainsKey( readyIdent ), "Completed requirements must also be adopted." );
		}
		finally
		{
			Networking.System = previousSystem;
			downloadsField.SetValue( null, previousDownloads );
			typeof( ServerPackages ).GetProperty( nameof( ServerPackages.Current ) ).SetValue( null, previousPackages );
		}
	}
}
