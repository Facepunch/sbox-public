using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Sandbox.Generator;

/// <summary>
/// A sync object to allow workers to exchange data abstractly. Should lock Worker.Sync when accessing.
/// </summary>
internal class Sync
{
	HashSet<string> tags = new HashSet<string>();

	/// <summary>
	/// Add a tag. Return true if added, return false if not. This is used to let other
	/// workers know that we're adding some code, so they don't also try to add the same code.
	/// ie - adding attributes to classes can fuck up if they have a partial spread over multiple
	/// files, because we'll try to add the attribute for each file.
	/// </summary>
	public bool AddTag( string tag )
	{
		if ( tags.Contains( tag ) )
			return false;

		tags.Add( tag );
		return true;
	}
}

public class Processor
{
	public static Func<string, string> DefaultPackageAssetResolver;

	/// <summary>
	/// Generated file that will get stuff like global usings and assembly attributes.
	/// </summary>
	public const string CompilerExtraPath = "/.obj/__compiler_extra.cs";

	public string AddonName { get; set; } = "AddonName";

	public Dictionary<string, string> AddonFileMap { get; set; } = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
	public CSharpCompilation Compilation { get; set; }
	public CSharpCompilation LastSuccessfulCompilation { get; set; }
	public List<SyntaxTree> SyntaxTrees { get; set; }
	public ImmutableArray<SyntaxTree> BeforeILHotloadProcessingTrees { get; set; }
	public Exception Exception { get; internal set; }
	public SourceProductionContext? Context { get; set; }

	public List<Diagnostic> Diagnostics = new List<Diagnostic>();
	public bool ILHotloadSupported { get; set; }

	/// <summary>
	/// A function that will take a package name and return the path to the asset
	/// </summary>
	public Func<string, string> PackageAssetResolver { get; set; } = DefaultPackageAssetResolver;

	public void AddTrees( IEnumerable<SyntaxTree> trees )
	{
		if ( Context == null )
		{
			Compilation = Compilation.AddSyntaxTrees( trees.ToArray() );
		}

		foreach ( var tree in trees )
		{
			Context?.AddSource( tree.FilePath, SourceText.From( tree.ToString(), Encoding.UTF8 ) );
		}
	}

	/// <summary>
	/// Can be called manually
	/// </summary>
	public void Run( CSharpCompilation compilation,
		List<SyntaxTree> syntaxTrees = null,
		CSharpCompilation lastSuccessfulCompilation = null,
		ImmutableArray<SyntaxTree> lastBeforeIlHotloadProcessingTrees = default )
	{
		Compilation = compilation;
		LastSuccessfulCompilation = lastSuccessfulCompilation;
		BeforeILHotloadProcessingTrees = lastBeforeIlHotloadProcessingTrees;

		syntaxTrees ??= compilation.SyntaxTrees.ToList();
		SyntaxTrees = syntaxTrees;

		try
		{
			if ( syntaxTrees.Any() )
			{
				var sync = new Sync();
				ConcurrentBag<Worker> workers = new();
				ConcurrentBag<Exception> exceptions = new();

				//
				// Run all the processers in tasks so it's super fast
				//
				var result = System.Threading.Tasks.Parallel.ForEach( syntaxTrees, tree =>
				{
					try
					{
						var w = Worker.Process( Compilation, tree, AddonFileMap, Context == null, sync, this );
						workers.Add( w );
					}
					catch ( System.Exception e )
					{
						exceptions.Add( e );
					}

				} );

				// any exceptions?
				if ( exceptions.Any() )
				{
					throw exceptions.First();
				}

				//
				// Sort the workers, so added code is in a deterministic order
				//
				var sortedWorkers = workers.OrderBy( x => x.TreeInput.FilePath ).ToArray();

				//
				// Process the results
				//
				foreach ( var worker in sortedWorkers )
				{
					// Don't need to do this if just using Source Generator
					if ( Context == null )
					{
						ReplaceSyntaxTree( worker.TreeInput, CSharpSyntaxTree.Create( worker.OutputNode, worker.TreeInput.Options as CSharpParseOptions, worker.TreeInput.FilePath, worker.TreeInput.Encoding ) );

						// Copy each worker's diagnostics so they're accessible outside of Sandbox.Generator
						Diagnostics.AddRange( worker.Diagnostics );
					}
					else
					{
						foreach ( var diag in worker.Diagnostics )
						{
							Context.Value.ReportDiagnostic( diag );
						}
					}
				}

				//
				// If trees were added, add them to the source
				//
				var trees = sortedWorkers.SelectMany( x => x.AddedTrees ).ToList();

				//
				// If we added loose code
				//
				var extraCode = string.Join( "\n", sortedWorkers.Where( x => !string.IsNullOrEmpty( x.AddedCode ) ).Select( x => x.AddedCode ) );
				if ( !string.IsNullOrWhiteSpace( extraCode ) )
				{
					trees.Add( CSharpSyntaxTree.ParseText( extraCode, path: "_gen__AddedCode.cs", encoding: System.Text.Encoding.UTF8 ) );
				}

				//
				// Write all the new trees
				//
				if ( trees.Count() > 0 )
				{
					AddTrees( trees );
				}
			}

			var beforeIlHotloadTrees = Compilation.SyntaxTrees;

			if ( Context == null )
			{
				ILHotloadProcessor.Process( this );
			}

			BeforeILHotloadProcessingTrees = beforeIlHotloadTrees;
		}
		catch ( System.Exception e )
		{
			Exception = e;
			var desc = new DiagnosticDescriptor( "SB5000", "Generator Crash", $"Code Generator Crashed {Exception.StackTrace.Trim( '\n', '\r', ' ', '\t' ).Replace( "\n", "" ).Replace( "\r", "" )} - {e.Message}", "generator", DiagnosticSeverity.Error, true );

			Context?.ReportDiagnostic( Diagnostic.Create( desc, null ) );
		}
	}

	public void ReplaceSyntaxTree( SyntaxTree oldTree, SyntaxTree newTree )
	{
		Compilation = Compilation.ReplaceSyntaxTree( oldTree, newTree );

		// ensure the list of syntax trees is updated to reflect what's actually in the compilation
		if ( SyntaxTrees.Remove( oldTree ) )
			SyntaxTrees.Add( newTree );
	}
}
