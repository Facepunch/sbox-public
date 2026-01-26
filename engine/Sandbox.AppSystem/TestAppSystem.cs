using System;
using System.Runtime;

namespace Sandbox;

public class TestAppSystem : AppSystem
{
	public override void Init()
	{
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		var GameFolder = System.Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE", EnvironmentVariableTarget.Process );
		if ( GameFolder is null ) throw new Exception( "FACEPUNCH_ENGINE not found" );

		NetCore.InitializeInterop( GameFolder );

		var nativeDllPath = $"{GameFolder}\\bin\\win64\\";
		//
		// Put our native dll path first so that when looking up native dlls we'll
		// always use the ones from our folder first
		//
		var path = System.Environment.GetEnvironmentVariable( "PATH" );
		path = $"{nativeDllPath};{path}";
		System.Environment.SetEnvironmentVariable( "PATH", path );

		CreateGame();

		var createInfo = new AppSystemCreateInfo()
		{
			Flags = AppSystemFlags.IsGameApp | AppSystemFlags.IsUnitTest
		};

		InitGame( createInfo, "" );
	}
}
