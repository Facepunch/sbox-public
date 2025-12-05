namespace Facepunch;

/// <summary>
/// Linux platform implementation
/// </summary>
internal class LinuxPlatform : Platform
{
	protected override string PlatformBaseName => "linuxsteamrt"; // Fucking make this just "linux" when port is more mature -> Yes do it :D

	public override bool CompileSolution( string solutionName, bool forceRebuild = false )
	{
		if ( solutionName.Contains( "developer_all" ) )
		{
			Log.Info( "Building NativeAOT Engine..." );
			return Utility.RunProcess( "dotnet", "publish src/Sandbox.Engine.Emulation/Sandbox.Engine.Emulation.csproj -c Release -r linux-x64 /p:NativeLib=Shared /p:SelfContained=true -o game/bin/linuxsteamrt64", "." );
		}

		return Utility.RunProcess( "make", $"-f {solutionName}.mak SHELL=/bin/bash", "src" );
	}
}
