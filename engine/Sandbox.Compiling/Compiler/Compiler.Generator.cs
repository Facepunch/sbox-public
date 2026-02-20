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
			AddonFileMap = output.Archive.FileMap
		};

		if ( Group.AllowFastHotload && incrementalState.HasState )
		{
			processor.Run( compiler, syntaxTrees, incrementalState.Compilation, incrementalState.PreHotloadSyntaxTrees );
		}
		else
		{
			processor.Run( compiler, syntaxTrees );
		}

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
