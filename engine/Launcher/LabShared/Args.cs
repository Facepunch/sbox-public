namespace Sandbox.Lab;

/// <summary>
/// The command line.
/// </summary>
internal static class Args
{
	public static string String( string name )
	{
		var args = Environment.GetCommandLineArgs();
		var index = Array.FindIndex( args, x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );
		return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
	}

	public static bool Has( string name ) => Environment.GetCommandLineArgs().Any( x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );

	public static int Int( string name, int fallback ) => int.TryParse( String( name ), out var value ) ? value : fallback;

	public static float Float( string name, float fallback ) => float.TryParse( String( name ), System.Globalization.CultureInfo.InvariantCulture, out var value ) ? value : fallback;
}
