using Microsoft.CodeAnalysis;
using Sandbox;
using System;
using System.Linq;

namespace Editor.Mcp;

[McpToolset( "editor" )]
public static partial class EditorTools
{
	/// <summary>
	/// Run a console command - anything the editor console accepts, including setting convars.
	/// Output, if any, lands in the console - read_console shows it.
	/// </summary>
	/// <param name="command">The command line to run, e.g. 'stat fps' or 'sv_cheats 1'.</param>
	[McpTool( "console_command" )]
	public static object ConsoleCommand( string command )
	{
		if ( string.IsNullOrWhiteSpace( command ) )
			throw new Exception( "Give a command to run" );

		ConsoleSystem.Run( command );

		return "Ran. Output, if any, is in the console - read_console shows it.";
	}

	/// <summary>
	/// The live state of the project's code compilers - is anything building, did each compiler's
	/// last build succeed, and every error and warning with file and line. This is how you check
	/// whether a code edit hotloaded cleanly.
	/// </summary>
	[McpTool.ReadOnly( "compile_status" )]
	public static CompileState CompileStatus()
	{
		var group = Sandbox.Project.CompileGroup
			?? throw new Exception( "No compile group - is a project loaded?" );

		return new CompileState
		{
			IsBuilding = group.IsBuilding,
			Compilers = group.Compilers.Select( x =>
			{
				var diagnostics = x.Diagnostics;

				return new CompilerState
				{
					Name = x.Name,
					IsBuilding = x.IsBuilding,
					NeedsBuild = x.NeedsBuild,
					Success = x.Output?.Successful,
					Errors = diagnostics.Count( d => d.Severity == DiagnosticSeverity.Error ),
					Warnings = diagnostics.Count( d => d.Severity == DiagnosticSeverity.Warning ),
					Diagnostics = diagnostics
						.Where( d => d.Severity >= DiagnosticSeverity.Warning )
						.OrderByDescending( d => d.Severity )
						.Take( 50 )
						.Select( d =>
						{
							var span = d.Location.GetLineSpan();

							return new CompileDiagnostic
							{
								Severity = d.Severity.ToString(),
								Id = d.Id,
								Message = d.GetMessage(),
								File = span.Path,
								Line = span.StartLinePosition.Line + 1
							};
						} ).ToArray()
				};
			} ).ToArray()
		};
	}

	/// <summary>
	/// The live state of every compiler in the project's compile group.
	/// </summary>
	public class CompileState
	{
		/// <summary>Whether anything is building right now.</summary>
		public bool IsBuilding { get; set; }

		/// <summary>Every compiler, each with its last build's outcome.</summary>
		public CompilerState[] Compilers { get; set; }
	}

	/// <summary>
	/// One compiler and how its last build went.
	/// </summary>
	public class CompilerState
	{
		/// <summary>The project this compiler builds, like 'base' or 'facepunch.sandbox'.</summary>
		public string Name { get; set; }

		public bool IsBuilding { get; set; }

		public bool NeedsBuild { get; set; }

		/// <summary>Whether the last build succeeded. Null when it hasn't built yet.</summary>
		public bool? Success { get; set; }

		public int Errors { get; set; }

		public int Warnings { get; set; }

		/// <summary>The last build's errors and warnings, errors first, capped at 50.</summary>
		public CompileDiagnostic[] Diagnostics { get; set; }
	}

	/// <summary>
	/// One compiler error or warning and where it happened.
	/// </summary>
	public class CompileDiagnostic
	{
		public string Severity { get; set; }

		/// <summary>The diagnostic id, like 'CS0246'.</summary>
		public string Id { get; set; }

		public string Message { get; set; }

		public string File { get; set; }

		public int Line { get; set; }
	}
}
