using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// We want to execute callbacks in a predictable order. This happens
/// naturally when spawning one GameObject, but when spawning a scene, or a 
/// prefab, we want to hold the calls to things like OnEnable and call them all
/// after OnStart or whatever has been called on all the objects in the batch.
/// </summary>
class CallbackBatch : System.IDisposable
{
	static CallbackBatch Current { get; set; }
	static Stack<CallbackBatch> Pool = new();

	/// <summary>
	/// Stores either a direct dispatch target (no delegate allocation) or a fallback action for closures.
	/// </summary>
	record struct ActionTarget( object Target, CommonCallback Callback, Action FallbackAction, string name, Scene Scene );

	CallbackBatch _previous;

	string Name;

	/// <summary>
	/// Pre-sorted CommonCallback values used in Execute() to avoid per-call LINQ OrderBy allocation.
	/// </summary>
	static readonly CommonCallback[] SortedCallbacks = Enum.GetValues<CommonCallback>().OrderBy( x => (int)x ).ToArray();

	class Group
	{
		List<ActionTarget> Actions = new List<ActionTarget>();

		public void Clear()
		{
			Actions.Clear();
		}

		public void Add( ActionTarget action )
		{
			Actions.Add( action );
		}

		public void Execute( CallbackBatch parent )
		{
			if ( Actions.Count == 0 )
				return;

			foreach ( var action in Actions )
			{
				try
				{
					using var scope = action.Scene is not null ? new ScenePushScope( action.Scene ) : default;

					switch ( action.Target )
					{
						case Component c: c.InvokeCallback( action.Callback ); break;
						case GameObject go: go.InvokeCallback( action.Callback ); break;
						default: action.FallbackAction?.Invoke(); break;
					}
				}
				catch ( System.Exception e )
				{
					// We want to know how we got here to make these things easier to diagnose
					var fullStack = new StackTrace( true ).ToString();

					var wrap = new Exception( $"{action.name} on {action.Target} failed from {parent.Name}: {e.Message}\n{fullStack}", e );
					Log.Error( wrap, wrap.Message );
				}
			}
		}
	}

	Dictionary<CommonCallback, Group> Groups = new();

	static CallbackBatch GetOrCreateBatch( string debugName )
	{
		if ( !Pool.TryPop( out var instance ) )
		{
			instance = new CallbackBatch();
		}

		instance.Name = debugName;
		return instance;
	}

	/// <summary>
	/// Add callbacks to the previous batch (or create one). This allows for one single batch, the
	/// most outer one, and won't create a new batch for inner ones. This is used when doing things like
	/// deserializing a map, so all the OnEnable etc are executed at the same time, and in the right order.
	/// </summary>
	public static CallbackBatch Batch( [CallerMemberName] string caller = null )
	{
		if ( Current is not null )
			return null;

		var instance = GetOrCreateBatch( caller );

		Assert.IsNull( instance._previous );

		instance._previous = default;
		Current = instance;
		return Current;
	}

	/// <summary>
	/// Collect callbacks in this scope and execute them immediately at the end of this batch. This is used
	/// for things like gameobject Clones, where we're going to want access to the object straight after 
	/// creating it.. and if we're inside a Batch then OnEnable etc won't have been called, so it will be
	/// confusing to everyone.
	/// </summary>
	public static CallbackBatch Isolated( [CallerMemberName] string caller = null )
	{
		var instance = GetOrCreateBatch( caller );

		Assert.IsNull( instance._previous );

		instance._previous = Current;
		Current = instance;
		return Current;
	}

	/// <summary>
	/// Adds a direct-dispatch callback, avoiding a delegate allocation.
	/// </summary>
	internal static void Add( CommonCallback order, Component target, string name )
	{
		if ( Current is not null )
		{
			var group = Current.Groups.GetOrCreate( order );
			group.Add( new ActionTarget( target, order, null, name, target.Scene ) );
			return;
		}

		throw new System.Exception( $"CallbackBatch.Add called outside of a batch for '{order}'" );
	}

	/// <inheritdoc cref="Add(CommonCallback, Component, string)"/>
	internal static void Add( CommonCallback order, GameObject target, string name )
	{
		if ( Current is not null )
		{
			var group = Current.Groups.GetOrCreate( order );
			group.Add( new ActionTarget( target, order, null, name, target.Scene ) );
			return;
		}

		throw new System.Exception( $"CallbackBatch.Add called outside of a batch for '{order}'" );
	}

	/// <summary>
	/// Adds a closure-based fallback callback. Prefer the non-Action overloads to avoid delegate allocation.
	/// </summary>
	public static void Add( CommonCallback order, Action action, Component target, string name )
	{
		if ( Current is not null )
		{
			var group = Current.Groups.GetOrCreate( order );
			group.Add( new ActionTarget( null, order, action, name, target.Scene ) );
			return;
		}

		throw new System.Exception( $"CallbackBatch.Add called outside of a batch for '{order}'" );
	}

	/// <inheritdoc cref="Add(CommonCallback, Action, Component, string)"/>
	public static void Add( CommonCallback order, Action action, GameObject target, string name )
	{
		if ( Current is not null )
		{
			var group = Current.Groups.GetOrCreate( order );
			group.Add( new ActionTarget( null, order, action, name, target.Scene ) );
			return;
		}

		throw new System.Exception( $"CallbackBatch.Add called outside of a batch for '{order}'" );
	}

	void Execute()
	{
		if ( Groups.Count == 0 ) return;

		// when we execute a group (like OnEnable), it might create new gameobjects or components
		// so we create a new batch group, which will catch those and execute them in the right order too
		using var batch = CallbackBatch.Batch();

		// Iterate over pre-sorted callbacks instead of allocating OrderBy each time
		foreach ( var key in SortedCallbacks )
		{
			if ( !Groups.TryGetValue( key, out var group ) ) continue;
			group.Execute( this );
			group.Clear();
		}
	}

	public void Dispose()
	{
		if ( Current == this )
		{
			Current = _previous;
		}

		Execute();

		if ( Pool.Count < 2 )
		{
			_previous = default;
			Pool.Push( this );
		}
	}
}

/// <summary>
/// A list of component methods that are deferred and batched into groups, and exected in group order.
/// This is used to ensure that components are initialized in a predictable order.
/// The order of this enum is critical.
/// </summary>
internal enum CommonCallback
{
	Unknown,

	/// <summary>
	/// The component is deserializing.
	/// </summary>
	Deserialize,

	/// <summary>
	/// The component has been deserialized, or edited in the editor
	/// </summary>
	Validate,

	/// <summary>
	/// An opportunity for the component to load any data they need to load
	/// </summary>
	Loading,

	/// <summary>
	/// The component is awake. Called only once, on first enable.
	/// </summary>
	Awake,

	/// <summary>
	/// Component has been enabled
	/// </summary>
	Enable,

	/// <summary>
	/// The component has become dirty, usually due to a property changing
	/// </summary>
	Dirty,

	/// <summary>
	/// Component has been disabled
	/// </summary>
	Disable,

	/// <summary>
	/// Component has been destroyed
	/// </summary>
	Destroy,

	/// <summary>
	/// GameObject actually deleted
	/// </summary>
	Term
}
