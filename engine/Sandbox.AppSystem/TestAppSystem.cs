using System;
using System.IO;
using System.Runtime;

namespace Sandbox;

public class TestAppSystem : AppSystem
{
	public override void Init()
	{
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		var gameFolder = Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE", EnvironmentVariableTarget.Process );
		if ( gameFolder is null ) throw new Exception( "FACEPUNCH_ENGINE not found" );

		var nativeDllPath = Path.Combine( gameFolder, "bin", "win64" ) + Path.DirectorySeparatorChar;

		// Some tests would set FACEPUNCH_ENGINE to a temp dir that got deleted, make it really clear if that happened

		if ( !File.Exists( Path.Combine( nativeDllPath, "engine2.dll" ) ) )
		{
			throw new Exception( $"FACEPUNCH_ENGINE ({gameFolder}) doesn't contain bin/win64/engine2.dll" );
		}

		NetCore.InitializeInterop( gameFolder );

		//
		// Put our native dll path first so that when looking up native dlls we'll
		// always use the ones from our folder first
		//
		var path = Environment.GetEnvironmentVariable( "PATH" );
		path = $"{nativeDllPath};{path}";
		Environment.SetEnvironmentVariable( "PATH", path );

		CreateGame();

		var createInfo = new AppSystemCreateInfo()
		{
			Flags = AppSystemFlags.IsGameApp | AppSystemFlags.IsUnitTest
		};

		// Opt in to the real renderer for bitmap comparisons; ordinary tests stay headless. SBOX_TEST_GRAPHICS=1 draws on
		// Borealis.Gpu, as the game and the editor do; =source2 on Source 2's own device, for the tests of its native
		// rendering alone (the vfx specialization experiment: ComplexSpecialization, ComplexPerformance)
		var arguments = Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS_ARGS" );
		switch ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) )
		{
			case "1":
				// Draws wait for their shaders and models load at once, so what a test renders is all there the first time
				DrawOnBorealisGpu();
				Utility.CommandLine.CommandLineString = (Utility.CommandLine.CommandLineString ?? Environment.CommandLine) + " -wait-for-shaders -borealisgpu-syncload";
				Utility.CommandLine.Parse();
				InitGame( createInfo, "-borealisgpu -wait-for-shaders -borealisgpu-syncload -noassert -novr " + arguments );
				break;
			case "source2":
				InitGame( createInfo, "-vulkan -noassert -novr " + arguments );
				break;
			default:
				InitGame( createInfo, "" );
				break;
		}
	}
}
