using System.Threading;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// The frame each drawing thread is rendering, kept apart: the main thread and the render thread (<see cref="ThreadSafe.IsRenderThread"/>)
/// draw frames of their own at once in the editor, its tool views on the main thread while the render thread draws the scene
/// view's frame handed over. A frame asked for meanwhile on the same drawing thread is the one being rendered's child; one on the
/// other drawing thread isn't. A worker recording segments works in the render thread's frame while that one is being drawn,
/// else in the main thread's.
/// </summary>
internal sealed class DrawerFrames<T> where T : class
{
	const int MainThread = 0, RenderThread = 1;

	readonly T[] frames = new T[2];

	/// <summary>
	/// The frame this thread's drawing thread is rendering, or null.
	/// </summary>
	public T Current => Volatile.Read( ref frames[Slot()] );

	/// <summary>
	/// Mark <paramref name="frame"/> as this thread's drawing thread's frame until the scope is disposed, when the one before it is again.
	/// </summary>
	public Scope Enter( T frame )
	{
		var slot = Slot();
		var parent = Volatile.Read( ref frames[slot] );
		Volatile.Write( ref frames[slot], frame );
		return new Scope( this, slot, parent );
	}

	int Slot()
	{
		if ( ThreadSafe.IsRenderThread ) return RenderThread;
		if ( ThreadSafe.IsMainThread ) return MainThread;
		return Volatile.Read( ref frames[RenderThread] ) is not null ? RenderThread : MainThread;
	}

	/// <summary>
	/// A frame marked as being rendered (<see cref="Enter"/>).
	/// </summary>
	public readonly struct Scope( DrawerFrames<T> owner, int slot, T parent ) : IDisposable
	{
		public void Dispose() => Volatile.Write( ref owner.frames[slot], parent );
	}
}
