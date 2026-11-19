using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Cancelling a reused download or deferred resource load releases exactly the failed caller's requirement.
	/// </summary>
	[DataTestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void CancelledPackageInstallReleasesItsRequirement( bool inFlight, bool alreadyRequired )
	{
		var previousSystem = Networking.System;
		var previousPackages = ServerPackages.Current;
		var downloadsField = typeof( ServerPackages ).GetField( "Downloads", BindingFlags.Static | BindingFlags.NonPublic );
		var previousDownloads = downloadsField.GetValue( null );
		var resourceLock = (SemaphoreSlim)typeof( ServerPackages ).GetField( "_resourceLoadSem", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null );

		try
		{
			Networking.System = null;
			var packages = new ServerPackages();
			const string ident = "test.cancelled";
			if ( alreadyRequired )
			{
				packages.AddRequirement( ident );
			}

			var download = new ServerPackages.PackageDownload
			{
				ident = ident,
				activePackage = CreateNetworkTestPackage(),
				IsDownloading = inFlight
			};
			var downloads = (IDictionary<string, ServerPackages.PackageDownload>)downloadsField.GetValue( null );
			downloads.Add( ident, download );

			AsyncPump.RunSynchronously( async () =>
			{
				using var cancellation = new CancellationTokenSource();
				await resourceLock.WaitAsync();
				try
				{
					var pending = ServerPackages.DownloadAndMount( ident, cancellation.Token ).AsTask();
					Assert.IsFalse( pending.IsCompleted );
					cancellation.Cancel();
					try
					{
						await pending.WaitAsync( TimeSpan.FromSeconds( 5 ) );
						Assert.Fail( "The cancelled install must report cancellation." );
					}
					catch ( OperationCanceledException )
					{
						Assert.IsTrue( pending.IsCanceled );
					}
				}
				finally
				{
					download.IsDownloading = false;
					resourceLock.Release();
				}
			} );

			Assert.AreEqual( alreadyRequired, packages.StringTable.Entries.ContainsKey( ident ) );
			if ( alreadyRequired )
			{
				Assert.IsTrue( packages.StringTable.Entries[ident].Read<ServerPackages.PackageInfo>().ResourcesReady );
				packages.RemoveRequirement( ident );
				Assert.IsFalse( packages.StringTable.Entries.ContainsKey( ident ), "Only the original reference should remain." );
			}
		}
		finally
		{
			Networking.System = previousSystem;
			downloadsField.SetValue( null, previousDownloads );
			typeof( ServerPackages ).GetProperty( nameof( ServerPackages.Current ) ).SetValue( null, previousPackages );
		}
	}

	/// <summary>
	/// Script edits wait for active required downloads without starting other installs or touching the loading screen.
	/// </summary>
	[TestMethod]
	public void ScriptUpdatesOnlyWaitForActivePackageDownloads()
	{
		var previousPackages = ServerPackages.Current;
		var downloadsField = typeof( ServerPackages ).GetField( "Downloads", BindingFlags.Static | BindingFlags.NonPublic );
		var previousDownloads = downloadsField.GetValue( null );
		var previousTitle = LoadingScreen.Title;

		try
		{
			var packages = new ServerPackages();
			packages.AddRequirement( "test.required" );
			var downloads = (IDictionary<string, ServerPackages.PackageDownload>)downloadsField.GetValue( null );
			var download = new ServerPackages.PackageDownload { IsDownloading = true };
			downloads.Add( "test.required", download );
			downloads.Add( "test.unrelated", new() { IsDownloading = true } );
			LoadingScreen.Title = "Unchanged by script edits";

			AsyncPump.RunSynchronously( async () =>
			{
				var pending = packages.WaitForDownloads();
				Assert.IsFalse( pending.IsCompleted );
				download.IsDownloading = false;
				await pending;
				Assert.IsTrue( packages.WaitForDownloads().IsCompletedSuccessfully );
			} );

			Assert.AreEqual( "Unchanged by script edits", LoadingScreen.Title );
			Assert.IsNull( download.activePackage );
			Assert.IsTrue( downloads["test.unrelated"].IsDownloading );
		}
		finally
		{
			LoadingScreen.Title = previousTitle;
			downloadsField.SetValue( null, previousDownloads );
			typeof( ServerPackages ).GetProperty( nameof( ServerPackages.Current ) ).SetValue( null, previousPackages );
		}
	}
}
