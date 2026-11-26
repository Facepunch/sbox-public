namespace Sandbox.Lab;

/// <summary>
/// The command line. A value follows its name (<c>-name value</c>) or is joined to it (<c>-name=value</c>).
/// </summary>
internal static class Args
{
	public static string String( string name )
	{
		var args = Environment.GetCommandLineArgs();
		for ( int i = 0; i < args.Length; i++ )
		{
			if ( args[i].Equals( name, StringComparison.OrdinalIgnoreCase ) ) return i + 1 < args.Length ? args[i + 1] : null;
			if ( IsJoined( args[i], name ) ) return args[i][(name.Length + 1)..];
		}

		return null;
	}

	public static bool Has( string name ) => Environment.GetCommandLineArgs().Any( x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) || IsJoined( x, name ) );

	static bool IsJoined( string arg, string name ) => arg.Length > name.Length && arg[name.Length] == '=' && arg.StartsWith( name, StringComparison.OrdinalIgnoreCase );

	public static int Int( string name, int fallback ) => int.TryParse( String( name ), out var value ) ? value : fallback;

	public static float Float( string name, float fallback ) => float.TryParse( String( name ), System.Globalization.CultureInfo.InvariantCulture, out var value ) ? value : fallback;
}
