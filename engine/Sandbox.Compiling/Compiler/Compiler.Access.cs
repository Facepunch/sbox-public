using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Concurrent;

namespace Sandbox;

partial class Compiler
{
	private void RunAccessWalker( CSharpCompilation compiler, IReadOnlyCollection<SyntaxTree> syntaxTrees, CompilerOutput output )
	{
		if ( syntaxTrees.Count == 0 )
			return;

		ConcurrentBag<Diagnostic> diagnostics = [];

		System.Threading.Tasks.Parallel.ForEach( syntaxTrees, tree =>
		{
			var semanticModel = compiler.GetSemanticModel( tree );
			var walker = new AccessWalker( semanticModel, checkAllowList: tree.FilePath != Generator.Processor.CompilerExtraPath );
			walker.Visit( tree.GetRoot() );

			foreach ( var diagnostic in walker.Diagnostics )
				diagnostics.Add( diagnostic );
		} );

		output.Diagnostics.AddRange( diagnostics
			.OrderBy( x => x.Location.SourceTree?.FilePath, StringComparer.OrdinalIgnoreCase )
			.ThenBy( x => x.Location.SourceSpan.Start ) );
	}
}
