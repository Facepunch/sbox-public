using Sandbox.Diagnostics;
using System;
using static Sandbox.Diagnostics.PerformanceStats;

internal static class Analytics
{
	static RealTimeUntil timeUntilNextUpdate;

	static string gameIdent;
	static string gameVersion;
	static string mapIdent;
	static object net;

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
		var nextMap = Application.Map?.Text ?? "";
		var mapChanged = mapIdent != nextMap;
		gameIdent = nextGame;
		gameVersion = Application.GamePackage?.Revision?.VersionId.ToString() ?? "";
		mapIdent = nextMap;

		net = SampleNetwork();

		// Steam counts the workshop items' playtime from what the backend's told - games and maps alike
		if ( ready && !Application.IsEditor && !Application.IsDedicatedServer )
			Workshop.SetPlaying( Application.GamePackage?.WorkshopId ?? 0, Application.Map?.WorkshopId ?? 0 );
		else
			Workshop.SetPlaying();

		if ( gameChanged ) timeUntilNextUpdate = 0;
		else if ( mapChanged ) timeUntilNextUpdate = 1.0f;

		TryUpdateActivity();
	}

	internal static void TryUpdateActivity()
	{
		if ( timeUntilNextUpdate > 0.0f )
			return;

		timeUntilNextUpdate = 60.0f * 1.0f;

		var serverAddons = Application.IsDedicatedServer
			? PackageManager.ActivePackages.Where( x => x.Package is RemotePackage ).Select( x => x.Package.FullIdent ).ToArray()
			: null;

		Api.Activity.QueueUpdate( gameIdent, gameVersion, mapIdent, serverAddons, net );
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
