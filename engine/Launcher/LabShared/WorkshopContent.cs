using System.Text.RegularExpressions;

namespace Sandbox.Lab;

/// <summary>
/// s&amp;box workshop items a game installed, for the labs, which have no Steam workshop of their own: each of Steam's
/// libraries' <c>steamapps/workshop/content/590830/id</c>. <c>-workshop-content dir</c> is looked in first, a folder of
/// items by id, for a machine where the game isn't installed.
/// </summary>
internal static class WorkshopContent
{
	/// <summary>The folders items may be in, <c>-workshop-content</c> first.</summary>
	public static IEnumerable<string> Folders()
	{
		if ( Args.String( "-workshop-content" ) is { } given ) yield return System.IO.Path.GetFullPath( given );

		foreach ( var library in Libraries() )
			yield return System.IO.Path.Combine( library, "steamapps", "workshop", "content", "590830" );
	}

	/// <summary>The installed item's folder, or null.</summary>
	public static string Find( ulong id ) => Folders().Select( x => System.IO.Path.Combine( x, id.ToString() ) ).FirstOrDefault( System.IO.Directory.Exists );

	/// <summary>Steam's libraries: Steam's own folder and the others libraryfolders.vdf lists.</summary>
	static IEnumerable<string> Libraries()
	{
		var steam = OperatingSystem.IsWindows()
			? Microsoft.Win32.Registry.GetValue( @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null ) as string ?? @"C:\Program Files (x86)\Steam"
			: System.IO.Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".local", "share", "Steam" );

		var libraries = new List<string> { steam };
		var vdf = System.IO.Path.Combine( steam, "steamapps", "libraryfolders.vdf" );
		if ( System.IO.File.Exists( vdf ) )
		{
			foreach ( Match m in Regex.Matches( System.IO.File.ReadAllText( vdf ), @"""path""\s+""([^""]+)""" ) )
				libraries.Add( m.Groups[1].Value.Replace( @"\\", @"\" ) );
		}

		return libraries.Distinct( OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal );
	}
}
