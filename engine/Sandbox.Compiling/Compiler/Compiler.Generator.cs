using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sandbox;

partial class Compiler
{
	private Generator.Processor RunGenerators( CSharpCompilation compiler, List<SyntaxTree> syntaxTrees, CompilerOutput output )
	{
		var processor = new Generator.Processor()
		{
			AddonName = Name,
			AddonFileMap = output.Archive.FileMap,
			EnableCorelibPolyfills = _config.Whitelist
		};

		// The previous compile is also used to find added fields, so pass it even without fast hotloading

		processor.AllowILHotload = Group.AllowFastHotload;
		processor.Run( compiler, syntaxTrees, incrementalState.HasState ? incrementalState.Compilation : null );

		output.Diagnostics.AddRange( processor.Diagnostics );

		// Error within code generation itself
		if ( processor.Exception != null )
		{
			Log.Error( processor.Exception, "Error when generating code" );

			Sentry.SentrySdk.CaptureException( processor.Exception, scope =>
			{
				scope.SetTag( "group", "generator" );
			} );
		}

		return processor;
	}
}
