namespace Editor;

public static partial class EditorUtility
{
	public static partial class Projects
	{
		public static IReadOnlyList<Project> GetAll() => Project.All.AsReadOnly();

		public static async Task<bool> Updated( Project addon )
		{
			// Save changes
			addon?.Save();

			//
			// If we're a transient project we don't need to dirty everything else.
			// we just really want to call the save callback and return.
			//
			if ( addon is not null && addon.IsTransient )
			{
				return true;
			}

			bool compileSuccess = await Project.CompileAsync();
			if ( !compileSuccess )
				return false;

			if ( addon is not null )
			{
				if ( addon.Compiler is not null && !addon.Compiler.BuildSuccess )
					return false;

				if ( addon.EditorCompiler is not null && !addon.EditorCompiler.BuildSuccess )
					return false;
			}

			await WaitForCompiles();

			EditorEvent.Run( "localaddons.changed" );
			SceneEditorSession.Active?.UpdateEditorTitle();

			return true;
		}

		/// <summary>
		/// Wait for the local compiles to be finished
		/// </summary>
		public static async Task WaitForCompiles()
		{
			// give time for any files to finish being written
			//await Task.Delay( 1000 );

			// force finding new files, running callbacks
			FileWatch.Tick();

			// wait for compiles to finish
			await Project.CompileAsync();

			// give time for any files to finish being written
			// this is horrible in this context. We have to wait for 
			// filewatch to tick again to make sure we've picked up all the written files
			// search for FileSystem.Watch( "/.bin/*.dll" ); We need a better way to trigger
			// this shit manually in PackageLoader to 
			// 1. Say this project changed so reload the new package
			// 2. Don't re-trigger after it detects filesystem changes
			await Task.Delay( 500 );

			// filesystem callbacks..
			FileWatch.Tick();

			// Tick the loader to actually load
			Sandbox.GameInstanceDll.PackageLoader.Tick();

			// give time for any files to finish being written
			//await Task.Delay( 1000 );
		}

		/// <summary>
		/// Regenerates the project's solution
		/// </summary>
		public static async Task GenerateSolution()
		{
			await Project.GenerateSolution();
		}

		/// <summary>
		/// Replaces the mounted parent of the current addon with an exact remote revision,
		/// recompiles the addon against it, then recreates the editor game instance.
		/// </summary>
		public static async Task UpdateParentPackage( string packageIdent, long revisionId )
		{
			ThreadSafe.AssertIsMainThread();

			var project = Project.Current ?? throw new System.InvalidOperationException( "No project is currently open." );
			var parentPackage = project.Config.GetMetaOrDefault<string>( "ParentPackage", null );

			if ( project.Config.Type != "addon" || !IsSamePackage( parentPackage, packageIdent ) )
				throw new System.InvalidOperationException( $"'{packageIdent}' is not the current addon's parent package." );

			if ( revisionId is <= 0 or > int.MaxValue )
				throw new System.ArgumentOutOfRangeException( nameof( revisionId ), "The package revision is invalid." );

			var mountedParent = PackageManager.Find( parentPackage, false );
			if ( mountedParent?.Package.Revision?.VersionId > revisionId )
				return;

			Package.TryParseIdent( parentPackage, out var parentParts );
			var exactIdent = Package.FormatIdent( parentParts.org, parentParts.package, (int)revisionId );
			var updatedPackage = await Package.FetchAsync( exactIdent, false, false );

			if ( updatedPackage?.Revision?.VersionId != revisionId )
				throw new System.InvalidOperationException( $"Unable to fetch revision {revisionId} of '{packageIdent}'." );

			var gameInstance = Sandbox.Engine.IGameInstanceDll.Current
				?? throw new System.InvalidOperationException( "The editor game instance is not available." );

			gameInstance.CloseGame();

			await PackageManager.InstallAsync( new PackageLoadOptions( exactIdent, "tools" )
			{
				AllowLocalPackages = false,
				ReplaceExistingRevision = true
			} );

			await AssetSystem.InstallAsync( updatedPackage, false );

			project.Compiler?.MarkForRecompile();
			project.EditorCompiler?.MarkForRecompile();
			await Project.GenerateSolution();

			if ( !await Updated( project ) )
				throw new System.InvalidOperationException( "The addon failed to compile against the updated parent package." );

			if ( !await gameInstance.LoadGamePackageAsync( exactIdent, Sandbox.Engine.GameLoadingFlags.Host | Sandbox.Engine.GameLoadingFlags.Reload, default ) )
				throw new System.InvalidOperationException( "The updated parent package could not be loaded." );

			await project.Package.MountAsync( true );
			await ResourceLoader.LoadAllGameResourceAsync( FileSystem.Mounted, default, true );
		}

		private static bool IsSamePackage( string left, string right )
		{
			if ( !Package.TryParseIdent( left, out var leftParts ) || leftParts.local )
				return false;

			if ( !Package.TryParseIdent( right, out var rightParts ) || rightParts.local )
				return false;

			return string.Equals( leftParts.org, rightParts.org, System.StringComparison.OrdinalIgnoreCase )
				&& string.Equals( leftParts.package, rightParts.package, System.StringComparison.OrdinalIgnoreCase );
		}
	}
}
