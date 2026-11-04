namespace NetSim;

/// <summary>
/// Compares what every peer sees against the peer with authority over each object.
/// </summary>
internal static class WorldCheck
{
	readonly record struct ObjectState( Guid Id, Guid Owner, Vector3 Position, Rotation Rotation, string Values );

	static Dictionary<Guid, ObjectState> Capture( NetSimulation sim, SimPeer peer )
	{
		return sim.As( peer, () =>
		{
			var result = new Dictionary<Guid, ObjectState>();

			foreach ( var go in Game.ActiveScene.GetAllObjects( false ) )
			{
				if ( !go.IsNetworkRoot )
					continue;

				string values;
				if ( go.Components.Get<SimProp>() is { } prop )
					values = $"health {prop.Health} tint {prop.Tint.Hex} heat {prop.Heat}";
				else if ( go.Components.Get<SimPlayer>() is { } player )
					values = $"aim {player.Aim} ammo {player.Ammo} inventory [{string.Join( ",", player.Inventory )}]";
				else
					continue;

				result[go.Id] = new ObjectState( go.Id, go.Network.OwnerId, go.WorldPosition, go.WorldRotation, values );
			}

			return result;
		} );
	}

	/// <summary>
	/// Every difference between peers, empty when the world agrees everywhere.
	/// </summary>
	public static List<string> Differences( NetSimulation sim, int max = 20 )
	{
		var peers = sim.Peers.ToList();
		var states = peers.ToDictionary( p => p, p => Capture( sim, p ) );
		var byLocalId = peers.ToDictionary( p => p.Local.Id );
		var diffs = new List<string>();

		void Add( string s )
		{
			if ( diffs.Count < max ) diffs.Add( s );
			else if ( diffs.Count == max ) diffs.Add( "..." );
		}

		var host = states[sim.Host];

		foreach ( var (id, hostView) in host )
		{
			// The owner's copy is the truth for owned objects, the host's for the rest
			var authority = hostView.Owner != Guid.Empty && byLocalId.TryGetValue( hostView.Owner, out var owner ) ? owner : sim.Host;
			if ( !states[authority].TryGetValue( id, out var truth ) )
			{
				Add( $"{id}: owner {authority} doesn't have it" );
				continue;
			}

			foreach ( var peer in peers )
			{
				if ( peer == authority )
					continue;

				if ( !states[peer].TryGetValue( id, out var seen ) )
				{
					Add( $"{id}: missing on {peer}" );
					continue;
				}

				if ( seen.Owner != truth.Owner ) Add( $"{id} on {peer}: owner {seen.Owner} vs {truth.Owner}" );
				if ( seen.Position.Distance( truth.Position ) > 0.1f ) Add( $"{id} on {peer}: position {seen.Position} vs {truth.Position} ({authority})" );
				if ( seen.Rotation.Distance( truth.Rotation ) > 0.5f ) Add( $"{id} on {peer}: rotation {seen.Rotation} vs {truth.Rotation}" );
				if ( seen.Values != truth.Values ) Add( $"{id} on {peer}: {seen.Values} vs {truth.Values} ({authority})" );
			}
		}

		foreach ( var peer in peers )
		{
			foreach ( var id in states[peer].Keys )
			{
				if ( !host.ContainsKey( id ) )
					Add( $"{id}: on {peer} but not on the host" );
			}
		}

		return diffs;
	}

	/// <summary>
	/// Reliable broadcast RPCs that didn't run exactly once on every peer.
	/// </summary>
	public static List<string> RpcProblems( NetSimulation sim, params string[] broadcastMethods )
	{
		var peers = sim.Peers.ToList();
		var problems = new List<string>();

		foreach ( var call in RpcLog.Entries.Where( e => broadcastMethods.Contains( e.Method ) ).GroupBy( e => (e.Method, e.Seq) ) )
		{
			var calledAt = call.Min( e => e.Frame );

			foreach ( var peer in peers )
			{
				// Calls made before a peer finished joining don't reach it
				if ( calledAt <= peer.JoinedFrame )
					continue;

				var count = call.Count( e => e.Peer == peer.Name );
				if ( count != 1 && problems.Count < 20 )
					problems.Add( $"{call.Key.Method} #{call.Key.Seq} ran {count} times on {peer}" );
			}
		}

		return problems;
	}
}
