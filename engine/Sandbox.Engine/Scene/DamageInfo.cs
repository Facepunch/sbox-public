namespace Sandbox;

/// <summary>
/// Describes the damage that should be done to something. This is purposefully a class
/// so it can be derived from, allowing games to create their own special types of damage, while
/// not having to create a whole new system.
/// </summary>
[Expose]
public class DamageInfo
{
	/// <summary>
	/// Usually a player or Npc
	/// </summary>
	public GameObject Attacker { get; set; }

	/// <summary>
	/// The weapon that did the damage, or a vehicle etc
	/// </summary>
	public GameObject Weapon { get; set; }

	/// <summary>
	/// The hitbox that we hit (if any)
	/// </summary>
	public Hitbox Hitbox { get; set; }

	/// <summary>
	/// Amount of damage this should do
	/// </summary>
	public float Damage { get; set; }

	/// <summary>
	/// The origin of the damage. For bullets this would be the shooter's eye position. For explosions, this would be the center of the exposion.
	/// </summary>
	public Vector3 Origin { get; set; }

	/// <summary>
	/// The location of the damage on the hit object.
	/// </summary>
	public Vector3 Position { get; set; }

	/// <summary>
	/// The physics shape that we hit (if any)
	/// </summary>
	public PhysicsShape Shape { get; set; }

	/// <summary>
	/// The physics force vector to apply to whatever this damage hits. Direction and magnitude
	/// encode the intended push; consumers multiply by their own mass to derive an impulse.
	/// </summary>
	public Vector3 Force { get; set; }

	/// <summary>
	/// Tags for this damage, allows you to enter and read different damage types etc
	/// </summary>
	public TagSet Tags { get; set; } = new();

	/// <summary>
	/// True if this is explosive damage
	/// </summary>
	[Obsolete( "Use Tags" )]
	public bool IsExplosion
	{
		get => Tags.Has( "explosion" );
		set => Tags.Set( "explosion", value );
	}

	public DamageInfo()
	{

	}

	[ActionGraphInclude]
	public DamageInfo( float damage, GameObject attacker, GameObject weapon )
	{
		Damage = damage;
		Attacker = attacker;
		Weapon = weapon;
	}

	public DamageInfo( float damage, GameObject attacker, GameObject weapon, Hitbox hitbox )
	{
		Damage = damage;
		Attacker = attacker;
		Weapon = weapon;
		Hitbox = hitbox;
	}

}
