public static class SpawnMenuCategory
{
	public static string Root( string category )
	{
		var normalized = Normalize( category );
		if ( string.IsNullOrEmpty( normalized ) ) return "Other";

		var separator = normalized.IndexOf( '/' );
		return separator < 0 ? normalized : normalized[..separator];
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
