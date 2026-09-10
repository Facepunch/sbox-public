using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sandbox.Generator;

[Generator]
public class SourceGenerator : IIncrementalGenerator
{
	public void Initialize( IncrementalGeneratorInitializationContext context )
	{
		// The codegen processor is the engine compile pipeline's - a project registering this
		// assembly as an analyzer (for RazorGenerator) opts into it separately
		var enabled = context.AnalyzerConfigOptionsProvider.Select( static ( options, _ ) =>
			options.GlobalOptions.TryGetValue( "build_property.SandboxCodegen", out var value ) && value == "true" );

		context.RegisterSourceOutput( context.CompilationProvider.Combine( enabled ), static ( spc, pair ) =>
		{
			if ( !pair.Right ) return;

			// Razor files are now handled by the Razor SDK in IDE scenarios
			// and by Compiler.Razor.cs during engine compilation

			var processor = new Processor();
			processor.Context = spc;

			processor.Run( (CSharpCompilation)pair.Left );
		} );
	}
}
