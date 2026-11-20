namespace Sandbox.Citizen;

/// <summary>
/// The citizen animgraph's "holdtype" options, and the closest one for each well-known weapon hold.
/// </summary>
internal static class CitizenHoldTypes
{
	static readonly HashSet<string> Options = new( StringComparer.OrdinalIgnoreCase )
	{
		"none", "pistol", "rifle", "shotgun", "holditem", "melee_punch", "melee_weapons", "rpg", "physgun"
	};

	/// <summary>True when <paramref name="name"/> is one of the citizen graph's holdtype options.</summary>
	public static bool IsOption( string name ) => !string.IsNullOrEmpty( name ) && Options.Contains( name );

	/// <summary>The citizen holdtype option closest to a <see cref="WeaponHolds"/> name, or null when it isn't one.</summary>
	public static string For( string hold ) => WeaponHolds.Normalize( hold ) switch
	{
		WeaponHolds.Pistol or WeaponHolds.PistolTwoHanded or WeaponHolds.DualPistols or WeaponHolds.Throwable => "pistol",
		WeaponHolds.Smg or WeaponHolds.Rifle => "rifle",
		WeaponHolds.Shotgun => "shotgun",
		WeaponHolds.Shoulder => "rpg",
		WeaponHolds.Melee or WeaponHolds.MeleeTwoHanded or WeaponHolds.Knife => "melee_weapons",
		WeaponHolds.Fists => "melee_punch",
		WeaponHolds.Item => "holditem",
		WeaponHolds.Tool => "physgun",
		WeaponHolds.None => "none",
		_ => null
	};

	/// <summary>The closest citizen holdtype option for a weapon's hold type and its fallback.</summary>
	public static string For( string holdType, string fallback ) => For( holdType ) ?? For( fallback ) ?? "none";

	/// <summary>
	/// The holdtype option to give a graph for a weapon's hold type and its fallback. The hold type itself when the
	/// graph has it, else the citizen option for a well-known hold type, else the same for the fallback. A name the
	/// graph has none of these for is returned unchanged, so a game's own graph still gets its own names.
	/// </summary>
	public static string Resolve<T>( string holdType, string fallback, T graph, Func<T, string, bool> hasOption )
	{
		if ( TryResolve( holdType, graph, hasOption, out var option ) || TryResolve( fallback, graph, hasOption, out option ) )
			return option;

		return string.IsNullOrEmpty( holdType ) ? fallback : holdType;
	}

	static bool TryResolve<T>( string hold, T graph, Func<T, string, bool> hasOption, out string option )
	{
		option = hold;
		if ( string.IsNullOrEmpty( hold ) )
			return false;

		if ( hasOption( graph, hold ) )
			return true;

		option = For( hold );
		return option is not null && hasOption( graph, option );
	}
}
