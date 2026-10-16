using DiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Editor.Mcp;

[McpToolset( "editor" )]
internal static class EditorStatusTools
{
	/// <summary>
	/// Inspect the current editor project, scene and compiler.
	/// </summary>
	[McpTool.ReadOnly( "editor_status" ), McpListed]
	[Description( "Get the current state of the editor - engine version, which project is open, the active scene and whether it has unsaved changes, whether play mode is running or paused, how many tools are registered, the directory paths that matter (logs, project code and assets), and the state of the code compiler. Check IsCompiling/LastCompileSucceeded here after editing code instead of relying on read_console - a successful compile can scroll out of view or be mistaken for the last failure you saw." )]
	public static EditorStatus GetEditorStatus()
	{
		var scene = SceneEditorSession.Active?.Scene ?? Game.ActiveScene;
		var compileGroup = Project.CompileGroup;
		var buildResult = compileGroup?.BuildResult;

		return new EditorStatus
		{
			EngineVersion = $"{Sandbox.Application.Version}",
			Project = Project.Current?.Config?.Ident,
			ProjectTitle = Project.Current?.Config?.Title,
			ActiveScene = scene?.Name,
			ActiveScenePath = scene?.Source?.ResourcePath,
			SceneHasUnsavedChanges = SceneEditorSession.Active?.HasUnsavedChanges ?? false,
			IsPlaying = Game.IsPlaying,
			IsPaused = Game.IsPaused,
			ToolCount = ToolRegistry.All().Count(),
			IsCompiling = compileGroup is not null && (compileGroup.IsBuilding || compileGroup.NeedsBuild),
			LastCompileSucceeded = buildResult is { Diagnostics: not null } r ? r.Success : null,
			LastCompileErrors = buildResult?.Diagnostics?.Count( x => x.Severity == DiagnosticSeverity.Error ) ?? 0,
			Paths = new EditorPaths
			{
				Engine = FileSystem.Root.GetFullPath( "/" ),
				Logs = FileSystem.Root.GetFullPath( "/logs/" ),
				ProjectRoot = Project.Current?.GetRootPath(),
				ProjectSettings = FileSystem.ProjectSettings?.GetFullPath( "/" )
			}
		};
	}

}

/// <summary>
/// What editor_status reports.
/// </summary>
internal class EditorStatus
{
	public string EngineVersion { get; set; }
	public string Project { get; set; }
	public string ProjectTitle { get; set; }
	public string ActiveScene { get; set; }
	public string ActiveScenePath { get; set; }
	public bool SceneHasUnsavedChanges { get; set; }
	public bool IsPlaying { get; set; }
	public bool IsPaused { get; set; }
	public int ToolCount { get; set; }

	/// <summary>
	/// Whether the project's code compiler is currently building, or has changes queued to build.
	/// </summary>
	public bool IsCompiling { get; set; }

	/// <summary>
	/// Whether the most recent finished compile succeeded. Null if no compile has happened yet.
	/// </summary>
	public bool? LastCompileSucceeded { get; set; }

	/// <summary>
	/// Error count from the most recent finished compile - only meaningful when LastCompileSucceeded is false.
	/// </summary>
	public int LastCompileErrors { get; set; }

	public EditorPaths Paths { get; set; }
}

/// <summary>
/// The directory paths that matter to an agent working with the editor.
/// </summary>
internal class EditorPaths
{
	public string Engine { get; set; }
	public string Logs { get; set; }
	public string ProjectRoot { get; set; }
	public string ProjectSettings { get; set; }
}
