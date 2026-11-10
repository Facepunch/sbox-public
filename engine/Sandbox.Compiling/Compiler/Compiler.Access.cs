using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Concurrent;

namespace Sandbox;

partial class Compiler
{
	/// <summary>
	/// Runs the code-access analyzer over every tree, including unchanged ones. Editing one tree can
	/// change what another binds to - dropping a type that shadowed a BCL type re-points calls to it -
	/// so analyzing only changed trees would let a prohibited API through on an incremental build.
	/// </summary>
	private void RunAccessWalker( CSharpCompilation compiler, CompilerOutput output )
	{
		var syntaxTrees = compiler.SyntaxTrees;
		if ( syntaxTrees.Length == 0 )
			return;

		ConcurrentBag<Diagnostic> diagnostics = [];
		var cache = new AccessCheckCache();

		// Largest first, so one big file isn't left running alone at the end
		var ordered = syntaxTrees.OrderByDescending( x => x.Length ).ToArray();

		// Handed out one at a time rather than in fixed ranges per thread
		System.Threading.Tasks.Parallel.ForEach( Partitioner.Create( ordered, loadBalance: true ), tree =>
		{
			var walker = new AccessWalker( compiler.GetSemanticModel( tree ), cache: cache );
			walker.Visit( tree.GetRoot() );

			foreach ( var diagnostic in walker.Diagnostics )
				diagnostics.Add( diagnostic );
		} );

		output.Diagnostics.AddRange( diagnostics
			.OrderBy( x => x.Location.SourceTree?.FilePath, StringComparer.OrdinalIgnoreCase )
			.ThenBy( x => x.Location.SourceSpan.Start ) );
	}
}
