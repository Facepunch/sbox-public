using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Menu;
using Sandbox.Network;
using System.Globalization;
using System.Threading;

namespace Sandbox;

public partial class PartyRoom
{
	/// <summary>The party leader's advertised availability for joining their game.</summary>
	public enum OwnerJoinState
	{
		None = 0,
		Loading = 1,
		Ready = 2,
		Unavailable = 3
	}

	/// <summary>The local player's progress following the party leader.</summary>
	public enum JoinStage
	{
		/// <summary>No attempt to follow the leader is active.</summary>
		None = 0,
		/// <summary>Downloading the leader's game package.</summary>
		Downloading = 1,
		/// <summary>The local download is ready; waiting for the leader's server.</summary>
		WaitingForHost = 2,
		/// <summary>Connecting to the server and loading its game.</summary>
		Connecting = 3,
		/// <summary>The local player has finished joining the leader's game.</summary>
		Connected = 4,
		/// <summary>The attempt failed. See <see cref="JoinError"/>.</summary>
		Failed = 5,
		/// <summary>The local player cancelled this attempt.</summary>
		Cancelled = 6,
		/// <summary>The leader's current game has no joinable server.</summary>
		Unavailable = 7
	}

	/// <summary>The leader's advertised join state. Unknown or missing states report <see cref="OwnerJoinState.None"/>.</summary>
	public OwnerJoinState JoinState => Enum.TryParse<OwnerJoinState>( steamLobby.GetData( "joinstate" ), out var state ) && Enum.IsDefined( state ) ? state : OwnerJoinState.None;

	/// <summary>The local follower's join stage, or <see cref="JoinStage.None"/> when no attempt exists.</summary>
	public JoinStage JoiningStage => _join?.Stage ?? JoinStage.None;

	/// <summary>A snapshot of this player's current download progress, or null when unavailable.</summary>
	public LoadingProgress? DownloadProgress => _join?.Progress;

	/// <summary>A human-readable failure message, or null when there is no failure. Do not use it to determine the join stage.</summary>
	public string JoinError => _join?.Error;

	/// <summary>The game title advertised by the party leader, which may be empty.</summary>
	public string PackageTitle => steamLobby.GetData( "packagetitle" );

	/// <summary>
	/// A snapshot of the leader's download progress, separate from this player's download.
	/// Null when the leader is not loading or has not reported valid progress.
	/// </summary>
	public LoadingProgress? HostDownloadProgress
	{
		get
		{
			if ( JoinState != OwnerJoinState.Loading ) return null;
			if ( !double.TryParse( steamLobby.GetData( "download_fraction" ), NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction ) || !double.IsFinite( fraction ) )
				return null;
			return new LoadingProgress { Title = steamLobby.GetData( "download_title" ), Fraction = Math.Clamp( fraction, 0, 1 ) };
		}
	}

	PartyJoinController _join;

	/// <summary>
	/// Retry a failed or cancelled join on the next party update. Does nothing in other stages.
	/// Must be called on the main thread.
	/// </summary>
	public void RetryJoin() => _join?.Retry();

	/// <summary>
	/// Cancel a download, wait for the leader, or connection attempt while staying in the party.
	/// Does nothing in other stages, including after joining. Must be called on the main thread.
	/// </summary>
	public void CancelJoin() => _join?.Cancel();

	void UpdateFollowing()
	{
		_join ??= new PartyJoinController( PreloadAsync, ConnectAsync, StopConnecting );
		var target = new PartyJoinController.Target( Owner.Id, JoinState, PackageIdent, GameAddress );
		if ( JoinState == OwnerJoinState.None && JoiningStage == JoinStage.Connected )
			StopConnecting();
		_join.Update( target, RealTime.Now, HostDownloadProgress );
		if ( JoiningStage == JoinStage.Connected && !Networking.IsActive )
			_join.Fail( "Disconnected from the game. Retry to rejoin the party leader." );
	}

	static void StopConnecting()
	{
		// CloseGame performs teardown synchronously. Game.Close only schedules it for the next tick.
		IGameInstanceDll.Current?.CloseGame();
		Networking.Disconnect();
		LoadingScreen.IsVisible = false;
	}

	static async Task ConnectAsync( string address, CancellationToken token, Action<LoadingProgress?> progress )
	{
		token.ThrowIfCancellationRequested();
		string failure = null;
		if ( !await Networking.TryConnect( address, token: token, onFailure: message => failure = message ) )
			throw new InvalidOperationException( failure ?? "Unable to connect to the party leader's game." );

		var system = Networking.System;
		while ( system is not null && ReferenceEquals( system, Networking.System ) )
		{
			token.ThrowIfCancellationRequested();
			if ( Connection.Local.State == Connection.ChannelState.Connected ) return;
			progress( LoadingScreen.Progress );
			await Task.Delay( 100, token );
		}
		throw new InvalidOperationException( system?.FailureReason ?? "Disconnected before joining finished." );
	}

	static async Task PreloadAsync( string packageIdent, CancellationToken token, Action<LoadingProgress?> progress )
	{
		var package = await Package.Fetch( packageIdent, false ).WaitAsync( token );
		token.ThrowIfCancellationRequested();
		if ( package is null ) throw new InvalidOperationException( $"Package '{packageIdent}' was not found." );
		using var loading = new PreloadProgress( progress );
		var files = await package.Download( token, new PackageLoadOptions { Loading = loading } );
		if ( files is null ) throw new InvalidOperationException( $"Could not download '{package.Title}'." );
	}

	sealed class PreloadProgress( Action<LoadingProgress?> report ) : ILoadingInterface
	{
		public void LoadingProgress( LoadingProgress progress ) => report( progress );
		public void Dispose() { }
	}
}
