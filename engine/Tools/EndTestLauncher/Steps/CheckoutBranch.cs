namespace Facepunch.EndTestLauncher.Steps;

/// <summary>
/// Checks out a specific branch in the repository
/// </summary>
internal class CheckoutBranch( string repository, string branch, string workDir ) : Step( "Checkout Branch" )
{
	private readonly string repository = repository;
	private readonly string branch = branch;
	private readonly string workDir = workDir;

	protected override ExitCode RunInternal()
	{
		// Extract owner and repo name
		var parts = repository.Split( '/' );
		if ( parts.Length != 2 )
		{
			Log.Error( $"Invalid repository format: {repository}" );
			return ExitCode.Failure;
		}

		var owner = parts[0];
		var repoName = parts[1];
		var repoPath = Path.Combine( workDir, $"{owner}_{repoName}" );

		if ( !Directory.Exists( repoPath ) )
		{
			Log.Error( $"Repository path does not exist: {repoPath}" );
			return ExitCode.Failure;
		}

		Log.Info( $"Checking out branch: {branch}" );

		// Fetch to ensure we have the latest branch info
		if ( !Utility.RunProcess( "git", "fetch origin", repoPath ) )
		{
			Log.Warning( "Failed to fetch from origin, but continuing..." );
		}

		// Checkout the branch
		// First try to checkout existing local branch or create tracking branch
		if ( !Utility.RunProcess( "git", $"checkout {branch}", repoPath ) )
		{
			Log.Info( $"Branch {branch} not found locally, trying to checkout from remote..." );
			
			// Try checking out as remote tracking branch
			if ( !Utility.RunProcess( "git", $"checkout -b {branch} origin/{branch}", repoPath ) )
			{
				Log.Error( $"Branch {branch} does not exist locally or remotely" );
				Log.Info( "Available branches:" );
				Utility.RunProcess( "git", "branch -a", repoPath );
				return ExitCode.Failure;
			}
		}

		// Pull latest changes
		Log.Info( "Pulling latest changes..." );
		if ( !Utility.RunProcess( "git", "pull", repoPath ) )
		{
			Log.Warning( "Failed to pull latest changes, but continuing..." );
		}

		// Show current commit
		Log.Info( "Current commit:" );
		Utility.RunProcess( "git", "log -1 --oneline", repoPath );

		return ExitCode.Success;
	}
}
