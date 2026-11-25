/// <summary>
/// Holds persistent player information like deaths, kills
/// </summary>
public sealed partial class PlayerData : Component
{
	[Sync( SyncFlags.FromHost )] public int Kills { get; internal set; }
	[Sync( SyncFlags.FromHost )] public int Deaths { get; internal set; }
	[Sync( SyncFlags.FromHost )] public bool IsGodMode { get; internal set; }

	/// <summary>
	/// Sent by the host. Check with Permissions.Has instead, it also counts admins.
	/// </summary>
	[Sync( SyncFlags.FromHost )] public NetList<string> Permissions { get; set; } = new();

	protected override void OnStart()
	{
		// runs on join, after a host change and after loading a save
		global::Permissions.Refresh( this );
	}

	/// <summary>
	/// Is this player data me?
	/// </summary>
	public bool IsMe => Network.Owner == Connection.Local;

	/// <summary>
	/// Data for all players
	/// </summary>
	public static IEnumerable<PlayerData> All => Game.ActiveScene.GetAll<PlayerData>();

	/// <summary>
	/// Get player data for a player
	/// </summary>
	public static PlayerData For( Connection connection ) => connection == null ? default : All.FirstOrDefault( x => x.Network.Owner == connection );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void RpcAddStat( string identifier, int amount = 1 )
	{
		Sandbox.Services.Stats.Increment( identifier, amount );
	}

	/// <summary>
	/// Called on the host, calls a RPC on the player and adds a stat
	/// </summary>
	internal void AddStat( string identifier, int amount = 1 )
	{
		if ( Application.CheatsEnabled ) return;

		Assert.True( Networking.IsHost, "PlayerData.AddStat is host-only!" );

		using ( Rpc.FilterInclude( Network.Owner ) )
		{
			RpcAddStat( identifier, amount );
		}
	}
}
