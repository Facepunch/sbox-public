using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class SignBinaries() : Step( "SignBinaries" )
{

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

		Log.Info( $"Signing {filesToSign.Count} files in a single batch..." );

		var fileArgs = string.Join( " ", filesToSign.Select( f => $"\"{f}\"" ) );

		bool success = Utility.RunProcess(
			"AzureSignTool",
			$"sign -kvu \"{vaultUrl}\" -kvi \"{clientId}\" -kvs \"{clientSecret}\" -kvt \"{tenantId}\" -kvc FPCodeSign -tr http://timestamp.digicert.com {fileArgs}",
			rootDir
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
