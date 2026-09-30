using Sandbox.Menu;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Owns one follower's download and connection attempt. All callbacks run on the main thread.
/// Kept separate from Steam and the UI so races can be exercised without a live lobby.
/// </summary>
internal sealed class PartyJoinController : IDisposable
{
	internal readonly record struct Target( ulong Owner, PartyRoom.OwnerJoinState State, string Package, string Address );

	readonly Func<string, CancellationToken, Action<LoadingProgress?>, Task> _download;
	readonly Func<string, CancellationToken, Action<LoadingProgress?>, Task> _connect;
	readonly Action _stopConnecting;
	CancellationTokenSource _attempt;
	Target _target;
	bool _hasTarget;
	Task _preload;
	bool _connecting;
	double _now;
	double _lastActivity;
	LoadingProgress? _hostProgress;

	internal PartyRoom.JoinStage Stage { get; private set; }
	internal LoadingProgress? Progress { get; private set; }
	internal string Error { get; private set; }
	internal Task PendingTask { get; private set; } = Task.CompletedTask;

	internal PartyJoinController( Func<string, CancellationToken, Action<LoadingProgress?>, Task> download,
		Func<string, CancellationToken, Action<LoadingProgress?>, Task> connect, Action stopConnecting )
	{
		_download = download;
		_connect = connect;
		_stopConnecting = stopConnecting;
	}

	internal void Update( Target target, double now, LoadingProgress? hostProgress = null )
	{
		_now = now;
		var wasPreparing = _target.State is PartyRoom.OwnerJoinState.Loading or PartyRoom.OwnerJoinState.Unavailable;
		var isFollowing = target.State is PartyRoom.OwnerJoinState.Loading or PartyRoom.OwnerJoinState.Unavailable or PartyRoom.OwnerJoinState.Ready;
		var changed = !_hasTarget || target.Owner != _target.Owner || target.Package != _target.Package
			|| (target.State != _target.State && !(wasPreparing && isFollowing))
			|| (target.State == PartyRoom.OwnerJoinState.Ready && _target.State == PartyRoom.OwnerJoinState.Ready && target.Address != _target.Address);

		if ( changed )
		{
			Reset();
			_hasTarget = true;
			_attempt = new();
			_lastActivity = now;
		}
		else if ( target.State != _target.State )
		{
			_lastActivity = now;
		}

		_target = target;
		if ( target.State is PartyRoom.OwnerJoinState.None ) return;
		if ( Stage is PartyRoom.JoinStage.Failed or PartyRoom.JoinStage.Cancelled or PartyRoom.JoinStage.Connected ) return;

		if ( hostProgress is { } host && (_hostProgress is not { } previous || host.Fraction != previous.Fraction || host.Title != previous.Title) )
			_lastActivity = now;
		_hostProgress = hostProgress;

		// A loaded game may have its own menu. Give the leader time to set up a server.
		var waitingForServer = target.State == PartyRoom.OwnerJoinState.Unavailable && Stage == PartyRoom.JoinStage.WaitingForHost;
		if ( !waitingForServer && now - _lastActivity > 120 )
		{
			Fail( "No progress for two minutes. Check the party leader or retry." );
			return;
		}

		if ( _preload is null )
		{
			Stage = PartyRoom.JoinStage.Downloading;
			_preload = Preload( target.Package, _attempt.Token );
			PendingTask = _preload;
		}

		if ( target.State == PartyRoom.OwnerJoinState.Ready && !string.IsNullOrWhiteSpace( target.Address ) && !_connecting && Stage != PartyRoom.JoinStage.Failed )
		{
			_connecting = true;
			PendingTask = Connect( target.Address, _preload, _attempt.Token );
		}
	}

	void ReportProgress( LoadingProgress? progress, CancellationToken token )
	{
		if ( token.IsCancellationRequested ) return;
		if ( progress is { } current && (Progress is not { } previous || previous.Fraction != current.Fraction || previous.Title != current.Title) )
			_lastActivity = _now;
		Progress = progress;
	}

	async Task Preload( string package, CancellationToken token )
	{
		try
		{
			if ( !string.IsNullOrWhiteSpace( package ) )
				await _download( package, token, progress => ReportProgress( progress, token ) ).WaitAsync( token );
			token.ThrowIfCancellationRequested();
			Progress = null;
			Stage = PartyRoom.JoinStage.WaitingForHost;
			_lastActivity = _now;
		}
		catch ( OperationCanceledException ) when ( token.IsCancellationRequested ) { }
		catch ( Exception e )
		{
			if ( !token.IsCancellationRequested ) Fail( $"Download failed: {e.Message}" );
		}
	}

	async Task Connect( string address, Task preload, CancellationToken token )
	{
		try
		{
			await preload.WaitAsync( token );
			token.ThrowIfCancellationRequested();
			Stage = PartyRoom.JoinStage.Connecting;
			_lastActivity = _now;
			_stopConnecting();
			token.ThrowIfCancellationRequested();
			await _connect( address, token, progress => ReportProgress( progress, token ) ).WaitAsync( token );
			token.ThrowIfCancellationRequested();
			Stage = PartyRoom.JoinStage.Connected;
			Progress = null;
		}
		catch ( OperationCanceledException ) when ( token.IsCancellationRequested ) { }
		catch ( Exception e )
		{
			if ( !token.IsCancellationRequested ) Fail( $"Joining failed: {e.Message}" );
		}
	}

	internal void Fail( string error )
	{
		Reset();
		Error = error;
		Stage = PartyRoom.JoinStage.Failed;
	}

	internal void Cancel()
	{
		if ( Stage is not (PartyRoom.JoinStage.Downloading or PartyRoom.JoinStage.WaitingForHost or PartyRoom.JoinStage.Connecting) ) return;
		Reset();
		Stage = PartyRoom.JoinStage.Cancelled;
	}

	internal void Retry()
	{
		if ( Stage is not (PartyRoom.JoinStage.Failed or PartyRoom.JoinStage.Cancelled) ) return;
		Reset();
		_hasTarget = false;
	}

	void Reset()
	{
		_attempt?.Cancel();
		_attempt?.Dispose();
		_attempt = null;
		if ( Stage == PartyRoom.JoinStage.Connecting ) _stopConnecting();
		_preload = null;
		_connecting = false;
		Progress = null;
		_hostProgress = null;
		Error = null;
		Stage = PartyRoom.JoinStage.None;
	}

	public void Dispose()
	{
		Reset();
		Stage = PartyRoom.JoinStage.Cancelled;
	}
}
