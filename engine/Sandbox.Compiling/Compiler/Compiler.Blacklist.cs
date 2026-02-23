using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Concurrent;

namespace Sandbox;

partial class Compiler
{
	private void RunBlacklistWalker( CSharpCompilation compiler, IEnumerable<SyntaxTree> syntaxTrees, CompilerOutput output )
	{
		if ( !syntaxTrees.Any() )
		{
			return;
		}

		ConcurrentBag<Diagnostic> diagnostics = new();

		var result = System.Threading.Tasks.Parallel.ForEach( syntaxTrees, tree =>
		{
			var semanticModel = compiler.GetSemanticModel( tree );

			var walker = new BlacklistCodeWalker( semanticModel );
			walker.Visit( tree.GetRoot() );

			walker.Diagnostics.ForEach( diagnostics.Add );
		} );

		output.Diagnostics.AddRange( diagnostics );
	}
}
