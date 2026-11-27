using System;
using System.IO;
using System.Runtime;

namespace Sandbox;

public class TestAppSystem : AppSystem
{
	public override void Init()
	{
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		var gameFolder = GameFolder();

		// bin/win64/engine2.dll, bin/linuxsteamrt64/libengine2.so, ...
		var nativeDllPath = Path.GetFullPath( Path.Combine( gameFolder, NetCore.NativeDllPath ) );
		var engine2 = Path.Combine( nativeDllPath, Interop.GetNativeLibraryName( "engine2" ) );

		// Some tests would set FACEPUNCH_ENGINE to a temp dir that got deleted, make it really clear if that happened

		if ( !File.Exists( engine2 ) )
		{
			throw new Exception( $"FACEPUNCH_ENGINE ({gameFolder}) doesn't contain {Path.GetRelativePath( gameFolder, engine2 )}" );
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
		// Borealis.Gpu, as the game and the editor do
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
			default:
				InitGame( createInfo, "" );
				break;
		}
	}

	/// <summary>
	/// The game folder whose native engine the tests load: the game folder of the checkout the tests were built in, when it has
	/// a built native engine, else FACEPUNCH_ENGINE. FACEPUNCH_ENGINE is a user-wide variable the launcher of whichever
	/// checkout ran last writes, and a shell keeps the value it started with; another build's native dlls don't match this
	/// build's interop and crash the test host in NativeInterop.Initialize. The process's FACEPUNCH_ENGINE is set to the
	/// result, so tests that read it agree.
	/// </summary>
	public static string GameFolder()
	{
		var set = Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE", EnvironmentVariableTarget.Process );
		var gameFolder = CheckoutGameFolder( AppContext.BaseDirectory ) ?? set;
		if ( gameFolder is null ) throw new Exception( "FACEPUNCH_ENGINE not found" );

		if ( set is null || !SamePath( set, gameFolder ) )
		{
			if ( set is not null ) Console.WriteLine( $"FACEPUNCH_ENGINE is {set}, another build's folder: loading this checkout's {gameFolder}" );
			Environment.SetEnvironmentVariable( "FACEPUNCH_ENGINE", gameFolder );
		}

		return gameFolder;
	}

	/// <summary>The game folder of the checkout <paramref name="directory"/> is in, when it has a built native engine; else null.</summary>
	public static string CheckoutGameFolder( string directory )
	{
		for ( var dir = new DirectoryInfo( directory ); dir is not null; dir = dir.Parent )
		{
			var game = Path.Combine( dir.FullName, "game" );
			if ( File.Exists( Path.Combine( game, NetCore.NativeDllPath, Interop.GetNativeLibraryName( "engine2" ) ) ) ) return game;
		}

		return null;
	}

	static bool SamePath( string a, string b )
		=> string.Equals( Path.TrimEndingDirectorySeparator( Path.GetFullPath( a ) ), Path.TrimEndingDirectorySeparator( Path.GetFullPath( b ) ),
			OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal );
}
