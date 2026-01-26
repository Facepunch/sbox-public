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
	public abstract Task ForEachAsync<T>( IEnumerable<T> list, string title, Func<T, Task> worker, CancellationToken cancel = default );
}
