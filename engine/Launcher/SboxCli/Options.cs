using System;
using System.IO;

namespace Sandbox.Cli;

/// <summary>
/// Arguments for one persistent stdio connection, or a one-shot discovery command.
/// </summary>
internal sealed class Options
{
	internal string Mode { get; private set; }
	internal string Target { get; private set; }
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

			switch ( argument )
			{
				case "--project":
				case "--game":
				case "--connect":
				case "--list":
				case "--help":
					if ( options.Mode is not null )
						throw new ArgumentException( "Choose exactly one of --project, --game, --connect, --list or --help." );

					options.Mode = argument;
					if ( argument is not ("--list" or "--help") )
						options.Target = Value();
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

		options.Mode ??= "--help";

		if ( options.Mode == "--connect" && (!int.TryParse( options.Target, out var pid ) || pid <= 0) )
			throw new ArgumentException( "--connect requires a positive process ID. Use --list to discover instances." );

		if ( options.Mode == "--project" || options.Mode == "--game" &&
			(Directory.Exists( options.Target ) || Path.IsPathRooted( options.Target )
			|| options.Target.Contains( '/' ) || options.Target.Contains( '\\' )
			|| options.Target.EndsWith( ".sbproj", StringComparison.OrdinalIgnoreCase )) )
		{
			options.Target = Path.GetFullPath( options.Target );
			if ( Directory.Exists( options.Target ) )
				options.Target = Path.Combine( options.Target, ".sbproj" );

			if ( !File.Exists( options.Target ) )
				throw new FileNotFoundException( "Project file does not exist.", options.Target );
		}

		return options;
	}

	static TimeSpan Seconds( string value )
	{
		if ( !int.TryParse( value, out var seconds ) || seconds < 1 || seconds > 86400 )
			throw new ArgumentException( "Timeouts must be between 1 and 86400 seconds." );

		return TimeSpan.FromSeconds( seconds );
	}
}
