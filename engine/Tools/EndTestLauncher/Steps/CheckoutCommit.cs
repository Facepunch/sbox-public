namespace Facepunch.EndTestLauncher.Steps;

/// <summary>
/// Checks out a specific commit in the repository
/// </summary>
internal class CheckoutCommit( string repository, string commit, string workDir ) : Step( "Checkout Commit" )
{
	private readonly string repository = repository;
	private readonly string commit = commit;
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

		Log.Info( $"Checking out commit: {commit}" );

		// Fetch to ensure we have the latest commits
		if ( !Utility.RunProcess( "git", "fetch origin", repoPath ) )
		{
			Log.Warning( "Failed to fetch from origin, but continuing..." );
		}

		// Verify that the commit exists
		Log.Info( "Verifying commit exists..." );
		if ( !Utility.RunProcess( "git", $"rev-parse --verify {commit}^{{commit}}", repoPath ) )
		{
			Log.Error( $"Commit {commit} does not exist in the repository" );
			Log.Info( "Trying to fetch from all remotes..." );
			
			// Try fetching from all remotes
			if ( !Utility.RunProcess( "git", "fetch --all", repoPath ) )
			{
				Log.Warning( "Failed to fetch from all remotes" );
			}
			
			// Try again after fetching
			if ( !Utility.RunProcess( "git", $"rev-parse --verify {commit}^{{commit}}", repoPath ) )
			{
				Log.Error( $"Commit {commit} still not found after fetching" );
				return ExitCode.Failure;
			}
		}

		// Checkout the commit (detached HEAD state)
		Log.Info( $"Checking out commit {commit} (detached HEAD)..." );
		if ( !Utility.RunProcess( "git", $"checkout {commit}", repoPath ) )
		{
			Log.Error( $"Failed to checkout commit: {commit}" );
			return ExitCode.Failure;
		}

		// Show commit information
		Log.Info( "Commit information:" );
		Utility.RunProcess( "git", "log -1 --oneline --decorate", repoPath );
		Log.Info( "" );
		Log.Warning( "Note: You are in a detached HEAD state. Any commits made will not belong to any branch." );

		return ExitCode.Success;
	}
}
