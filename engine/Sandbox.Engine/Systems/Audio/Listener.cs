using System.Collections.Concurrent;

namespace Sandbox.Audio;

/// <summary>
/// Listens for sounds in a scene.
/// </summary>
internal class Listener : IValid, IDisposable
{
	public bool IsValid => !_destroyed;

	private static readonly List<Listener> _active = [];
	private static readonly List<Listener> _removed = [];
	private static readonly ConcurrentQueue<Listener> _removeQueue = [];

	/// <summary>
	/// Currently active listeners.
	/// </summary>
	public static IReadOnlyList<Listener> Active => _active.AsReadOnly();

	/// <summary>
	/// Listener's that have been removed this frame.
	/// </summary>
	public static IReadOnlyList<Listener> Removed => _removed.AsReadOnly();

	/// <summary>
	/// Direct access to active listeners list. Use for iteration without allocation.
	/// </summary>
	internal static List<Listener> ActiveList => _active;

	/// <summary>
	/// Direct access to removed listeners list. Use for iteration without allocation.
	/// </summary>
	internal static List<Listener> RemovedList => _removed;

	/// <summary>
	/// For the mixing thread to know which listeners have been removed.
	/// </summary>
	public static ConcurrentQueue<Listener> RemoveQueue => _removeQueue;

	private Transform _transform;

	/// <summary>
	/// Listener's world transform, where we are listening to sounds from.
	/// </summary>
	public Transform Transform
	{
		get
		{
			ThreadSafe.AssertIsMainThread();
			return _transform;
		}
		set
		{
			ThreadSafe.AssertIsMainThread();
			_transform = value;
		}
	}

	/// <summary>
	/// Mixer thread safe transform.
	/// </summary>
	internal Transform MixTransform { get; set; }

	/// <summary>
	/// Listener's world position, where we are listening to sounds from.
	/// </summary>
	public Vector3 Position => Transform.Position;

	/// <summary>
	/// Scene this listener belongs to.
	/// </summary>
	public readonly Scene Scene;

	private bool _destroyed;

	public static readonly Listener Local = new();

	private Listener()
	{
	}

	public Listener( Scene scene )
	{
		Scene = scene;

		_active.Add( this );
	}

	public void Dispose()
	{
		if ( _destroyed )
			return;

		RemoveQueue.Enqueue( this );

		_removed.Add( this );
		_active.Remove( this );

		_destroyed = true;

		GC.SuppressFinalize( this );
	}

	~Listener()
	{
		Dispose();
	}

	/// <summary>
	/// Get active sound listener states.
	/// </summary>
	public static void GetActive( List<Listener> listeners )
	{
		foreach ( var listener in _active )
		{
			listener.MixTransform = listener.Transform;
			listeners.Add( listener );
		}

		_removed.Clear();
	}

	public static void Clear()
	{
		_active.Clear();
		_removed.Clear();
		_removeQueue.Clear();
	}
}
