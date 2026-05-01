using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Concurrent;

namespace Sandbox;

partial class Compiler
{
	/// <summary>
	/// Process Razor files from the code archive and generate C# syntax trees
	/// </summary>
	private List<SyntaxTree> ProcessRazorFiles( CodeArchive archive, CompilerOutput output )
	{
		var razorFiles = archive.AdditionalFiles
			.Where( x => x.LocalPath.EndsWith( ".razor", System.StringComparison.OrdinalIgnoreCase ) )
			.ToList();

		if ( razorFiles.Count == 0 )
			return [];

		var oldTrees = incrementalState.HasState ? incrementalState.SyntaxTrees
						.DistinctBy( x => x.FilePath )
						.ToDictionary( x => x.FilePath, x => x ) : null;

		var trees = new ConcurrentBag<SyntaxTree>();
		var diagnostics = new ConcurrentBag<Diagnostic>();

		Parallel.ForEach( razorFiles, file =>
		{
			var hash = file.LocalPath.FastHash();
			string filenameOnly = System.IO.Path.GetFileName( file.LocalPath );

			if ( file.Text is null )
			{
				var desc = new DiagnosticDescriptor( "SB6000", "Razor Error", $"Error reading {file.LocalPath}", "razor", DiagnosticSeverity.Error, true );
				diagnostics.Add( Diagnostic.Create( desc, null ) );
				return;
			}

			try
			{
				// Create the generated file path using the same naming convention
				string filePath = $"_gen_{filenameOnly}_{hash:x}.cs";

				SyntaxTree tree = null;
				if ( oldTrees?.TryGetValue( filePath, out tree ) == true
					&& incrementalState.FileHashMap.TryGetValue( file.LocalPath, out var oldHash )
					&& oldHash == archive.FileHashMap[file.LocalPath] )
				{
					// file is unchanged, just reuse existing tree
				}
				else
				{
					// Use the existing RazorProcessor to generate C# code from the Razor file
					// Pass the root namespace so Razor can auto-generate @namespace directives from folder structure
					var generatedCode = Sandbox.Razor.RazorProcessor.GenerateFromSource( file.Text, file.LocalPath, archive.Configuration.RootNamespace, !archive.Version_UsesOldRazorNamespaces );

					var sourceText = SourceText.From( generatedCode, Encoding.UTF8 );
					if ( tree is not null )
					{
						tree = tree.WithChangedText( sourceText );
					}
					else
					{
						tree = CSharpSyntaxTree.ParseText( sourceText, path: filePath );
					}
				}

				// Check for duplicates
				if ( trees.Any( x => x.FilePath == filePath ) )
				{
					var desc = new DiagnosticDescriptor( "SB6001", "Razor Error", $"Duplicate Razor Component: {file.LocalPath}", "razor", DiagnosticSeverity.Error, true );
					diagnostics.Add( Diagnostic.Create( desc, null ) );
					return;
				}

				trees.Add( tree );

				// Map the generated file path to the original .razor file for debugging support
				lock ( archive.FileMap )
				{
					archive.FileMap[filePath] = file.LocalPath;

					// If we had a relative version of this path stored, then use that instead
					if ( archive.FileMap.TryGetValue( file.LocalPath, out var relativePath ) )
						archive.FileMap[filePath] = relativePath;
				}
			}
			catch ( System.Exception ex )
			{
				var desc = new DiagnosticDescriptor( "SB6002", "Razor Error", $"Error processing {file.LocalPath}: {ex.Message}", "razor", DiagnosticSeverity.Error, true );
				diagnostics.Add( Diagnostic.Create( desc, null ) );
			}
		} );

		// Add any diagnostics to the output
		if ( diagnostics.Any() )
		{
			output.Diagnostics.AddRange( diagnostics );
		}

		return trees.OrderBy( x => x.FilePath ).ToList();
	}
}
