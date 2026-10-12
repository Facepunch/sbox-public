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

			if ( addon is not null && addon == Project.Current && !StartupLoadProject.IsLoading )
			{
				if ( addon.Config.AI?.EnableMcpCommunication != false )
				{
					Mcp.McpPipeServer.Start();
				}
				else
				{
					Mcp.McpPipeServer.Stop();
				}
			}

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
			FileWatch.Tick();
			var compileSuccess = await Project.CompileAsync();
			FileWatch.Tick();

			var loader = Sandbox.GameInstanceDll.PackageLoader;
			if ( compileSuccess && Project.CompileGroup.BuildResult.Output is { } outputs )
				loader.QueueCompiledAssemblies( outputs );

			loader.Tick();
		}

		/// <summary>
		/// Regenerates the project's solution
		/// </summary>
		public static async Task GenerateSolution()
		{
			await Project.GenerateSolution();
		}
	}
}
