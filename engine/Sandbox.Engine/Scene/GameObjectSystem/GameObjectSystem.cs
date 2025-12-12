using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// Allows creation of a system that always exists in every scene, is hooked into the scene's lifecycle, 
/// and is disposed when the scene is disposed.
/// </summary>
[Expose]
public abstract partial class GameObjectSystem : IDisposable
{
	public Scene Scene { get; private set; }

	List<IDisposable> disposables = new List<IDisposable>();

	public GameObjectSystem( Scene scene )
	{
		Scene = scene;
		Id = Guid.NewGuid();
	}

	public virtual void Dispose()
	{
		foreach ( var d in disposables )
		{
			d.Dispose();
		}

		Scene = null;
	}

	/// <summary>
	/// Listen to a frame stage. Order is used to determine the order in which listeners are called, the default action always happens at 0, so if you
	/// want it to happen before you should go to -1, if you want it to happen after go to 1 etc.
	/// </summary>
	protected void Listen( Stage stage, int order, Action function, string debugName )
	{
		var d = Scene.AddHook( stage, order, function, GetType().Name, debugName );
		disposables.Add( d );
	}

	/// <summary>
	/// A list of stages in the scene tick in which we can hook
	/// </summary>
	public enum Stage
	{
		/// <summary>
		/// At the very start of the scene update
		/// </summary>
		StartUpdate,

		/// <summary>
		/// Bones are worked out
		/// </summary>
		UpdateBones,

		/// <summary>
		/// Physics step, called in fixed update
		/// </summary>
		PhysicsStep,

		/// <summary>
		/// When transforms are interpolated
		/// </summary>
		Interpolation,

		/// <summary>
		/// At the very end of the scene update
		/// </summary>
		FinishUpdate,

		/// <summary>
		/// Called at the start of fixed update
		/// </summary>
		StartFixedUpdate,

		/// <summary>
		/// Called at the end of fixed update
		/// </summary>
		FinishFixedUpdate,

		/// <summary>
		/// Called after a scene has been loaded
		/// </summary>
		SceneLoaded,
	}

	/// <summary>
	/// When implementing an ITraceProvider, the most important thing to keep in mind
	/// is that the call to DoTrace should be thread safe. This might be called from
	/// multiple threads at once, so you better watch out.
	/// </summary>
	public interface ITraceProvider
	{
		public void DoTrace( in SceneTrace trace, List<SceneTraceResult> results );
		public SceneTraceResult? DoTrace( in SceneTrace trace );
	}

}

/// <summary>
/// A syntax sugar wrapper around GameObjectSystem, which allows you to access your system using
/// SystemName.Current instead of Scene.GetSystem.
/// </summary>
public abstract class GameObjectSystem<T> : GameObjectSystem where T : GameObjectSystem
{
	protected GameObjectSystem( Scene scene ) : base( scene )
	{
	}

	public static T Current => Get( Game.ActiveScene );

	public static T Get( Scene scene ) => scene?.GetSystem<T>() ?? default;
}
