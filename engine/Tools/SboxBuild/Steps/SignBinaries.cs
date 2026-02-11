using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class SignBinaries() : Step( "SignBinaries" )
{
	// All the stuff we compile directly, no third party
	// (We compile Qt ourselves, so we sign that too)
	private static readonly string[] Win64Binaries =
	[
		"animationsystem.dll",
		"assetsystem.dll",
		"bakedlodbuilder.dll",
		"contentbuilder.exe",
		"dmxconvert.exe",
		"engine2.dll",
		"fbx2dmx.exe",
		"filesystem_stdio.dll",
		"helpsystem.dll",
		"localize.dll",
		"materialsystem2.dll",
		"meshsystem.dll",
		"modeldoc_utils.dll",
		"obj2dmx.exe",
		"physicsbuilder.dll",
		"propertyeditor.dll",
		"Qt5Concurrent.dll",
		"Qt5Core.dll",
		"Qt5Gui.dll",
		"Qt5Widgets.dll",
		"rendersystemempty.dll",
		"rendersystemvulkan.dll",
		"resourcecompiler.dll",
		"resourcecompiler.exe",
		"schemasystem.dll",
		"tier0.dll",
		"tier0_s64.dll",
		"toolframework2.dll",
		"toolscenenodes.dll",
		"vfx_vulkan.dll",
		"visbuilder.dll",
		"vpk.exe",
		"vrad2.exe",
		"vrad3.exe",
		"qt5_plugins/imageformats/qgif.dll",
		"qt5_plugins/imageformats/qico.dll",
		"qt5_plugins/imageformats/qjpeg.dll",
		"qt5_plugins/imageformats/qtga.dll",
		"qt5_plugins/imageformats/qwbmp.dll",
		"qt5_plugins/platforms/qwindows.dll",
		"tools/animgraph_editor.dll",
		"tools/hammer.dll",
		"tools/met.dll",
		"tools/modeldoc_editor.dll"
	];

	protected override ExitCode RunInternal()
	{
		string rootDir = Directory.GetCurrentDirectory();

		var vaultUrl = Environment.GetEnvironmentVariable( "CODESIGN_AZURE_KEYVAULT_URL" );
		var clientId = Environment.GetEnvironmentVariable( "CODESIGN_AZURE_CLIENT_ID" );
		var clientSecret = Environment.GetEnvironmentVariable( "CODESIGN_AZURE_CLIENT_SECRET" );
		var tenantId = Environment.GetEnvironmentVariable( "CODESIGN_AZURE_TENANT_ID" );

		if ( string.IsNullOrEmpty( vaultUrl ) || string.IsNullOrEmpty( clientId ) ||
			 string.IsNullOrEmpty( clientSecret ) || string.IsNullOrEmpty( tenantId ) )
		{
			Log.Error( "One or more Azure signing environment variables are missing (CODESIGN_AZURE_KEYVAULT_URL, CODESIGN_AZURE_CLIENT_ID, CODESIGN_AZURE_CLIENT_SECRET, CODESIGN_AZURE_TENANT_ID)" );
			return ExitCode.Failure;
		}

		var filesToSign = CollectFilesToSign( rootDir );

		if ( filesToSign.Count == 0 )
		{
			Log.Warning( "No files found to sign." );
			return ExitCode.Success;
		}

		foreach ( var file in filesToSign )
		{
			Log.Info( $"Signing {Path.GetFileName( file )}" );

			bool success = Utility.RunProcess(
				"AzureSignTool",
				$"sign -kvu \"{vaultUrl}\" -kvi \"{clientId}\" -kvs \"{clientSecret}\" -kvt \"{tenantId}\" -kvc FPCodeSign -tr http://timestamp.digicert.com \"{file}\"",
				rootDir
			);

			if ( !success )
			{
				Log.Error( $"Failed to sign {file}" );
			}
		}

		Log.Info( $"Successfully signed {filesToSign.Count} files." );
		return ExitCode.Success;
	}

	private static List<string> CollectFilesToSign( string rootDir )
	{
		var files = new List<string>();

		// game/bin/win64 - only our compiled binaries
		string win64Path = Path.Combine( rootDir, "game", "bin", "win64" );
		foreach ( var binary in Win64Binaries )
		{
			files.Add( Path.Combine( win64Path, binary ) );
		}

		// game folder - sbox.exe, sbox.dll, etc.
		files.AddRange( Directory.EnumerateFiles( Path.Combine( rootDir, "game" ), "*.exe" ) );
		files.AddRange( Directory.EnumerateFiles( Path.Combine( rootDir, "game" ), "*.dll" ) );

		// managed assemblies that are ours
		files.AddRange( Directory.EnumerateFiles( Path.Combine( rootDir, "game", "bin", "managed" ), "Sandbox.*.dll" ) );

		return files;
	}
}
