namespace Sandbox.Npcs;

public partial class BaseNpc : Component
{
	/// <summary>
	/// The faction this NPC belongs to -- its identity to others: one of <see cref="Factions"/> or a game's own.
	/// Its default relationships follow from it, see <see cref="SetupRelationships"/>. Set it on the host: the NPC
	/// then works out afresh who it likes and hates and what to do. An NPC type can override it to compute it.
	/// </summary>
	[Property, Sync( SyncFlags.FromHost )]
	public virtual string Faction
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;

			ResetRelationships();

			if ( Networking.IsHost )
				EndCurrentSchedule();
		}
	} = "neutral";

	// This NPC's default stance toward other factions, built once from SetupRelationships().
	private Dictionary<string, Relationship> _factionRelationships;

	// Per-entity overrides -- win over faction defaults.
	readonly Dictionary<GameObject, Relationship> _entityRelationships = new();

	private Dictionary<string, Relationship> FactionRelationships
	{
		get
		{
			if ( _factionRelationships is null )
			{
				_factionRelationships = new();
				SetupRelationships();
			}

			return _factionRelationships;
		}
	}

	/// <summary>
	/// Declare this NPC's relationships toward other factions with
	/// <see cref="SetRelationship(string, Disposition, int)"/>. Unlisted factions use <see cref="GetFallbackRelationship"/>.
	/// The base takes the defaults of its <see cref="Faction"/>, described on <see cref="Factions"/>.
	/// Override per NPC type, without calling the base to declare all of them yourself.
	/// </summary>
	protected virtual void SetupRelationships()
	{
		foreach ( var (other, stance) in Factions.GetDefaultStances( Faction ) )
			SetRelationship( other, stance );
	}

	/// <summary>
	/// Forget the faction relationships, so <see cref="SetupRelationships"/> runs again when they are next needed.
	/// Call it when something they are built from changes.
	/// </summary>
	protected void ResetRelationships() => _factionRelationships = null;

	/// <summary>
	/// How this NPC regards the members of a faction: hostile to attack them, fearful to flee them, friendly to
	/// treat them as allies. Among hostiles a higher priority is picked first. Call it from
	/// <see cref="SetupRelationships"/>.
	/// </summary>
	protected void SetRelationship( string faction, Disposition disposition, int priority = 0 )
	{
		FactionRelationships[faction] = new Relationship( disposition, priority );
	}

	/// <summary>
	/// Override how this NPC regards a specific entity, ignoring faction defaults -- e.g. a
	/// cop turning hostile toward someone it saw commit a crime.
	/// </summary>
	public void SetRelationship( GameObject other, Disposition disposition, int priority = 0 )
	{
		if ( !other.IsValid() )
			return;

		// Sweep out overrides for destroyed entities so this never grows unbounded.
		List<GameObject> stale = null;
		foreach ( var key in _entityRelationships.Keys )
		{
			if ( !key.IsValid() )
				(stale ??= new()).Add( key );
		}

		if ( stale is not null )
		{
			foreach ( var key in stale )
				_entityRelationships.Remove( key );
		}

		_entityRelationships[other] = new Relationship( disposition, priority );
	}

	/// <summary>Remove a per-entity override, falling back to the faction default.</summary>
	public void ClearRelationship( GameObject other )
	{
		_entityRelationships.Remove( other );
	}

	/// <summary>
	/// The full relationship (disposition + priority) this NPC has toward another entity.
	/// Per-entity overrides win, then the faction relationship, then <see cref="GetFallbackRelationship"/>. Override this for
	/// fully dynamic relationships.
	/// </summary>
	public virtual Relationship GetRelationship( GameObject other )
	{
		if ( !other.IsValid() || other == GameObject )
			return new Relationship( Disposition.Friendly );

		if ( _entityRelationships.TryGetValue( other, out var entityRelationship ) )
			return entityRelationship;

		var faction = GetFactionOf( other );
		if ( faction is not null && FactionRelationships.TryGetValue( faction, out var factionRelationship ) )
			return factionRelationship;

		return GetFallbackRelationship( other );
	}

	/// <summary>
	/// Default stance when no entity override or faction relationship exists. Called during relationship
	/// queries on the main thread; keep it cheap and free of side effects. The base returns neutral.
	/// </summary>
	protected virtual Relationship GetFallbackRelationship( GameObject other ) => new( Disposition.Neutral );

	/// <summary>How this NPC regards another entity.</summary>
	public Disposition GetDisposition( GameObject other ) => GetRelationship( other ).Disposition;

	/// <summary>
	/// Resolve any entity's faction -- an NPC's own faction, or the player faction for anything with a
	/// <see cref="PlayerController"/> or the <see cref="Factions.Player"/> tag, on itself or its root. Returns
	/// null for things that don't take part in relationships.
	/// </summary>
	internal static string GetFactionOf( GameObject go )
	{
		if ( !go.IsValid() )
			return null;

		var npc = go.GetComponent<BaseNpc>() ?? go.Root?.GetComponent<BaseNpc>();
		if ( npc.IsValid() )
			return npc.Faction;

		var root = go.Root;

		if ( go.Tags.Has( Factions.Player ) || (root.IsValid() && root.Tags.Has( Factions.Player )) )
			return Factions.Player;

		if ( go.GetComponent<PlayerController>().IsValid() || (root.IsValid() && root != go && root.GetComponent<PlayerController>().IsValid()) )
			return Factions.Player;

		return null;
	}

	/// <summary>
	/// Whether something can be hurt right now: it has an enabled <see cref="Component.IDamageable"/> on an active
	/// GameObject, on itself or below. NPCs only sense and attack live targets, so a dead player that stays in
	/// the world, or anything that takes no damage, is left alone.
	/// </summary>
	public static bool IsLiveTarget( GameObject go )
	{
		return go.IsValid() && go.GetComponentInChildren<Component.IDamageable>() is Component { Active: true };
	}
}
