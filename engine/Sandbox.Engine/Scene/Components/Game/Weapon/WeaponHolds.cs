namespace Sandbox;

/// <summary>
/// Well-known names for how a weapon is held (<see cref="BaseCombatWeapon.HoldType"/>). Games may use their own names too.
/// </summary>
public static class WeaponHolds
{
	public const string None = "none";
	public const string Pistol = "pistol";
	public const string PistolTwoHanded = "pistol_two_handed";
	public const string DualPistols = "dual_pistols";
	public const string Smg = "smg";
	public const string Rifle = "rifle";
	public const string Shotgun = "shotgun";

	/// <summary>Rested on the shoulder, like a rocket launcher.</summary>
	public const string Shoulder = "shoulder";
	public const string Melee = "melee";
	public const string MeleeTwoHanded = "melee_two_handed";
	public const string Knife = "knife";
	public const string Fists = "fists";
	public const string Throwable = "throwable";
	public const string Item = "item";
	public const string Tool = "tool";

	/// <summary>Every well-known name.</summary>
	internal static IReadOnlyList<string> All { get; } =
	[
		None, Pistol, PistolTwoHanded, DualPistols, Smg, Rifle, Shotgun, Shoulder, Melee, MeleeTwoHanded, Knife, Fists, Throwable, Item, Tool
	];

	static readonly Dictionary<string, string> Names = BuildNames();

	static Dictionary<string, string> BuildNames()
	{
		var names = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
		{
			["rpg"] = Shoulder,
			["physgun"] = Tool,
			["holditem"] = Item,
			["melee_punch"] = Fists,
			["melee_weapons"] = Melee,
		};

		foreach ( var name in All )
			names[name] = name;

		return names;
	}

	/// <summary>
	/// The well-known name for <paramref name="name"/>, ignoring case, including the citizen animgraph's holdtype options
	/// (rpg, physgun, holditem, melee_punch, melee_weapons). Any other name is returned unchanged.
	/// </summary>
	public static string Normalize( string name ) => name is not null && Names.TryGetValue( name, out var known ) ? known : name;
}
