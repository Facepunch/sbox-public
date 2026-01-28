using System.Threading;

namespace Editor;

public abstract class EditorSystem
{
	/// <summary>
	/// The scene we're currently editing
	/// </summary>
	public abstract Scene Scene { get; }

	/// <summary>
	/// Run a process on multiple items, show progress bar
	/// </summary>
	public abstract Task ForEachAsync<T>( IEnumerable<T> list, string title, Func<T, CancellationToken, Task> worker, CancellationToken cancel = default, bool modal = false );

	/// <summary>
	/// Start a progress section
	/// </summary>
	public abstract IProgressSection ProgressSection( bool modal = false );

	/// <summary>
	/// The main editor camera
	/// </summary>
	public abstract CameraComponent Camera { get; }
}

public interface IProgressSection : IDisposable
{
	public string Icon { get; set; }
	public string Title { get; set; }
	public string Subtitle { get; set; }
	public double Current { get; set; }
	public double TotalCount { get; set; }
	public double ProgressDelta => (TotalCount > 0 ? Current / TotalCount : 0).Clamp( 0, 1 );

	public void Cancel();
	public CancellationToken GetCancel();
}
