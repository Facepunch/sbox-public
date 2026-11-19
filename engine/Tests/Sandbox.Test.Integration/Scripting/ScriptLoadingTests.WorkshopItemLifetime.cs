using Sandbox.Network;
using Sandbox.Tasks;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Cancelling one caller leaves the shared installation running for another caller.
	/// </summary>
	[TestMethod]
	public void WorkshopItemCancellationOnlyReleasesItsCaller()
	{
		var folder = CreateWorkshopContent( "map" );
		var previousSystem = Networking.System;
		var mounted = new AggregateFileSystem();
		var download = new TaskCompletionSource<WorkshopFileSystem>( TaskCreationOptions.RunContinuationsAsynchronously );
		CancellationToken installToken = default;
		int downloads = 0;
		var items = new WorkshopItems( ( id, token ) =>
		{
			installToken = token;
			downloads++;
			return download.Task;
		} );
		WorkshopFileSystem content = null;
		using var caller = new CancellationTokenSource();

		try
		{
			Networking.System = null;
			context.FileMount = mounted;
			AsyncPump.RunSynchronously( async () =>
			{
				content = await WorkshopFileSystem.OpenInstalledAsync( 7001, folder, default );
				var first = items.LoadAsync( 7001, Workshop.ItemType.Map, caller.Token );
				var second = items.LoadAsync( 7001, Workshop.ItemType.Map );
				caller.Cancel();
				await AssertWorkshopRequestCancelled( first );
				Assert.IsFalse( installToken.IsCancellationRequested );
				Assert.AreEqual( 1, downloads );

				download.SetResult( content );
				Assert.AreSame( content, await second );
				Assert.IsTrue( items.IsMounted( 7001 ) );
				Assert.AreEqual( "content", mounted.ReadAllText( "content.txt" ) );
				Assert.IsNull( await items.LoadAsync( 7001, Workshop.ItemType.Addon ) );
				Assert.IsFalse( items.HasFailures );
				items.RemoveRequirement( 7001 );
				Assert.AreEqual( 0, items.StringTable.Entries.Count );
			} );
		}
		finally
		{
			items.Clear();
			content?.Dispose();
			context.FileMount = files;
			mounted.Dispose();
			Networking.System = previousSystem;
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A late factory result is released after its last caller cancels or the session closes.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void WorkshopItemCancellationReleasesLateFiles( bool clearSession )
	{
		var folder = CreateWorkshopContent( "map" );
		var previousSystem = Networking.System;
		var mounted = new AggregateFileSystem();
		var download = new TaskCompletionSource<WorkshopFileSystem>( TaskCreationOptions.RunContinuationsAsynchronously );
		CancellationToken installToken = default;
		var items = new WorkshopItems( ( id, token ) =>
		{
			installToken = token;
			return download.Task;
		} );
		WorkshopFileSystem content = null;
		using var caller = new CancellationTokenSource();

		try
		{
			Networking.System = null;
			context.FileMount = mounted;
			AsyncPump.RunSynchronously( async () =>
			{
				content = await WorkshopFileSystem.OpenInstalledAsync( 7002, folder, default );
				var loading = items.LoadAsync( 7002, Workshop.ItemType.Map, caller.Token );
				if ( clearSession )
				{
					items.Clear();
				}
				else
				{
					caller.Cancel();
				}

				await AssertWorkshopRequestCancelled( loading );
				Assert.IsTrue( installToken.IsCancellationRequested );
				Assert.IsFalse( items.IsMounted( 7002 ) );
				Assert.AreEqual( 0, items.StringTable.Entries.Count );

				// The factory deliberately ignores cancellation, like a download completing at teardown.
				download.SetResult( content );
				using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 5 ) );
				while ( content.IsValid ) await Task.Delay( 10, timeout.Token );
				Assert.IsFalse( mounted.FileExists( "content.txt" ) );
				Assert.IsFalse( WorkshopFileSystem.IsInUse( 7002 ) );
			} );
		}
		finally
		{
			items.Clear();
			content?.Dispose();
			context.FileMount = files;
			mounted.Dispose();
			Networking.System = previousSystem;
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Waiting for an installation includes mounting its files into the root used by scripts.
	/// </summary>
	[TestMethod]
	public void WorkshopItemWaitsUntilFilesAreMounted()
	{
		var folder = CreateWorkshopContent( "map" );
		var mounted = new AggregateFileSystem();
		var download = new TaskCompletionSource<WorkshopFileSystem>( TaskCreationOptions.RunContinuationsAsynchronously );
		var items = new WorkshopItems( ( id, token ) => download.Task );
		WorkshopFileSystem content = null;

		try
		{
			context.FileMount = mounted;
			AsyncPump.RunSynchronously( async () =>
			{
				content = await WorkshopFileSystem.OpenInstalledAsync( 7003, folder, default );
				items.StringTable.Set( "7003", new WorkshopItems.ItemInfo { Id = 7003, Type = Workshop.ItemType.Map, ResourcesReady = true } );
				var installing = items.InstallAsync( items.StringTable.Entries["7003"] );
				var waiting = items.WaitForDownloadsAsync();
				Assert.IsFalse( waiting.IsCompleted );
				Assert.IsFalse( items.IsMounted( 7003 ) );
				Assert.IsTrue( items.HasPending );
				Assert.IsFalse( mounted.FileExists( "content.txt" ) );

				download.SetResult( content );
				await waiting;
				Assert.IsTrue( items.IsMounted( 7003 ) );
				Assert.IsFalse( items.HasPending );
				Assert.AreEqual( "content", mounted.ReadAllText( "content.txt" ) );
				Assert.IsTrue( await installing );
			} );
		}
		finally
		{
			items.Clear();
			content?.Dispose();
			context.FileMount = files;
			mounted.Dispose();
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// Cancelling a deferred resource load keeps already mounted files available until the session ends.
	/// </summary>
	[TestMethod]
	public void WorkshopItemCancelledResourcesKeepMountedFiles()
	{
		var folder = CreateWorkshopContent( "map" );
		var previousSystem = Networking.System;
		var mounted = new AggregateFileSystem();
		var items = new WorkshopItems( ( id, token ) => WorkshopFileSystem.OpenInstalledAsync( id, folder, token ) );
		var resourceLock = (SemaphoreSlim)typeof( WorkshopItems ).GetField( "resourceLoading", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( items );
		WorkshopFileSystem content = null;

		try
		{
			Networking.System = null;
			context.FileMount = mounted;
			AsyncPump.RunSynchronously( async () =>
			{
				items.StringTable.Set( "7004", new WorkshopItems.ItemInfo { Id = 7004, Type = Workshop.ItemType.Map } );
				Assert.IsTrue( await items.InstallAsync( items.StringTable.Entries["7004"] ) );
				Assert.IsTrue( items.IsMounted( 7004 ) );

				using var caller = new CancellationTokenSource();
				await resourceLock.WaitAsync();
				try
				{
					var loading = items.LoadAsync( 7004, Workshop.ItemType.Map, caller.Token );
					Assert.IsFalse( loading.IsCompleted );
					caller.Cancel();
					await AssertWorkshopRequestCancelled( loading );
					Assert.AreEqual( 0, items.StringTable.Entries.Count );
					Assert.IsTrue( items.IsMounted( 7004 ) );
					Assert.IsTrue( WorkshopFileSystem.IsInUse( 7004 ) );
					Assert.AreEqual( "content", mounted.ReadAllText( "content.txt" ) );
				}
				finally
				{
					resourceLock.Release();
				}

				content = await items.LoadAsync( 7004, Workshop.ItemType.Map );
				Assert.IsNotNull( content );
				Assert.IsFalse( items.HasFailures );
				Assert.IsFalse( items.HasPending );
				items.Clear();
				Assert.IsFalse( content.IsValid );
				Assert.IsFalse( WorkshopFileSystem.IsInUse( 7004 ) );
				Assert.IsFalse( mounted.FileExists( "content.txt" ) );
			} );
		}
		finally
		{
			items.Clear();
			content?.Dispose();
			context.FileMount = files;
			mounted.Dispose();
			Networking.System = previousSystem;
			Directory.Delete( folder, true );
		}
	}

	private static async Task AssertWorkshopRequestCancelled( Task request )
	{
		try
		{
			await request.WaitAsync( TimeSpan.FromSeconds( 5 ) );
			Assert.Fail( "The caller's request should be cancelled." );
		}
		catch ( OperationCanceledException )
		{
			Assert.IsTrue( request.IsCanceled );
		}
	}
}
