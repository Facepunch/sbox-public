using System;
using System.IO;
using System.Linq;

namespace Sandbox;

internal enum ProjectAutomationAction
{
	Help,
	Version,
	Compile,
	Error
}

internal sealed class ProjectAutomationInvocation
{
	public ProjectAutomationAction Action { get; init; }
	public bool Json { get; init; }
	public string ProjectPath { get; init; }
	public string Error { get; init; }
}

/// <summary>
/// Parses the deliberately small project automation grammar without initializing the engine.
/// </summary>
internal static class ProjectAutomationCommandLine
{
	internal const string HelpText = """
		Usage:
		  sbox-cli project compile --project <path> [--json]
		  sbox-cli --help
		  sbox-cli --version

		Commands:
		  project compile    Compile a project's Code and Editor C# with the s&box compiler.

		Options:
		  --project <path>   Path to the .sbproj file. Relative paths use the caller's working directory.
		  --json             Write one machine-readable JSON result to stdout.
		  --help             Show this help without loading a project or initializing the engine.
		  --version          Show the engine version without loading a project or initializing the engine.
		""";

	public static ProjectAutomationInvocation Parse( string[] args, string invocationDirectory )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( invocationDirectory );
		args ??= [];

		var json = args.Any( x => string.Equals( x, "--json", StringComparison.Ordinal ) );

		if ( args.Length == 1 && string.Equals( args[0], "--help", StringComparison.Ordinal ) )
		{
			return new ProjectAutomationInvocation { Action = ProjectAutomationAction.Help };
		}

		if ( args.Length == 1 && string.Equals( args[0], "--version", StringComparison.Ordinal ) )
		{
			return new ProjectAutomationInvocation { Action = ProjectAutomationAction.Version };
		}

		if ( args.Length < 2 ||
			!string.Equals( args[0], "project", StringComparison.Ordinal ) ||
			!string.Equals( args[1], "compile", StringComparison.Ordinal ) )
		{
			return Error( json, "Expected 'project compile', '--help', or '--version'." );
		}

		string projectPath = null;
		var sawJson = false;

		for ( var i = 2; i < args.Length; i++ )
		{
			switch ( args[i] )
			{
				case "--json":
					if ( sawJson )
						return Error( true, "Option '--json' may only be specified once." );

					sawJson = true;
					break;

				case "--project":
					if ( projectPath is not null )
						return Error( json, "Option '--project' may only be specified once." );

					if ( i + 1 >= args.Length ||
						args[i + 1].StartsWith( "--", StringComparison.Ordinal ) ||
						string.IsNullOrWhiteSpace( args[i + 1] ) )
					{
						return Error( json, "Option '--project' requires a path." );
					}

					projectPath = args[++i];
					break;

				default:
					return Error( json, $"Unknown argument '{args[i]}'." );
			}
		}

		if ( projectPath is null )
			return Error( json, "Option '--project' is required." );

		try
		{
			projectPath = Path.GetFullPath( projectPath, invocationDirectory );
		}
		catch ( Exception e ) when ( e is ArgumentException or NotSupportedException or PathTooLongException )
		{
			return Error( json, $"Option '--project' is not a valid path: {e.Message}" );
		}

		return new ProjectAutomationInvocation
		{
			Action = ProjectAutomationAction.Compile,
			Json = json,
			ProjectPath = projectPath
		};
	}

	private static ProjectAutomationInvocation Error( bool json, string message )
	{
		return new ProjectAutomationInvocation
		{
			Action = ProjectAutomationAction.Error,
			Json = json,
			Error = message
		};
	}
}
