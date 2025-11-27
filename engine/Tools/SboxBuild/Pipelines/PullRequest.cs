using Facepunch.Steps;
using static Facepunch.Constants;

namespace Facepunch.Pipelines;

internal class PullRequest
{
	public static Pipeline Create()
	{
		var builder = new PipelineBuilder( "Pull Request" );

		// Add format steps - allow them to fail
		// Linux formatting complains about line endings, it's probably enough if we run this windows only anyway
		if ( OperatingSystem.IsWindows() )
		{
			builder.AddStepGroup( "Format",
			[
				new Format( "Format Engine", Solutions.Engine, Format.Mode.Full, verifyOnly: true ),
				new Format( "Format Editor", Solutions.Toolbase, Format.Mode.Whitespace, verifyOnly: true ),
				new Format( "Format Menu", Solutions.Menu, Format.Mode.Whitespace, verifyOnly: true ),
				new Format( "Format Build Tools", Solutions.BuildTools, Format.Mode.Full, verifyOnly: true )
			], continueOnFailure: true );
		}

		// Add other steps
		builder.AddStepGroup( "CodeGen",
			[
				new Steps.InteropGen( "Interop Gen" ),
				new Steps.ShaderProc( "Shader Proc" )
			] );

		builder.AddStepGroup( "Native Build",
			[
				new GenerateSolutions( "Generate Solutions", BuildConfiguration.Retail ),
				new BuildNative( "Compile Native", BuildConfiguration.Retail, clean: true )
			] );

		var managedSteps = new List<Step>
		{
			new BuildManaged( "Compile Managed", clean: true )
		};
		if ( OperatingSystem.IsWindows() )
		{
			managedSteps.Add( new NvPatch( "NvPatch" ) );
		}
		builder.AddStepGroup( "Managed Build", managedSteps );

		// TODO idk if any of this works on linux yet 
		if ( OperatingSystem.IsWindows() )
		{
			// Build shaders is allowed to fail
			builder.AddStep( new BuildShaders( "Build Shaders" ), continueOnFailure: true );
			builder.AddStep( new BuildContent( "Build Content" ) );
			builder.AddStep( new Test( "Tests" ) );
			builder.AddStep( new BuildAddons( "Build Addons" ) );
		}

		return builder.Build();
	}
}
