using System;
using System.IO;
using System.Text.Json.Nodes;

namespace Sandbox.Cli;

/// <summary>
/// Arguments for one persistent stdio connection, a one-shot tool call, or a discovery command.
/// </summary>
internal sealed class Options
{
	internal string Mode { get; private set; }

	/// <summary>
	/// The .sbproj for --project and --connect, or the project path or package ident for --game.
	/// Null for --connect means attach to the only running instance.
	/// </summary>
	internal string Target { get; private set; }

	/// <summary>
	/// Process ID given to --connect.
	/// </summary>
	internal int? Pid { get; private set; }

	/// <summary>
	/// Tool for a one-shot --call, made over a --connect connection.
	/// </summary>
	internal string CallTool { get; private set; }
	internal JsonObject CallArguments { get; private set; } = new();

	/// <summary>
	/// Template and parent folder for --new. Null means the launcher's default template and the current directory.
	/// </summary>
	internal string TemplateName { get; private set; }
	internal string Folder { get; private set; }

	internal string EngineDirectory { get; private set; } = Path.GetFullPath( Path.Combine( AppContext.BaseDirectory, "../.." ) );
	internal TimeSpan IdleTimeout { get; private set; } = TimeSpan.FromMinutes( 10 );
	internal TimeSpan StartupTimeout { get; private set; } = TimeSpan.FromMinutes( 3 );
	internal TimeSpan RequestTimeout { get; private set; } = TimeSpan.FromMinutes( 5 );

	internal static Options Parse( string[] args )
	{
		var options = new Options();

		for ( int i = 0; i < args.Length; i++ )
		{
			var argument = args[i];
			string Value()
			{
				if ( ++i >= args.Length || args[i].StartsWith( "--" ) )
					throw new ArgumentException( $"{argument} needs a value." );

				return args[i];
			}

			string OptionalValue() => i + 1 < args.Length && !args[i + 1].StartsWith( "--" ) ? args[++i] : null;

			switch ( argument )
			{
				case "--project":
				case "--game":
				case "--connect":
				case "--list":
				case "--help":
				case "--skill":
				case "--skill-status":
				case "--skill-install":
				case "--skill-uninstall":
				case "--new":
				case "--templates":
					if ( options.Mode is not null )
						throw new ArgumentException( "Choose one of --project, --game, --connect, --new, --templates, --list, --skill, --skill-status, --skill-install, --skill-uninstall or --help." );

					options.Mode = argument;
					if ( argument is "--game" or "--new" )
						options.Target = Value();
					else if ( argument is "--project" or "--connect" or "--skill" or "--skill-install" or "--skill-uninstall" )
						options.Target = OptionalValue();
					break;
				case "--call":
					options.CallTool = Value();
					if ( OptionalValue() is { } json )
						options.CallArguments = ParseArguments( json );
					break;
				case "--template":
					options.TemplateName = Value();
					break;
				case "--in":
					options.Folder = Value();
					break;
				case "--engine":
					options.EngineDirectory = Path.GetFullPath( Value() );
					break;
				case "--idle-timeout":
					options.IdleTimeout = Seconds( Value() );
					break;
				case "--startup-timeout":
					options.StartupTimeout = Seconds( Value() );
					break;
				case "--request-timeout":
					options.RequestTimeout = Seconds( Value() );
					break;
				default:
					throw new ArgumentException( $"Unknown option: {argument}" );
			}
		}

		if ( options.CallTool is not null )
		{
			// A one-shot call attaches; launching an instance only to close it again is never what's wanted.
			options.Mode ??= "--connect";
			if ( options.Mode != "--connect" )
				throw new ArgumentException( "--call attaches to a running instance and can only be combined with --connect." );
		}

		if ( (options.TemplateName ?? options.Folder) is not null && options.Mode != "--new" )
			throw new ArgumentException( "--template and --in only apply to --new." );

		options.Mode ??= "--help";

		switch ( options.Mode )
		{
			case "--connect" when options.Target is { } target && !LooksLikePath( target ):
				if ( !int.TryParse( target, out var pid ) || pid <= 0 )
					throw new ArgumentException( "--connect takes a process ID from --list, a .sbproj path or project directory, or nothing for the project in the current directory." );

				options.Pid = pid;
				options.Target = null;
				break;
			case "--connect":
				// Outside any project, a null target attaches to the only running instance.
				options.Target = options.Target is null ? EnclosingProject() : ProjectFile( options.Target );
				break;
			case "--project":
				options.Target = options.Target is null
					? EnclosingProject() ?? throw new FileNotFoundException( "No .sbproj in the current directory or its parents. Pass the project to open." )
					: ProjectFile( options.Target );
				break;
			case "--game" when LooksLikePath( options.Target ):
				options.Target = ProjectFile( options.Target );
				break;
		}

		return options;
	}

	/// <summary>
	/// Distinguish a project path from a PID or a package ident like facepunch.sandbox.
	/// </summary>
	static bool LooksLikePath( string target ) => Directory.Exists( target ) || Path.IsPathRooted( target )
		|| target.Contains( '/' ) || target.Contains( '\\' )
		|| target.EndsWith( ".sbproj", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The .sbproj of the project containing the current directory, if any.
	/// </summary>
	static string EnclosingProject()
	{
		for ( var directory = Directory.GetCurrentDirectory(); directory is not null; directory = Path.GetDirectoryName( directory ) )
		{
			if ( SingleProjectIn( directory ) is { } project )
				return project;
		}

		return null;
	}

	/// <summary>
	/// Resolve a .sbproj file, or the one inside a project directory.
	/// </summary>
	static string ProjectFile( string target )
	{
		target = Path.GetFullPath( target );
		if ( Directory.Exists( target ) )
			return SingleProjectIn( target ) ?? throw new FileNotFoundException( "No .sbproj in the project directory.", target );

		if ( !File.Exists( target ) )
			throw new FileNotFoundException( "Project file does not exist.", target );

		return target;
	}

	static string SingleProjectIn( string directory )
	{
		var projects = Directory.GetFiles( directory, "*.sbproj" );
		if ( projects.Length > 1 )
			throw new ArgumentException( $"{directory} contains several .sbproj files. Pass one explicitly." );

		return projects.Length == 1 ? projects[0] : null;
	}

	static JsonObject ParseArguments( string json )
	{
		try
		{
			if ( JsonNode.Parse( json ) is JsonObject arguments )
				return arguments;
		}
		catch ( System.Text.Json.JsonException )
		{
		}

		throw new ArgumentException( $"--call arguments must be a JSON object, like {{\"query\":\"scene\"}}. Got: {json}" );
	}

	static TimeSpan Seconds( string value )
	{
		if ( !int.TryParse( value, out var seconds ) || seconds < 1 || seconds > 86400 )
			throw new ArgumentException( "Timeouts must be between 1 and 86400 seconds." );

		return TimeSpan.FromSeconds( seconds );
	}
}
