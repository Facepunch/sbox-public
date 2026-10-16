using System.IO;
using System.Text.RegularExpressions;

namespace Sandbox;

/// <summary>
/// Turns a project template folder into a new project's files. Shared by the launcher's project
/// creators and <c>sbox-cli --new</c>, which links this file, so every way of making a project
/// produces the same thing. Writing the .sbproj is left to the caller.
/// </summary>
internal static class ProjectTemplateFiles
{
	/// <summary>
	/// The template's own project file, read for its settings rather than copied.
	/// </summary>
	internal const string ConfigFileName = "$ident.sbproj";

	/// <summary>
	/// A project ident from its title: lower case letters, digits and underscores, at most 32 characters.
	/// </summary>
	internal static string MakeIdent( string title )
	{
		var ident = Regex.Replace( title.ToLowerInvariant(), "[^a-z0-9_]", "_" ).Trim( '_' );
		return ident.Length > 32 ? ident[..32] : ident;
	}

	/// <summary>
	/// Copy a template into place, replacing $ident in file names and $ident and $title inside .cs and
	/// .json files. Skips the template's project file and hidden folders.
	/// </summary>
	internal static void Copy( string from, string to, string ident, string title )
	{
		Directory.CreateDirectory( to );

		foreach ( var file in Directory.EnumerateFiles( from ) )
		{
			var name = Path.GetFileName( file );
			if ( name == ConfigFileName )
				continue;

			var target = Path.Combine( to, name.Replace( "$ident", ident ) );

			if ( file.EndsWith( ".cs" ) || file.EndsWith( ".json" ) )
			{
				var text = File.ReadAllText( file ).Replace( "$title", title ).Replace( "$ident", ident );
				File.WriteAllText( target, text );
			}
			else
			{
				File.Copy( file, target, true );
			}
		}

		foreach ( var directory in Directory.EnumerateDirectories( from ) )
		{
			var name = Path.GetFileName( directory );
			if ( name.StartsWith( '.' ) )
				continue;

			Copy( directory, Path.Combine( to, name ), ident, title );
		}
	}
}
