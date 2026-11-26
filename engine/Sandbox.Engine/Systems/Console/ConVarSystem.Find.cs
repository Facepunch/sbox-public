namespace Sandbox;

internal static partial class ConVarSystem
{
	/// <summary>
	/// Lists visible console variables and commands whose name or help text contains the search term.
	/// </summary>
	[ConCmd( "find", ConVarFlags.Protected )]
	public static void CmdFind( string partial )
	{
		var results = Members.Values
			.Where( x => !x.IsHidden )
			.Where( x => x.Name.Contains( partial, StringComparison.OrdinalIgnoreCase )
				|| x.Help?.Contains( partial, StringComparison.OrdinalIgnoreCase ) == true )
			.ToArray();

		foreach ( var c in results.OrderBy( x => x.Name ) )
		{
			Log.Info( $"{c.Name} - {c.BuildDescription()}" );
		}
	}
}
