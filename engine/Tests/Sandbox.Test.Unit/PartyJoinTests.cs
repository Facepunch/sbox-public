using Sandbox.Menu;
using System;
using System.Threading;

[TestClass]
public class PartyJoinTests
{
	[TestMethod]
	public async Task HostMenuKeepsPreloadAndWaitsUntilTheServerIsReady()
	{
		var download = new TaskCompletionSource();
		var downloads = 0;
		var connects = 0;
		using var join = new PartyJoinController( ( _, _, _ ) => { downloads++; return download.Task; },
			( _, _, _ ) => { connects++; return Task.CompletedTask; }, () => { } );
		join.Update( Loading(), 0 );
		var pending = join.PendingTask;
		var menu = Loading() with { State = PartyRoom.OwnerJoinState.Unavailable };
		join.Update( menu, 1 );
		Assert.AreEqual( 1, downloads );
		Assert.AreEqual( PartyRoom.JoinStage.Downloading, join.Stage );

		download.SetResult();
		await pending;
		join.Update( menu, 600 );
		Assert.AreEqual( PartyRoom.JoinStage.WaitingForHost, join.Stage );
		Assert.IsNull( join.Error );
		Assert.AreEqual( 0, connects );

		join.Update( Ready(), 601 );
		await join.PendingTask;
		Assert.AreEqual( PartyRoom.JoinStage.Connected, join.Stage );
		Assert.AreEqual( 1, connects );
		Assert.AreEqual( 1, downloads );
	}

	[TestMethod]
	public async Task JoiningAPartyWhileHostIsInMenuPreloadsAndCanBeCancelled()
	{
		var downloads = 0;
		var connects = 0;
		using var join = new PartyJoinController( ( _, _, _ ) => { downloads++; return Task.CompletedTask; },
			( _, _, _ ) => { connects++; return Task.CompletedTask; }, () => { } );
		join.Update( Loading() with { State = PartyRoom.OwnerJoinState.Unavailable }, 0 );
		await join.PendingTask;
		Assert.AreEqual( 1, downloads );
		Assert.AreEqual( PartyRoom.JoinStage.WaitingForHost, join.Stage );
		join.Cancel();
		join.Update( Loading(), 300 );
		join.Update( Ready(), 600 );
		Assert.AreEqual( PartyRoom.JoinStage.Cancelled, join.Stage );
		Assert.AreEqual( 0, connects );
	}

	[TestMethod]
	public void StalledLocalDownloadStillTimesOutWhileHostIsInMenu()
	{
		var download = new TaskCompletionSource();
		using var join = new PartyJoinController( ( _, _, _ ) => download.Task,
			( _, _, _ ) => Task.CompletedTask, () => { } );
		var menu = Loading() with { State = PartyRoom.OwnerJoinState.Unavailable };
		join.Update( menu, 0 );
		join.Update( menu, 121 );
		Assert.AreEqual( PartyRoom.JoinStage.Failed, join.Stage );
	}

	[TestMethod]
	public async Task RetryDuringConnectionDoesNotRestartTheAttempt()
	{
		var connection = new TaskCompletionSource();
		var connects = 0;
		var stops = 0;
		using var join = new PartyJoinController( ( _, _, _ ) => Task.CompletedTask,
			( _, _, _ ) => { connects++; return connection.Task; }, () => stops++ );
		join.Update( Ready(), 0 );
		var pending = join.PendingTask;
		join.Retry();
		join.Retry();
		join.Update( Ready(), 1 );
		Assert.AreEqual( PartyRoom.JoinStage.Connecting, join.Stage );
		Assert.AreEqual( 1, connects );
		Assert.AreEqual( 1, stops );
		connection.SetResult();
		await pending;
		Assert.AreEqual( PartyRoom.JoinStage.Connected, join.Stage );
	}

	[TestMethod]
	public async Task CancelAndRetryDoNotDisturbAJoinedGame()
	{
		var connects = 0;
		var stops = 0;
		using var join = new PartyJoinController( ( _, _, _ ) => Task.CompletedTask,
			( _, _, _ ) => { connects++; return Task.CompletedTask; }, () => stops++ );
		join.Cancel();
		join.Retry();
		Assert.AreEqual( PartyRoom.JoinStage.None, join.Stage );
		join.Update( Ready(), 0 );
		await join.PendingTask;
		join.Cancel();
		join.Retry();
		join.Update( Ready(), 1 );
		Assert.AreEqual( PartyRoom.JoinStage.Connected, join.Stage );
		Assert.AreEqual( 1, connects );
		Assert.AreEqual( 1, stops );
	}

	[TestMethod]
	public async Task CancelledAttemptCanRetryTheSameTargetOnce()
	{
		var connects = 0;
		using var join = new PartyJoinController( ( _, _, _ ) => Task.CompletedTask,
			( _, _, _ ) => { connects++; return Task.CompletedTask; }, () => { } );
		join.Update( Loading(), 0 );
		join.Cancel();
		join.Cancel();
		join.Update( Ready(), 1 );
		Assert.AreEqual( PartyRoom.JoinStage.Cancelled, join.Stage );
		Assert.AreEqual( 0, connects );
		join.Retry();
		join.Retry();
		join.Update( Ready(), 2 );
		await join.PendingTask;
		Assert.AreEqual( PartyRoom.JoinStage.Connected, join.Stage );
		Assert.AreEqual( 1, connects );
	}

	[TestMethod]
	public async Task ConnectionFailureKeepsTheDetailedReason()
	{
		const string reason = "Steam was still finding a route. Reason: 5003.";
		using var join = new PartyJoinController( ( _, _, _ ) => Task.CompletedTask,
			( _, _, _ ) => Task.FromException( new InvalidOperationException( reason ) ), () => { } );
		join.Update( Ready(), 0 );
		await join.PendingTask;
		Assert.AreEqual( PartyRoom.JoinStage.Failed, join.Stage );
		StringAssert.Contains( join.Error, reason );
	}

	[TestMethod]
	public void LoadedGameWithoutServerIsUnavailable()
	{
		Assert.AreEqual( PartyRoom.OwnerJoinState.Unavailable, PartyRoom.DetermineJoinState( false, false, null, true ) );
		Assert.AreEqual( PartyRoom.OwnerJoinState.None, PartyRoom.DetermineJoinState( false, false, null, false ) );
		Assert.AreEqual( PartyRoom.OwnerJoinState.Loading, PartyRoom.DetermineJoinState( true, false, "server", true ) );
		Assert.AreEqual( PartyRoom.OwnerJoinState.Ready, PartyRoom.DetermineJoinState( false, false, "server", true ) );
	}

	[TestMethod]
	public async Task DownloadFailureNeverConnects()
	{
		var connects = 0;
		using var join = new PartyJoinController( ( _, _, _ ) => Task.FromException( new Exception( "Download failed" ) ),
			( _, _, _ ) => { connects++; return Task.CompletedTask; }, () => { } );
		join.Update( Ready(), 0 );
		await join.PendingTask;
		Assert.AreEqual( PartyRoom.JoinStage.Failed, join.Stage );
		Assert.AreEqual( 0, connects );
	}

	[TestMethod]
	public async Task RepeatedReadyUpdatesConnectOnlyOnce()
	{
		var connects = 0;
		using var join = new PartyJoinController( ( _, _, _ ) => Task.CompletedTask,
			( _, _, _ ) => { connects++; return Task.CompletedTask; }, () => { } );
		for ( var i = 0; i < 10; i++ ) join.Update( Ready(), i );
		await join.PendingTask;
		Assert.AreEqual( 1, connects );
	}

	[TestMethod]
	public async Task HostReturningToMenuCancelsPendingJoin()
	{
		var download = new TaskCompletionSource();
		var connects = 0;
		using var join = new PartyJoinController( ( _, _, _ ) => download.Task,
			( _, _, _ ) => { connects++; return Task.CompletedTask; }, () => { } );
		join.Update( Ready(), 0 );
		var pending = join.PendingTask;
		join.Update( new( 1, PartyRoom.OwnerJoinState.None, "", "" ), 1 );
		download.SetResult();
		await pending;
		Assert.AreEqual( 0, connects );
		Assert.AreEqual( PartyRoom.JoinStage.None, join.Stage );
	}

	[TestMethod]
	public async Task DisposeDuringHandshakeStopsConnection()
	{
		var connection = new TaskCompletionSource();
		var stopped = 0;
		var join = new PartyJoinController( ( _, _, _ ) => Task.CompletedTask, ( _, _, _ ) => connection.Task, () => stopped++ );
		join.Update( Ready(), 0 );
		var pending = join.PendingTask;
		join.Dispose();
		connection.SetResult();
		await pending;
		Assert.AreEqual( 2, stopped ); // Previous game teardown, then cancellation of the new connection.
		Assert.AreEqual( PartyRoom.JoinStage.Cancelled, join.Stage );
	}

	[TestMethod]
	public async Task ReadyWithoutAddressTimesOut()
	{
		using var join = new PartyJoinController( ( _, _, _ ) => Task.CompletedTask, ( _, _, _ ) => Task.CompletedTask, () => { } );
		join.Update( Ready( "" ), 0 );
		await join.PendingTask;
		join.Update( Ready( "" ), 121 );
		Assert.AreEqual( PartyRoom.JoinStage.Failed, join.Stage );
	}

	[TestMethod]
	public async Task LateConnectionCannotOverwriteNewAttemptFailure()
	{
		var oldConnection = new TaskCompletionSource();
		using var join = new PartyJoinController( ( _, _, _ ) => Task.CompletedTask,
			( address, _, _ ) => address == "old" ? oldConnection.Task : Task.FromException( new Exception( "new failure" ) ), () => { } );
		join.Update( Ready( "old" ), 0 );
		var oldTask = join.PendingTask;
		join.Update( Ready( "new" ), 1 );
		await join.PendingTask;
		oldConnection.SetResult();
		await oldTask;
		Assert.AreEqual( PartyRoom.JoinStage.Failed, join.Stage );
		StringAssert.Contains( join.Error, "new failure" );
	}

	[TestMethod]
	public async Task HostReadyDoesNotHideUnfinishedLocalDownload()
	{
		var download = new TaskCompletionSource();
		using var join = new PartyJoinController( ( _, _, _ ) => download.Task, ( _, _, _ ) => Task.CompletedTask, () => { } );
		join.Update( Loading(), 0 );
		join.Update( Ready(), 1 );
		Assert.AreEqual( PartyRoom.JoinStage.Downloading, join.Stage );
		download.SetResult();
		await join.PendingTask;
		Assert.AreEqual( PartyRoom.JoinStage.Connected, join.Stage );
	}

	static PartyJoinController.Target Loading( string package = "test.game#1", ulong owner = 1 ) => new( owner, PartyRoom.OwnerJoinState.Loading, package, "" );
	static PartyJoinController.Target Ready( string address = "server", string package = "test.game#1", ulong owner = 1 ) => new( owner, PartyRoom.OwnerJoinState.Ready, package, address );

	[TestMethod]
	public async Task CachedDownloadClosesPreviousGameBeforeConnecting()
	{
		var closed = false;
		var connected = false;
		using var join = new PartyJoinController( ( _, _, _ ) => Task.CompletedTask, ( _, _, _ ) =>
		{
			Assert.IsTrue( closed, "The previous game must be closed before starting a cached join." );
			connected = true;
			return Task.CompletedTask;
		}, () => closed = true );
		join.Update( Loading(), 0 );
		Assert.IsFalse( connected );
		join.Update( Ready(), 1 );
		await join.PendingTask;
		Assert.IsTrue( connected );
		Assert.AreEqual( PartyRoom.JoinStage.Connected, join.Stage );
	}

	[TestMethod]
	public async Task CancelledPreloadCannotConnectEvenIfDownloadIgnoresCancellation()
	{
		var download = new TaskCompletionSource();
		var connects = 0;
		using var join = new PartyJoinController( ( _, _, _ ) => download.Task, ( _, _, _ ) => { connects++; return Task.CompletedTask; }, () => { } );
		join.Update( Loading(), 0 );
		join.Update( Ready(), 1 );
		var pending = join.PendingTask;
		join.Cancel();
		download.SetResult();
		await pending;
		Assert.AreEqual( 0, connects );
		Assert.AreEqual( PartyRoom.JoinStage.Cancelled, join.Stage );
		join.Update( Ready(), 2 );
		Assert.AreEqual( 0, connects );
	}

	[TestMethod]
	public async Task HostChangeInvalidatesOldDownloadAndProgress()
	{
		var oldDownload = new TaskCompletionSource();
		Action<LoadingProgress?> oldProgress = null;
		string connected = null;
		using var join = new PartyJoinController( ( package, _, progress ) =>
		{
			if ( package == "old.game" ) { oldProgress = progress; return oldDownload.Task; }
			return Task.CompletedTask;
		}, ( address, _, _ ) => { connected = address; return Task.CompletedTask; }, () => { } );
		join.Update( Loading( "old.game" ), 0 );
		join.Update( Ready( "old-server", "old.game" ), 1 );
		var oldTask = join.PendingTask;
		join.Update( Ready( "new-server", owner: 2 ), 2 );
		await join.PendingTask;
		oldProgress( new() { Fraction = 0.5 } );
		oldDownload.SetResult();
		await oldTask;
		Assert.AreEqual( "new-server", connected );
		Assert.AreEqual( PartyRoom.JoinStage.Connected, join.Stage );
		Assert.IsNull( join.Progress );
	}

	[TestMethod]
	public async Task FailureCanRetryTheSameAddress()
	{
		var connects = 0;
		using var join = new PartyJoinController( ( _, _, _ ) => Task.CompletedTask, ( _, _, _ ) =>
		{
			if ( ++connects == 1 ) throw new InvalidOperationException( "Connection failed" );
			return Task.CompletedTask;
		}, () => { } );
		join.Update( Ready(), 0 );
		await join.PendingTask;
		Assert.AreEqual( PartyRoom.JoinStage.Failed, join.Stage );
		join.Retry();
		join.Update( Ready(), 1 );
		await join.PendingTask;
		Assert.AreEqual( 2, connects );
		Assert.AreEqual( PartyRoom.JoinStage.Connected, join.Stage );
	}

	[TestMethod]
	public async Task StalledHostTimesOutButProgressKeepsSlowDownloadsAlive()
	{
		using var join = new PartyJoinController( ( _, _, _ ) => Task.CompletedTask, ( _, _, _ ) => Task.CompletedTask, () => { } );
		join.Update( Loading(), 0 );
		await join.PendingTask;
		join.Update( Loading(), 100, new() { Fraction = 0.2 } );
		join.Update( Loading(), 200, new() { Fraction = 0.4 } );
		Assert.AreEqual( PartyRoom.JoinStage.WaitingForHost, join.Stage );
		join.Update( Loading(), 321, new() { Fraction = 0.4 } );
		Assert.AreEqual( PartyRoom.JoinStage.Failed, join.Stage );
	}

	[TestMethod]
	public async Task PackageChangeWhileLoadingStartsNewPreload()
	{
		string downloaded = null;
		using var join = new PartyJoinController( ( package, _, _ ) => { downloaded = package; return Task.CompletedTask; }, ( _, _, _ ) => Task.CompletedTask, () => { } );
		join.Update( Loading( "first.game" ), 0 );
		join.Update( Loading( "second.game" ), 1 );
		await join.PendingTask;
		Assert.AreEqual( "second.game", downloaded );
	}
}
