using System.Text.RegularExpressions;

namespace Facepunch.EndTestLauncher.Steps;

/// <summary>
/// Clones or updates a GitHub repository
/// </summary>
internal class CloneRepository( string repository, string workDir ) : Step( "Clone Repository" )
{
	private readonly string repository = repository;
	private readonly string workDir = workDir;

	protected override ExitCode RunInternal()
	{
		// Validate repository format (owner/repo)
		if ( !IsValidRepositoryFormat( repository ) )
		{
			Log.Error( $"Invalid repository format: {repository}. Expected format: owner/repo" );
			return ExitCode.Failure;
		}

		// Extract owner and repo name
		var parts = repository.Split( '/' );
		if ( parts.Length != 2 )
		{
			Log.Error( $"Invalid repository format: {repository}" );
			return ExitCode.Failure;
		}

		var owner = parts[0];
		var repoName = parts[1];
		var repoUrl = $"https://github.com/{repository}.git";
		var repoPath = Path.Combine( workDir, $"{owner}_{repoName}" );

		Log.Info( $"Repository: {repository}" );
		Log.Info( $"Working directory: {workDir}" );
		Log.Info( $"Repository path: {repoPath}" );

		// Create work directory if it doesn't exist
		if ( !Directory.Exists( workDir ) )
		{
			Log.Info( $"Creating work directory: {workDir}" );
			Directory.CreateDirectory( workDir );
		}

		// Check if repository already exists
		if ( Directory.Exists( repoPath ) && Directory.Exists( Path.Combine( repoPath, ".git" ) ) )
		{
			Log.Info( $"Repository already exists at {repoPath}, fetching updates..." );
			
			// Fetch latest changes
			if ( !Utility.RunProcess( "git", "fetch --all", repoPath ) )
			{
				Log.Warning( "Failed to fetch updates, but continuing..." );
			}
		}
		else
		{
			Log.Info( $"Cloning repository from {repoUrl}..." );
			
			// Clone the repository
			if ( !Utility.RunProcess( "git", $"clone {repoUrl} \"{repoPath}\"", workDir ) )
			{
				Log.Error( $"Failed to clone repository: {repository}" );
				return ExitCode.Failure;
			}

			Log.Info( $"Successfully cloned repository to {repoPath}" );
		}

		return ExitCode.Success;
	}

	private static bool IsValidRepositoryFormat( string repo )
	{
		// Basic validation: owner/repo format
		var pattern = @"^[a-zA-Z0-9._-]+/[a-zA-Z0-9._-]+$";
		return Regex.IsMatch( repo, pattern );
	}
}
