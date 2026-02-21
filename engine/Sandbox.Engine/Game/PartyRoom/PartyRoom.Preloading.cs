using Sandbox.Network;
using System.Threading;

namespace Sandbox;

public partial class PartyRoom
{
	/// <summary>
	/// Used by party members to know when to preload the game package, and when to connect to the party leader's server.
	/// </summary>
	public enum OwnerJoinState
	{
		None,
		Loading,
		Ready
	}

	/// <summary>
	/// The current join state of the owner of the party
	/// </summary>
	public OwnerJoinState JoinState
	{
		get
		{
			if ( Enum.TryParse<OwnerJoinState>( steamLobby.GetData( "joinstate" ), out var state ) )
				return state;

			return OwnerJoinState.None;
		}
	}

	OwnerJoinState _lastJoinState;
	string _lastConnectString;
	Task _preloadTask;
	CancellationTokenSource _preloadCts;

	/// <summary>
	/// Wait for the background preload to finish, then connect to the server.
	/// </summary>
	async Task ConnectAfterPreload( string address )
	{
		if ( _preloadTask is not null )
		{
			Log.Trace( $"Party: waiting for preload to finish before connecting.." );
			await _preloadTask;
		}

		_preloadCts = null;
		_preloadTask = null;

		Log.Trace( $"Party: connecting to '{address}'" );
		NetworkConsoleCommands.Disconnect();
		NetworkConsoleCommands.ConnectToServer( address );
	}

	/// <summary>
	/// Start downloading the game package in the background so it's ready
	/// by the time the host signals we can connect.
	/// </summary>
	void PreloadPackageInBackground( string packageIdent )
	{
		// Cancel any previous preload
		_preloadCts?.Cancel();
		_preloadCts = null;
		_preloadTask = null;

		if ( string.IsNullOrWhiteSpace( packageIdent ) )
			return;

		Log.Trace( $"Party: pre-loading package '{packageIdent}'" );

		_preloadCts = new CancellationTokenSource();
		var token = _preloadCts.Token;

		_preloadTask = PreloadAsync( packageIdent, token );
	}

	async Task PreloadAsync( string packageIdent, CancellationToken token )
	{
		try
		{
			var package = await Package.Fetch( packageIdent, false );
			if ( package is null || token.IsCancellationRequested )
				return;

			await package.Download( token );

			Log.Trace( $"Party: pre-load of '{packageIdent}' complete" );
		}
		catch ( OperationCanceledException ) { }
		catch ( Exception e )
		{
			Log.Warning( $"Party: failed to pre-load '{packageIdent}': {e.Message}" );
		}
	}
}
