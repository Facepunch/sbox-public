using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json.Serialization;

namespace Sandbox;


partial class Compiler
{
	public enum ReleaseMode
	{
		Debug,
		Release
	}

	public struct Configuration
	{
		public Configuration()
		{
			Clean();
		}

		public string RootNamespace { get; set; }

		[Title( "Symbols" ), Description( "A list of pre-processor symbols to define when compiling, separated with semi-colons." )]
		public string DefineConstants { get; set; } = "SANDBOX";
		public string NoWarn { get; set; } = "1701;1702;1591;";
		public string WarningsAsErrors { get; set; } = "";
		public bool TreatWarningsAsErrors { get; set; }
		public bool Nullables { get; set; }

		/// <summary>
		/// If true, we will be using the whitelist system. If false then this package won't
		/// be "sandboxed", so won't be able to be published on the platform.
		/// </summary>
		[JsonIgnore]
		public bool Whitelist { get; set; } = true;

		/// <summary>
		/// If true, we'll compile with /unsafe. This means that the package won't be able to
		/// be published on the platform.
		/// </summary>
		[JsonIgnore]
		public bool Unsafe { get; set; } = false;

		/// <summary>
		/// The current release mode. This only matters during local development. 
		/// Published games are always built in release mode, where optimizations are enabled and debugging is limited (breakpoints, sequence points, and locals may be unavailable).
		/// </summary>
		public ReleaseMode ReleaseMode { get; set; }

		/// <summary>
		/// References to non-package assemblies, by assembly name.
		/// </summary>
		public List<string> AssemblyReferences { get; set; }

		/// <summary>
		/// Maps file patterns to preprocessor directives they should be wrapped in
		/// </summary>
		public Dictionary<string, string> ReplacementDirectives { get; set; }

		/// <summary>
		/// Strips disabled text trivia from the syntax tree. This is stuff like `#if false` blocks that are not compiled.
		/// </summary>
		internal bool StripDisabledTextTrivia { get; set; } = false;

		/// <summary>
		/// Folders to ignore when walking the tree
		/// </summary>
		public HashSet<string> IgnoreFolders { get; set; } = new HashSet<string>();

		bool IsPermittedAssemblyReference( string assemblyName )
		{
			if ( !Whitelist )
				return true;

			return assemblyName.StartsWith( "Sandbox.", StringComparison.OrdinalIgnoreCase )
				|| assemblyName.StartsWith( "Facepunch.", StringComparison.OrdinalIgnoreCase )
				|| assemblyName == "Microsoft.AspNetCore.Components";
		}

		/// <summary>
		/// Each unique element of <see cref="AssemblyReferences"/>
		/// </summary>
		public IReadOnlySet<string> DistinctAssemblyReferences => new HashSet<string>( AssemblyReferences.Where( IsPermittedAssemblyReference ), StringComparer.OrdinalIgnoreCase );

		public void Clean()
		{
			if ( string.IsNullOrWhiteSpace( RootNamespace ) )
				RootNamespace = "Sandbox";

			AssemblyReferences ??= new();
			IgnoreFolders ??= new();
			ReplacementDirectives ??= new()
			{
				{ ".Server.cs", "SERVER" } // Include .Server.cs replacements by default
			};
		}

		internal Dictionary<string, ReportDiagnostic> GetReportDiagnostics()
		{
			var diags = NoWarn.Split( ";", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
			var diagnostics = diags.ToDictionary( x => $"CS{x}", y => ReportDiagnostic.Suppress );

			// alex: I had support for the "nullable" shorthand in here but it doesn't appear to work with CSharpCompilationOptions
			//		 (see https://github.com/dotnet/roslyn/issues/52414) so I've removed it for now.
			var warns = WarningsAsErrors.Split( ";", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries );
			foreach ( var warn in warns )
				diagnostics.Add( $"CS{warn}", ReportDiagnostic.Error );

			return diagnostics;
		}

		/// <summary>
		/// Fetches the preprocessor symbols, which might've changed based on criteria
		/// </summary>
		/// <returns></returns>
		public HashSet<string> GetPreprocessorSymbols()
		{
			var symbols = DefineConstants.Split( ";", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
							.ToHashSet();

			if ( ReleaseMode == ReleaseMode.Debug )
			{
				symbols.Add( "DEBUG" );
			}
			else
			{
				symbols.Remove( "DEBUG" );
			}

			return symbols;
		}

		/// <summary>
		/// Returns the CSharpParseOptions for this configuration, which includes the preprocessor symbols defined in <see cref="DefineConstants"/>.
		/// </summary>
		/// <returns></returns>
		public CSharpParseOptions GetParseOptions()
		{
			return CSharpParseOptions.Default
				.WithLanguageVersion( LanguageVersion.CSharp14 )
				.WithPreprocessorSymbols( GetPreprocessorSymbols() );
		}
	}
}
