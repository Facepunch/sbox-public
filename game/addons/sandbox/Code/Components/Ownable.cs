using System.Text.Json.Serialization;

/// <summary>
/// Tracks which connection spawned this object
/// </summary>
public sealed class Ownable : Component
{
	[Sync( SyncFlags.FromHost )]
	private Guid _ownerId { get; set; }

	/// <summary>
	/// I would fucking love to be able to Sync these..
	/// And it would just do this exact behaviour. Why not?
	/// </summary>
	[Property, ReadOnly, JsonIgnore]
	public Connection Owner
	{
		get => Connection.Find( _ownerId );
		internal set => _ownerId = value?.Id ?? Guid.Empty;
	}

	/// <summary>
	/// Sets a gameObject as owned by a connection. If the gameObject already has an Ownable component, it'll just update the owner.
	/// </summary>
	/// <param name="go"></param>
	/// <param name="owner"></param>
	/// <returns></returns>
	public static Ownable Set( GameObject go, Connection owner )
	{
		var ownable = go.GetOrAddComponent<Ownable>();
		ownable.Owner = owner;
		return ownable;
	}

	/// <summary>
	/// When enabled, players can only change objects they own. Host and admins are always exempt.
	/// </summary>
	[Title( "Prop Protection" )]
	[ConVar( "sandbox.ownership.checks", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Enforce ownership, players can only interact with their own props." )]
	public static bool OwnershipChecks { get; private set; } = false;

	internal bool CallerHasAccess( Connection caller )
	{
		if ( !OwnershipChecks ) return true;
		if ( Permissions.Has( caller, Permissions.Admin ) ) return true;

		var owner = Owner;
		return owner is null || owner == caller;
	}
}

public static class OwnableExtensions
{
	/// <summary>
	/// Whether a host RPC from <paramref name="caller"/> may change this object. A player and everything
	/// they carry only answers to that player, props follow <see cref="Ownable.OwnershipChecks"/>.
	/// </summary>
	public static bool HasAccess( this GameObject go, Connection caller )
	{
		if ( !go.IsValid() || caller is null ) return false;
		if ( caller.IsHost ) return true;

		if ( go.FindPlayer() is { } player )
			return player.Network.Owner == caller || Permissions.Has( caller, Permissions.Admin );

		// Ownable sits on the spawned root, callers often pass one of its parts
		var ownable = go.Components.Get<Ownable>( FindMode.Enabled | FindMode.Disabled | FindMode.InSelf | FindMode.InAncestors );
		return !ownable.IsValid() || ownable.CallerHasAccess( caller );
	}

	/// <summary>
	/// The player this object belongs to. Not the scene root, seats and vehicles parent the player.
	/// </summary>
	public static Player FindPlayer( this GameObject go )
		=> go.Components.Get<Player>( FindMode.Enabled | FindMode.Disabled | FindMode.InSelf | FindMode.InAncestors );
}
