using Sandbox.Diagnostics;
using System;
using static Sandbox.Diagnostics.PerformanceStats;

internal static class Analytics
{
	static RealTimeUntil timeUntilNextUpdate;

	static string gameIdent;
	static string gameVersion;
	static string mapIdent;
	static string[] contentIdent;
	static object net;

	static int lastHash;

	static RealTimeUntil timeUntilThink;


	internal static void Tick()
	{
		if ( timeUntilThink > 0 ) return;

		// must be a tool or something?
		if ( Application.IsHeadless ) return;

		timeUntilThink = Random.Shared.Float( 2, 4 );

		// Package identity is available during installation. Only a ready host or activated client
		// has reached gameplay; failed downloads must never open an activity session.
		var ready = IGameInstance.Current is { IsLoading: false }
			&& (!Networking.IsActive || Networking.IsHost || Connection.Local?.State == Connection.ChannelState.Connected);
		var nextGame = ready ? Application.GameIdent : null;
		var gameChanged = gameIdent != nextGame;
		gameIdent = nextGame;
		gameVersion = Application.GamePackage?.Revision?.VersionId.ToString() ?? "";
		mapIdent = Application.MapPackage?.FullIdent ?? "";

		// get a list of installed packages
		contentIdent = PackageManager.ActivePackages
								.Where( x => x.Package is RemotePackage )
								.Select( x => x.Package.FullIdent )
								.ToArray();

		net = SampleNetwork();

		CheckHash();
		if ( gameChanged ) timeUntilNextUpdate = 0;
		TryUpdateActivity();
	}

	static void CheckHash()
	{
		var hc = new HashCode();

		hc.Add( gameIdent );
		hc.Add( mapIdent );

		for ( int i = 0; i < contentIdent.Length; i++ )
			hc.Add( contentIdent[i] );

		if ( lastHash == hc.ToHashCode() )
			return;

		lastHash = hc.ToHashCode();
		timeUntilNextUpdate = 1.0f; // debounce
	}

	internal static void TryUpdateActivity()
	{
		if ( timeUntilNextUpdate > 0.0f )
			return;

		timeUntilNextUpdate = 60.0f * 1.0f;

		Api.Activity.QueueUpdate( gameIdent, gameVersion, mapIdent, contentIdent, net );
	}

	internal static void GameClosed()
	{
		if ( Application.IsHeadless || Application.IsEditor || Application.IsDedicatedServer ) return;
		gameIdent = null;
		timeUntilThink = 0;
		Api.Activity.QueueUpdate( null, null, null, null );
	}

	/// <summary>
	/// Solo, hosting or joined, and how many others were there - playing alone is the norm for new players.
	/// And how many are in our party, 0 without one.
	/// </summary>
	static object SampleNetwork()
	{
		var party = PartyRoom.Current?.MemberCount ?? 0;

		if ( !Networking.IsActive )
			return new { mode = "solo", players = 1, max = 1, party };

		return new
		{
			mode = Networking.IsHost ? "host" : "client",
			players = Connection.All.Count( x => x.State == Connection.ChannelState.Connected ),
			max = Networking.MaxPlayers,
			party,
		};
	}
}
