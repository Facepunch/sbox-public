namespace NetSim;

/// <summary>
/// Every RPC a peer executed, so tests can check each one ran exactly once everywhere it should.
/// </summary>
internal static class RpcLog
{
	public readonly record struct Entry( string Peer, string Method, Guid Caller, int Seq, int Frame );

	public static readonly List<Entry> Entries = new();

	// The benchmark turns it off, a growing log would count as the engine's allocations
	public static bool Enabled = true;

	public static void Record( string method, int seq )
	{
		if ( !Enabled )
			return;

		Entries.Add( new Entry( SimPeer.Current?.Name ?? "?", method, Rpc.Caller?.Id ?? Connection.Local.Id, seq, NetSimulation.Current?.Frame ?? 0 ) );
	}
}

/// <summary>
/// A prop: anyone can grab it, the host damages it.
/// </summary>
public sealed class SimProp : Component
{
	[Sync] public int Health { get; set; } = 100;
	[Sync] public Color Tint { get; set; } = Color.White;
	[Sync( SyncFlags.FromHost )] public float Heat { get; set; }

	[Rpc.Broadcast]
	public void Hit( int damage, int seq )
	{
		RpcLog.Record( nameof( Hit ), seq );
	}
}

/// <summary>
/// A player: owned by its client, moves and aims every frame, shoots, asks the host to spawn things.
/// </summary>
public sealed class SimPlayer : Component
{
	[Sync] public Vector3 Aim { get; set; }
	[Sync] public int Ammo { get; set; } = 30;
	[Sync] public NetList<int> Inventory { get; set; } = new();

	[Rpc.Broadcast( NetFlags.Unreliable )]
	public void Footstep( Vector3 position )
	{
	}

	[Rpc.Broadcast]
	public void Shoot( Vector3 from, Vector3 direction, int seq )
	{
		RpcLog.Record( nameof( Shoot ), seq );
	}

	[Rpc.Host]
	public void RequestSpawn( Vector3 position, int seq )
	{
		RpcLog.Record( nameof( RequestSpawn ), seq );
		Workload.Current?.SpawnProp( position );
	}
}

/// <summary>
/// Keyed transforms like ModelPhysics body transforms.
/// </summary>
public sealed class SimBodies : Component
{
	[Sync] internal NetworkTransforms Transforms { get; set; } = new();
}

/// <summary>
/// Added to a burning prop, like sandbox's fire.
/// </summary>
public sealed class SimFire : Component
{
	[Sync] public float Intensity { get; set; } = 1;
}
