namespace Sandbox;

public partial class BaseCombatWeapon
{
	//
	// How this weapon's bullets fly. Drives the default PrimaryAttack, so a data-configured gun works
	// with no code - HL2 kept this split between weapon scripts, skill convars and hardcoded C++;
	// here it's all one struct on the weapon.
	//

	/// <summary>
	/// Ballistics for a volley of bullets - damage, pellets, spread and reach.
	/// </summary>
	public record struct BallisticConfig()
	{
		/// <summary>Damage dealt by each pellet.</summary>
		public float Damage { get; set; } = 12f;

		/// <summary>Pellets per shot - 1 for a bullet, more for a shotgun blast.</summary>
		public int Pellets { get; set; } = 1;

		/// <summary>How far each pellet travels.</summary>
		public float Range { get; set; } = 4096f;

		/// <summary>Pellet radius - 0 for a thin ray, higher to be forgiving.</summary>
		public float Radius { get; set; } = 1f;

		/// <summary>Impulse applied to the physics body a pellet hits.</summary>
		public float Force { get; set; } = 3000f;

		/// <summary>Spread cone in degrees when settled - x wide, y tall.</summary>
		public Vector2 SpreadBase { get; set; } = new( 0.2f, 0.2f );

		/// <summary>Extra spread in degrees at full bloom, added by recent firing.</summary>
		public Vector2 SpreadGrowth { get; set; } = new( 1.5f, 1.5f );

		/// <summary>Seconds after a shot for the spread to settle back to base.</summary>
		public float SpreadRecovery { get; set; } = 0.2f;
	}

	/// <summary>
	/// How the default <see cref="PrimaryAttack"/> shoots - damage, pellets, spread and reach.
	/// </summary>
	[Property, Feature( "Shooting" )] public BallisticConfig Ballistics { get; set; } = new();

	/// <summary>
	/// Time since the last shot - drives spread bloom.
	/// </summary>
	protected TimeSince TimeSinceShoot { get; set; }

	/// <summary>
	/// How bloomed the spread is - 0 settled, 1 right after a shot.
	/// </summary>
	protected float SpreadBloom => Ballistics.SpreadRecovery <= 0f ? 0f : TimeSinceShoot.Relative.Remap( 0, Ballistics.SpreadRecovery, 1, 0 );

	/// <summary>
	/// Runtime multiplier on the spread cone - 1 is the configured ballistics. NPCs set this to the
	/// weapon's <see cref="Npc"/> spread scale times their own skill when they equip it.
	/// </summary>
	public float SpreadScale { get; set; } = 1f;

	/// <summary>
	/// The current spread cone in degrees - the base cone widened by recent firing. Override to
	/// modify it (e.g. narrow while aiming down sights).
	/// </summary>
	public virtual Vector2 CurrentSpread => (Ballistics.SpreadBase + Ballistics.SpreadGrowth * SpreadBloom) * SpreadScale;
}
