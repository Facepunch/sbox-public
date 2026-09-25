using System.Diagnostics;
using System.Text;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>Full shader A/B, keeping experimental compiled output outside shipped content afterwards.</summary>
internal class TestComplexSpecialization
{
	internal ExitCode Run()
	{
		if ( !OperatingSystem.IsWindows() ) return ExitCode.Failure;
		var root = Directory.GetCurrentDirectory();
		var game = Path.Combine( root, "game" );
		var shader = Path.GetFullPath( Path.Combine( game, "core/shaders/complex.shader" ) );
		var compiled = shader + "_c";
		var output = Path.Combine( root, "src/shader_specialization_experiment/obj/complex" );
		Directory.CreateDirectory( output );
		var original = File.ReadAllBytes( compiled );
		var report = new StringBuilder();
		var environment = new Dictionary<string, string>
		{
			["FACEPUNCH_ENGINE"] = game,
			["SBOX_TEST_GRAPHICS"] = "1",
			["SBOX_COMPLEX_SPECIALIZATION_TEST_DIR"] = output,
			["VFX_SPIRVVALIDATE"] = "0"
		};
		try
		{
			long baselineBytes = 0;
			foreach ( var mode in new[] { "macro", "specialized" } )
			{
				bool compiledShader = false;
				bool specializedJobs = false;
				var timer = Stopwatch.StartNew();
				bool success = Utility.RunProcess( Path.Combine( game, "bin/managed/ShaderCompiler.exe" ),
					$"\"{shader}\" -f{(mode == "macro" ? " -vfx-no-specialization" : "")}", game,
					environment, timeoutMs: 900_000,
					onDataReceived: ( _, e ) =>
					{
						if ( e.Data is null ) return;
						lock ( report ) report.AppendLine( e.Data );
						compiledShader |= e.Data.Contains( "Compiled successfully in" );
						specializedJobs |= e.Data.Contains( "Complex specialization:" );
					} );
				timer.Stop();
				if ( !success || !compiledShader || (mode == "specialized" && !specializedJobs) )
				{
					Log.Error( "Shader compiler did not complete the requested compilation strategy. See results.txt." );
					return ExitCode.Failure;
				}
				var bytes = new FileInfo( compiled ).Length;
				if ( mode == "macro" ) baselineBytes = bytes;
				else if ( bytes >= baselineBytes )
				{
					Log.Error( $"Shared shader storage did not reduce size: {bytes} >= {baselineBytes}" );
					return ExitCode.Failure;
				}
				var summary = $"{mode}: process wall time {timer.Elapsed.TotalSeconds:F3}s, shader_c {bytes} bytes";
				Log.Info( summary );
				report.AppendLine( summary );
				File.Copy( compiled, Path.Combine( output, mode + ".shader_c" ), true );
				environment["SBOX_COMPLEX_SPECIALIZATION_CAPTURE"] = mode == "macro" ? "1" : "0";
				if ( !Utility.RunProcess( "dotnet",
					"test engine/Tests/Sandbox.Test.Integration/Sandbox.Test.Integration.csproj -c Release --no-build --filter FullyQualifiedName~ComplexSpecializationTest --logger \"console;verbosity=detailed\"",
					root, environment, timeoutMs: 600_000,
					onDataReceived: ( _, e ) => { if ( e.Data is not null ) lock ( report ) report.AppendLine( e.Data ); } ) ) return ExitCode.Failure;
			}
			return ExitCode.Success;
		}
		finally
		{
			File.WriteAllText( Path.Combine( output, "results.txt" ), report.ToString() );
			// Native test shutdown can briefly retain a mapped shader section after
			// dotnet test exits. Preserve diagnostics first, then allow it to release.
			for ( int attempt = 0; ; ++attempt )
			{
				try { File.WriteAllBytes( compiled, original ); break; }
				catch ( IOException ) when ( attempt < 20 ) { System.Threading.Thread.Sleep( 250 ); }
			}
		}
	}
}
