using Sandbox.Npcs;

/// <summary>
/// Someone or something on either end of damage - a player, an NPC, a prop, or nothing for the world.
/// </summary>
public readonly struct Target
{
	public GameObject GameObject { get; }

	public Target( GameObject gameObject )
	{
		GameObject = gameObject;
	}

	public bool IsValid => GameObject.IsValid();

	/// <summary>
	/// Find a component on the object or its parents, like the <see cref="Player"/> or <see cref="Npc"/> behind it.
	/// </summary>
	public T Get<T>() where T : class => IsValid ? GameObject.GetComponentInParent<T>( true ) : null;

	public bool IsPlayer => Get<Player>() is not null;
	public bool IsNpc => Get<Npc>() is not null;

	public static implicit operator Target( GameObject gameObject ) => new( gameObject );
	public static implicit operator GameObject( Target target ) => target.GameObject;

	public override string ToString() => GameObject?.Name ?? "none";
}
