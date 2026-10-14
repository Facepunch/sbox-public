using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Cli;

/// <summary>
/// Agent skills. The full skills ship beside the CLI so they always match this installation;
/// a small bootstrap skill, installed into an agent once, tells it how to find them.
/// </summary>
internal static class Skills
{
	const string BootstrapName = "sbox";

	/// <summary>
	/// Every copy of the bootstrap carries this frontmatter key. Only copies with it are ever
	/// replaced or removed, so a skill someone else put at the same path is never touched.
	/// </summary>
	const string BootstrapMarker = "sbox-bootstrap:";

	static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	/// <summary>
	/// User-level skill directories, keyed by the name passed to --skill-install. Detected means the
	/// agent appears to be installed, judged by its own folder in the user's home directory.
	/// </summary>
	static IEnumerable<(string Name, string Agents, string Directory, bool Detected)> Targets()
	{
		var home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
		yield return ("claude", "Claude Code", Path.Combine( home, ".claude", "skills" ),
			Directory.Exists( Path.Combine( home, ".claude" ) ));
		yield return ("agents", "Codex and other agents that read ~/.agents/skills", Path.Combine( home, ".agents", "skills" ),
			Directory.Exists( Path.Combine( home, ".codex" ) ) || Directory.Exists( Path.Combine( home, ".agents" ) ));
	}

	static string Root => Path.GetFullPath( Path.Combine( AppContext.BaseDirectory, "skills" ) );

	/// <summary>
	/// Print the skill index, a skill's SKILL.md, or one file inside a skill.
	/// </summary>
	internal static int Print( string target )
	{
		var root = Root;
		if ( !Directory.Exists( root ) )
			throw new DirectoryNotFoundException( $"No skills ship beside this CLI ({root})." );

		using var output = Utf8( Console.OpenStandardOutput() );

		if ( string.IsNullOrWhiteSpace( target ) )
		{
			output.WriteLine( $"Agent skills for this s&box installation, in {root}" );
			output.WriteLine( "Read one with: sbox-cli --skill <name>" );
			output.WriteLine();

			foreach ( var directory in Directory.GetDirectories( root ).Order( StringComparer.Ordinal ) )
			{
				var file = Path.Combine( directory, "SKILL.md" );
				if ( File.Exists( file ) )
					output.WriteLine( $"- {Path.GetFileName( directory )}: {Frontmatter( File.ReadAllText( file ), "description" ) ?? "(no description)"}" );
			}

			return 0;
		}

		var path = Path.GetFullPath( Path.Combine( root, target ) );
		if ( !path.StartsWith( root + Path.DirectorySeparatorChar, PathComparison ) )
			throw new ArgumentException( $"{target} is outside the skills directory. Run --skill to list skills." );

		if ( Directory.Exists( path ) )
			path = Path.Combine( path, "SKILL.md" );

		if ( !File.Exists( path ) )
			throw new FileNotFoundException( $"No skill file '{target}'. Run --skill to list skills.", path );

		var skill = Path.GetRelativePath( root, path ).Split( Path.DirectorySeparatorChar )[0];
		output.WriteLine( $"<!-- {path}. Read files it links to with: sbox-cli --skill {skill}/<relative path> -->" );
		output.Write( File.ReadAllText( path ) );
		return 0;
	}

	/// <summary>
	/// Report where the bootstrap skill is installed, as JSON.
	/// </summary>
	internal static int Status()
	{
		var bootstrap = Bootstrap();
		var states = new JsonArray();

		foreach ( var target in Targets() )
		{
			var file = Path.Combine( target.Directory, BootstrapName, "SKILL.md" );
			states.Add( new JsonObject
			{
				["target"] = target.Name,
				["agents"] = target.Agents,
				["path"] = file,
				["detected"] = target.Detected,
				["state"] = State( file, bootstrap )
			} );
		}

		using var output = Utf8( Console.OpenStandardOutput() );
		output.WriteLine( states.ToJsonString( Program.Readable ) );
		return 0;
	}

	/// <summary>
	/// Install or update the bootstrap skill for one target, or every agent found when target is null.
	/// </summary>
	internal static int Install( string target )
	{
		var bootstrap = Bootstrap();
		var failed = false;
		var targets = Select( target, detectedOnly: true ).ToArray();

		if ( targets.Length == 0 )
		{
			Console.Error.WriteLine( $"No supported agents found. Name a target to install anyway: {string.Join( ", ", Targets().Select( x => x.Name ) )}." );
			return 1;
		}

		foreach ( var (name, _, directory, _) in targets )
		{
			var skillDirectory = Path.Combine( directory, BootstrapName );
			var file = Path.Combine( skillDirectory, "SKILL.md" );

			switch ( State( file, bootstrap ) )
			{
				case "current":
					Console.WriteLine( $"{name}: already up to date at {file}" );
					break;
				case "foreign":
					Console.Error.WriteLine( $"{name}: skipped, {file} wasn't installed by s&box. Remove it to install the s&box skill there." );
					failed = true;
					break;
				default:
					if ( IsLink( directory ) || IsLink( skillDirectory ) )
					{
						Console.Error.WriteLine( $"{name}: skipped, {skillDirectory} is reached through a symbolic link." );
						failed = true;
						break;
					}

					Directory.CreateDirectory( skillDirectory );
					File.WriteAllText( file, bootstrap, new UTF8Encoding( false ) );
					Console.WriteLine( $"{name}: installed {file}" );
					break;
			}
		}

		return failed ? 1 : 0;
	}

	/// <summary>
	/// Remove the bootstrap skill from one target, or all of them when target is null.
	/// </summary>
	internal static int Uninstall( string target )
	{
		var failed = false;

		foreach ( var (name, _, directory, _) in Select( target, detectedOnly: false ) )
		{
			var skillDirectory = Path.Combine( directory, BootstrapName );
			var file = Path.Combine( skillDirectory, "SKILL.md" );

			switch ( State( file, null ) )
			{
				case "missing":
					Console.WriteLine( $"{name}: not installed" );
					break;
				case "foreign":
					Console.Error.WriteLine( $"{name}: skipped, {file} wasn't installed by s&box." );
					failed = true;
					break;
				default:
					File.Delete( file );

					// Tidy the folders up to .claude / .agents, but only while they're empty.
					foreach ( var folder in new[] { skillDirectory, directory, Path.GetDirectoryName( directory ) } )
					{
						if ( Directory.EnumerateFileSystemEntries( folder ).Any() )
							break;

						Directory.Delete( folder );
					}

					Console.WriteLine( $"{name}: removed {file}" );
					break;
			}
		}

		return failed ? 1 : 0;
	}

	/// <summary>
	/// missing, current, outdated (an older s&amp;box bootstrap), or foreign (someone else's skill).
	/// </summary>
	static string State( string file, string bootstrap )
	{
		if ( !File.Exists( file ) )
			return "missing";

		var existing = File.ReadAllText( file );
		if ( Frontmatter( existing, BootstrapMarker.TrimEnd( ':' ) ) is null )
			return "foreign";

		return bootstrap is not null && Normalize( existing ) == Normalize( bootstrap ) ? "current" : "outdated";
	}

	/// <summary>
	/// The named target, or with none: every target to uninstall from, but only the agents found on
	/// this machine to install into, so no folder is created for an agent the user doesn't have.
	/// </summary>
	static IEnumerable<(string Name, string Agents, string Directory, bool Detected)> Select( string target, bool detectedOnly )
	{
		if ( target is null )
			return Targets().Where( x => x.Detected || !detectedOnly );

		var match = Targets().Where( x => x.Name.Equals( target, StringComparison.OrdinalIgnoreCase ) ).ToArray();
		if ( match.Length == 0 )
			throw new ArgumentException( $"Unknown skill target '{target}'. Use one of: {string.Join( ", ", Targets().Select( x => x.Name ) )}." );

		return match;
	}

	static string Bootstrap()
	{
		using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream( "sbox-bootstrap.md" )
			?? throw new InvalidOperationException( "The bootstrap skill isn't embedded in this build." );
		using var reader = new StreamReader( stream, Encoding.UTF8 );
		return reader.ReadToEnd();
	}

	/// <summary>
	/// A single-line value from a SKILL.md's YAML frontmatter, at any indentation.
	/// </summary>
	static string Frontmatter( string text, string key )
	{
		using var reader = new StringReader( text );
		if ( reader.ReadLine()?.Trim() != "---" )
			return null;

		while ( reader.ReadLine() is { } line && line.Trim() != "---" )
		{
			var trimmed = line.TrimStart();
			if ( trimmed.StartsWith( key + ":", StringComparison.Ordinal ) )
				return trimmed[(key.Length + 1)..].Trim().Trim( '"' );
		}

		return null;
	}

	static bool IsLink( string path ) => Directory.Exists( path ) && new DirectoryInfo( path ).LinkTarget is not null;

	static string Normalize( string text ) => text.Replace( "\r\n", "\n" ).Trim();

	internal static StreamWriter Utf8( Stream stream ) => new( stream, new UTF8Encoding( false ) );
}
