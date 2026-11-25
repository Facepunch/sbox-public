using System.Threading;

namespace Editor;

/// <summary>
/// Supplies editor compile work for a component or GameObjectSystem in a saved scene.
/// </summary>
public interface ISceneCompile
{
	/// <summary>
	/// Describe the work without modifying the scene. Return null when this component has nothing to bake or clear.
	/// </summary>
	SceneCompileWork GetWork();
}

/// <summary>
/// Services provided by the scene compiler while a job is running.
/// </summary>
public interface ICompileContext
{
	/// <summary>
	/// True when the job must discard its cached outputs and rebuild from source.
	/// </summary>
	bool IsClean { get; }

	/// <summary>
	/// Reports the current job's progress to the compile window.
	/// </summary>
	IProgressSection Progress { get; }

	/// <summary>
	/// Signals that the compile has been cancelled.
	/// </summary>
	CancellationToken CancellationToken { get; }

	/// <summary>
	/// Writes a message to the compile log.
	/// </summary>
	void Log( string message );

	/// <summary>
	/// Registers a file after writing it when its name is determined during the compile.
	/// The path is relative to the scene data folder.
	/// </summary>
	void AddOutput( string relativePath );
}

/// <summary>
/// Collects files to remove when a scene compile job clears its bake.
/// </summary>
public interface ISceneCompileCleanup
{
	/// <summary>
	/// Removes a generated file relative to the scene data folder after cleared references have been saved.
	/// </summary>
	void DeleteOutput( string relativePath );
}

/// <summary>
/// An asynchronous operation offered in the scene compile window. Jobs with the same group share a checkbox.
/// </summary>
/// <param name="Group">Display name for the group.</param>
/// <param name="Title">Name of this specific job, shown in the compile log and timings.</param>
/// <param name="Run">Runs the job on the editor thread. Use null for a job that only has old outputs to clear.</param>
/// <param name="Order">Lower values run first, before the final runtime scene is written.</param>
public sealed record SceneCompileWork( string Group, string Title, Func<ICompileContext, Task> Run, int Order = 0 )
{
	/// <summary>
	/// Files this job owns, relative to the scene data folder. The compiler counts existing files
	/// in the stage report and removes them when the stage is cleared.
	/// </summary>
	public IEnumerable<string> Outputs { get; init; } = [];

	/// <summary>
	/// Clears this job's baked state and registers its files for deletion.
	/// Call <see cref="Scene.ISceneEditorSession.MarkBakeReferencesChanged"/> when serialized bake references change.
	/// </summary>
	public Action<ISceneCompileCleanup> Clear { get; init; }
}
