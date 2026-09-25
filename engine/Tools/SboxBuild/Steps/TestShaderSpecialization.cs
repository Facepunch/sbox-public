using Facepunch.Native;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>Standalone compiler/driver probe, built with the same native toolchain and pinned dependencies.</summary>
internal class TestShaderSpecialization
{
	internal ExitCode Run()
	{
		if ( !OperatingSystem.IsWindows() )
		{
			Log.Error( "The specialization experiment runner currently supports Windows." );
			return ExitCode.Failure;
		}

		var platform = NativePlatform.Host();
		NativePlatform.Current = platform;
		var options = new Options { Platform = platform.Name };
		var module = Registry.Load( options ).Single( x => x.Name == "shader_specialization_experiment" );
		platform.Generate( module, options );
		const string solution = "shader_specialization_experiment";
		Solution.Write( solution, [module] );
		if ( !platform.Build( solution ) ) return ExitCode.Failure;

		var output = Paths.Absolute( $"{module.Dir}/obj/experiment" );
		Directory.CreateDirectory( output );
		var environment = new Dictionary<string, string>
		{
			["PATH"] = Paths.Absolute( "thirdparty/slang/lib/win64" ) + ";" +
				Paths.Absolute( "../game/bin/win64" ) + ";" + Environment.GetEnvironmentVariable( "PATH" )
		};
		return Utility.RunProcess(
			Paths.Absolute( $"{Paths.DevToolsDir}/{module.OutputName}.exe" ),
			$"\"{Paths.Absolute( $"{module.Dir}/skin_slice.slang" )}\" \"{output}\"",
			environmentVariables: environment, timeoutMs: 300_000 ) ? ExitCode.Success : ExitCode.Failure;
	}
}
