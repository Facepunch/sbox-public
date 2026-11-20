public static class SpawnMenuCategory
{
	public static string Root( string category )
	{
		var normalized = Normalize( category );
		if ( string.IsNullOrEmpty( normalized ) ) return "Other";

		var separator = normalized.IndexOf( '/' );
		return separator < 0 ? normalized : normalized[..separator];
	}

	/// <summary>
	/// The second level of a category, such as the game in "Weapon/Half-Life 2". Null if there isn't one.
	/// </summary>
	public static string Section( string category )
	{
		var parts = Normalize( category ).Split( '/' );
		return parts.Length > 1 ? parts[1] : null;
	}

	public static bool Matches( string category, string parentCategory, bool includeSubcategories = true )
	{
		var path = Normalize( category );
		var parent = Normalize( parentCategory );

		if ( string.IsNullOrEmpty( parent ) ) return false;
		if ( string.IsNullOrEmpty( path ) ) return string.Equals( parent, "Other", StringComparison.OrdinalIgnoreCase );
		if ( string.Equals( path, parent, StringComparison.OrdinalIgnoreCase ) ) return true;

		return includeSubcategories && path.StartsWith( $"{parent}/", StringComparison.OrdinalIgnoreCase );
	}

	static string Normalize( string category )
	{
		return category?.Trim().Trim( '/' ) ?? "";
	}
}
