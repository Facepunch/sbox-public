using System.Diagnostics;

namespace Facepunch;

/// <summary>
/// Windows platform implementation
/// </summary>
internal class WindowsPlatform : Platform
{
	protected override string PlatformBaseName => "win";

	public override bool CompileSolution( string solutionName, bool forceRebuild = false )
	{
		string vsDevCmdPath = FindVsDevCmdPath();
		if ( string.IsNullOrEmpty( vsDevCmdPath ) )
		{
			Log.Error( "Could not find Visual Studio Developer Command Prompt. Ensure Visual Studio is installed and accessible." );
			return false;
		}

		string buildTarget = forceRebuild ? "/t:Rebuild" : "/t:Build";
		return RunCommandWithVsEnv( vsDevCmdPath, $"msbuild {solutionName}.slnx {buildTarget} /p:Configuration=Release /p:Platform=x64 /m /v:minimal /clp:Summary" );
	}

	/// <summary>
	/// Finds the Visual Studio Developer Command Prompt path
	/// </summary>
	/// <returns>Path to VsDevCmd.bat, or empty string if not found</returns>
	private string FindVsDevCmdPath()
	{
		// Find Visual Studio installation path
		string vsPath = string.Empty;
		using ( Process vsWhere = new Process() )
		{
			vsWhere.StartInfo.FileName = "src\\devtools\\bin\\win64\\vswhere";
			vsWhere.StartInfo.Arguments = "-latest -prerelease -products Microsoft.VisualStudio.Product.Enterprise Microsoft.VisualStudio.Product.Professional Microsoft.VisualStudio.Product.Community Microsoft.VisualStudio.Product.BuildTools -property installationPath";
			vsWhere.StartInfo.UseShellExecute = false;
			vsWhere.StartInfo.RedirectStandardOutput = true;
			vsWhere.StartInfo.CreateNoWindow = true;
			vsWhere.Start();
			vsPath = vsWhere.StandardOutput.ReadToEnd().Trim();
			vsWhere.WaitForExit();
		}

		string vsDevCmdPath = Path.Combine( vsPath, "Common7\\Tools\\VsDevCmd.bat" );

		if ( !File.Exists( vsDevCmdPath ) )
		{
			Log.Error( $"Could not find VsDevCmd.bat at {vsDevCmdPath}" );
			return string.Empty;
		}

		return vsDevCmdPath;
	}

	/// <summary>
	/// Runs a command with Visual Studio environment variables set
	/// </summary>
	/// <param name="vsDevCmdPath">Path to VsDevCmd.bat</param>
	/// <param name="command">Command to execute</param>
	/// <returns>True if command succeeded, false otherwise</returns>
	private bool RunCommandWithVsEnv( string vsDevCmdPath, string command )
	{
		var arguments = $"/c \"call \"{vsDevCmdPath}\" -no_logo && cd src && {command}\"";
		return Utility.RunProcess( "cmd.exe", arguments, null );
	}
}
