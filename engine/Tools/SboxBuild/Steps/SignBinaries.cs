using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Signs all eligible binaries in the build output using the <c>sign</c> dotnet tool
/// with Azure Trusted Signing (artifact-signing). Auth is handled via DefaultAzureCredential
/// (expects AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_CLIENT_SECRET in the environment).
/// </summary>
internal class SignBinaries() : Step( "SignBinaries" )
{

	protected override ExitCode RunInternal()
	{
		string rootDir = Directory.GetCurrentDirectory();

		var endpointUrl = Environment.GetEnvironmentVariable( "CODESIGN_ENDPOINT_URL" );
		var accountName = Environment.GetEnvironmentVariable( "CODESIGN_ACCOUNT_NAME" );
		var certificateProfile = Environment.GetEnvironmentVariable( "CODESIGN_CERTIFICATE_PROFILE" );

		if ( string.IsNullOrEmpty( endpointUrl ) || string.IsNullOrEmpty( accountName ) || string.IsNullOrEmpty( certificateProfile ) )
		{
			Log.Error( "Missing signing env vars — need CODESIGN_ENDPOINT_URL, CODESIGN_ACCOUNT_NAME, CODESIGN_CERTIFICATE_PROFILE" );
			return ExitCode.Failure;
		}

		var filesToSign = CollectFilesToSign( rootDir );

		if ( filesToSign.Count == 0 )
		{
			Log.Warning( "No files found to sign." );
			return ExitCode.Success;
		}

		Log.Info( $"Signing {filesToSign.Count} files..." );

		var fileArgs = string.Join( " ", filesToSign.Select( f => $"\"{f}\"" ) );

		bool success = Utility.RunProcess(
			"sign",
			$"code artifact-signing -ase \"{endpointUrl}\" -asa \"{accountName}\" -ascp \"{certificateProfile}\" -v warning -m 16 {fileArgs}",
			rootDir,
			timeoutMs: 600000
		);

		if ( !success )
		{
			Log.Error( "Failed to sign files." );
			return ExitCode.Failure;
		}

		Log.Info( $"Successfully signed {filesToSign.Count} files." );
		return ExitCode.Success;
	}

	private static List<string> CollectFilesToSign( string rootDir )
	{
		var gamePath = Path.Combine( rootDir, "game" );
		var files = new List<string>();

		files.AddRange( Directory.EnumerateFiles( gamePath, "*.exe", SearchOption.AllDirectories ) );
		files.AddRange( Directory.EnumerateFiles( gamePath, "*.dll", SearchOption.AllDirectories ) );
		return files;
	}
}
