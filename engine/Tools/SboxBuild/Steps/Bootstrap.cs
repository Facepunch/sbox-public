using static Facepunch.Constants;

namespace Facepunch.Steps;

internal static class Bootstrap
{
	internal static ExitCode Run( bool verbose = false )
	{
		if ( !File.Exists( Path.Combine( "engine", "Tools", "SboxBuild", "SboxBuild.csproj" ) ) )
		{
			Log.Error( "Run bootstrap from the s&box repository root." );
			return ExitCode.Failure;
		}

		var isPublic = Build.IsPublicSourceDistribution();
		var extension = OperatingSystem.IsWindows() ? ".bat" : ".sh";
		var wrapper = (isPublic ? "Setup" : "Bootstrap") + extension;
		var rerun = OperatingSystem.IsWindows() ? wrapper : $"./{wrapper}";
		var stages = Build.GetStages().ToList();
		// First, so a checkout whose later stage fails (no network, say) still refreshes itself on the next pull.
		if ( isPublic )
			stages.Insert( 0, ("Git hooks", new InstallGitHooks().Run) );
		stages.Add( ("Shaders", new BuildShaders().Run) );
		stages.Add( ("Content", new BuildContent().Run) );
		try
		{
			Log.StartBootstrap( verbose );
			Log.Record( $"Bootstrap started in {Directory.GetCurrentDirectory()} (public: {Build.IsPublicSourceDistribution()}, verbose: {verbose})" );
			string failedStage = null;

			var result = BuildDisplay.Run( "s&box / bootstrap", () =>
			{
				for ( var i = 0; i < stages.Count; i++ )
				{
					var stage = stages[i];
					Log.ClearTail();
					BuildDisplay.StartStage( i );
					var stageResult = ExitCode.Failure;
					try
					{
						stageResult = stage.Run();
					}
					catch ( Exception ex )
					{
						Log.Error( ex.ToString() );
					}
					BuildDisplay.FinishStage( stageResult );
					if ( stageResult != ExitCode.Success )
					{
						Log.CaptureFailure();
						failedStage = stage.Name;
						return stageResult;
					}
				}

				return ExitCode.Success;
			}, stages.Select( stage => stage.Name ).ToArray() );

			if ( result != ExitCode.Success )
			{
				Log.Error( failedStage == null ? "Bootstrap failed while reporting build progress." : $"Bootstrap failed: {failedStage}." );
				Log.Error( $"Fix the error and rerun {rerun}. For full output, run {rerun} --verbose." );
				return result;
			}

			Log.WriteConsole( $"Bootstrap completed. Launch {Path.Combine( "game", "sbox-dev" + (OperatingSystem.IsWindows() ? ".exe" : "") )}.", "" );
			return ExitCode.Success;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Bootstrap failed: {ex}" );
			Log.Error( $"Fix the error and rerun {rerun} --verbose for full output." );
			return ExitCode.Failure;
		}
		finally
		{
			Log.FinishBootstrap();
		}
	}
}
