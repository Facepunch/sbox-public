namespace Sandbox.Npcs;

public partial class Npc : Component
{
	/// <summary>
	/// The faction this NPC belongs to -- its identity to others, like a Source NPC's class.
	/// Override per NPC type.
	/// </summary>
	public virtual string Faction => "neutral";

	// This NPC's default stance toward other factions, built once from SetupRelationships().
	private Dictionary<string, Relationship> _factionRelationships;

	// Per-entity overrides (the GMod AddEntityRelationship) -- win over faction defaults.
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
	/// Declare this NPC's relationships toward other factions with <see cref="Hates(string[])"/> /
	/// <see cref="Fears"/> / <see cref="Likes"/>. Anything not listed is neutral (ignored).
	/// Override per NPC type.
	/// </summary>
	protected virtual void SetupRelationships() { }

	/// <summary>Attack members of these factions.</summary>
	protected void Hates( params string[] factions ) => SetFactionRelationship( Disposition.Hostile, 0, factions );

	/// <summary>Attack members of a faction, with a target-selection priority.</summary>
	protected void Hates( string faction, int priority ) => SetFactionRelationship( Disposition.Hostile, priority, faction );

	/// <summary>Flee members of these factions.</summary>
	protected void Fears( params string[] factions ) => SetFactionRelationship( Disposition.Fearful, 0, factions );

	/// <summary>Treat members of these factions as allies.</summary>
	protected void Likes( params string[] factions ) => SetFactionRelationship( Disposition.Friendly, 0, factions );

	private void SetFactionRelationship( Disposition disposition, int priority, params string[] factions )
	{
		foreach ( var faction in factions )
			_factionRelationships[faction] = new Relationship( disposition, priority );
	}

	/// <summary>
	/// Override how this NPC regards a specific entity, ignoring faction defaults -- e.g. a
	/// cop turning hostile toward someone it saw commit a crime.
	/// </summary>
	public void SetDisposition( GameObject other, Disposition disposition, int priority = 0 )
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
	public void ClearDisposition( GameObject other )
	{
		_entityRelationships.Remove( other );
	}

	/// <summary>
	/// The full relationship (disposition + priority) this NPC has toward another entity.
	/// Per-entity overrides win, then the faction default, then neutral. Override this for
	/// fully dynamic relationships (the equivalent of Source's IRelationType).
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

		return new Relationship( Disposition.Neutral );
	}

	/// <summary>How this NPC regards another entity.</summary>
	public Disposition GetDisposition( GameObject other ) => GetRelationship( other ).Disposition;

	/// <summary>
	/// Resolve any entity's faction -- an NPC's own faction, or the player faction. Returns
	/// null for things that don't take part in relationships.
	/// </summary>
	public static string GetFactionOf( GameObject go )
	{
		if ( !go.IsValid() )
			return null;

		var npc = go.GetComponent<Npc>() ?? go.Root?.GetComponent<Npc>();
		if ( npc.IsValid() )
			return npc.Faction;

		if ( go.Tags.Has( Factions.Player ) || (go.Root.IsValid() && go.Root.Tags.Has( Factions.Player )) )
			return Factions.Player;

		return null;
	}
}
