using Microsoft.AspNetCore.Razor.Language;
using System.Collections.Generic;

namespace Sandbox.Razor;

public static class RazorProcessor
{
	/// <summary>
	/// Generate the c# code from an incoming razor file
	/// </summary>
	public static string GenerateFromSource( string text, string filename, string rootNamespace = null, bool useFolderNamespacing = true )
	{
		return GenerateDocument( text, filename, rootNamespace, useFolderNamespacing ).GeneratedCode;
	}

	/// <summary>
	/// Generates C# and retains Razor diagnostics for callers that need to report source errors.
	/// </summary>
	public static RazorCSharpDocument GenerateDocument( string text, string filename, string rootNamespace = null, bool useFolderNamespacing = true )
	{
		// Supply the inferred namespace as an import so source locations still refer to the original text.
		var imports = new List<RazorSourceDocument>();
		if ( useFolderNamespacing && !string.IsNullOrEmpty( rootNamespace ) && !text.Contains( "@namespace" ) )
		{
			imports.Add( RazorSourceDocument.Create( $"@namespace {GetNamespace( filename, rootNamespace )}", filename ) );
		}

		var engine = GetEngine();

		RazorSourceDocument source = RazorSourceDocument.Create( text, filename );
		RazorCodeDocument code = engine.Process( source, FileKinds.Component, imports, new List<TagHelperDescriptor>() );
		code.SetCodeGenerationOptions( RazorCodeGenerationOptions.Create( o => { } ) );

		return code.GetCSharpDocument();
	}

	private static string GetNamespace( string filename, string rootNamespace )
	{
		// Compute namespace from folder structure
		var directory = System.IO.Path.GetDirectoryName( filename );
		var computedNamespace = rootNamespace;

		if ( !string.IsNullOrEmpty( directory ) )
		{
			// Normalize path separators to forward slash
			directory = directory.Replace( '\\', '/' );

			// Split by path separator and filter out empty or invalid segments
			var folders = directory.Split( ['/'], System.StringSplitOptions.RemoveEmptyEntries );

			foreach ( var folder in folders )
			{
				// Only include folders that would make valid C# namespace identifiers
				if ( IsValidNamespaceSegment( folder ) )
				{
					computedNamespace += "." + folder;
				}
			}
		}

		return computedNamespace;
	}

	/// <summary>
	/// Check if a folder name is valid as a C# namespace segment
	/// </summary>
	static bool IsValidNamespaceSegment( string segment )
	{
		if ( string.IsNullOrWhiteSpace( segment ) ) return false;
		if ( segment.Contains( ":" ) ) return false;
		if ( segment == "." || segment == ".." ) return false;
		if ( segment.Contains( " " ) || segment.Contains( "-" ) ) return false;
		if ( char.IsDigit( segment[0] ) ) return false;

		return true;
	}

	static RazorProjectEngine GetEngine()
	{
		var configuration = RazorConfiguration.Default;
		var razorProjectEngine = RazorProjectEngine.Create( configuration, RazorProjectFileSystem.Create( "." ) );

		return razorProjectEngine;
	}

}
