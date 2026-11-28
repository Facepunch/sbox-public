using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox.Internal;

internal class EventSystem : IDisposable
{
	readonly Logger log = new( "EventSystem" );

	/// <summary>
	/// A Type with events on it
	/// </summary>
	sealed class EventClass
	{
		public string Assembly;
		public Type Type;
		public List<EventAction> Events = new();
		public List<object> Targets = new();

		/// <summary>
		/// The type gained its first target or lost its last: the event lists its handlers are in look again at which of their
		/// actions have anything to run.
		/// </summary>
		internal void TargetsChanged()
		{
			foreach ( var e in Events )
			{
				e.Group.ActionsChanged();
			}
		}

		internal void Destroy()
		{
			foreach ( var action in Events )
			{
				action.Group.Remove( action );
				action.Group.ActionsChanged();
			}

			Events.Clear();
			Targets.Clear();
		}
	}

	/// <summary>
	/// A method on a type
	/// </summary>
	sealed class EventAction : IComparable<EventAction>
	{
		public int Priority;
		public EventClass Class;
		public EventList Group;

		readonly MethodInfo method;
		readonly int parameterCount;
		readonly bool copyArguments;
		readonly Action<object> directCall;
		internal bool IsStatic { get; }

		internal EventAction( MethodInfo method )
		{
			this.method = method;
			IsStatic = method.IsStatic;

			var parameters = method.GetParameters();
			parameterCount = parameters.Length;

			// Parameterless frame events can call the handler directly.
			if ( parameterCount == 0 && method.ReturnType == typeof( void ) && !method.ContainsGenericParameters && method.DeclaringType is { IsValueType: false, ContainsGenericParameters: false } )
			{
				try
				{
					if ( IsStatic )
					{
						var call = method.CreateDelegate<Action>();
						directCall = _ => call();
					}
					else
					{
						directCall = (Action<object>)DirectInstanceMethod.MakeGenericMethod( method.DeclaringType ).Invoke( null, [method] );
					}
				}
				catch ( Exception e ) when ( e is ArgumentException or TargetInvocationException { InnerException: ArgumentException } )
				{
					// Fall back to reflection when the delegate cannot bind.
				}
			}

			// Reflection writes back ref/out values and replaces missing optional arguments.
			copyArguments = parameters.Any( p => p.ParameterType.IsByRef || p.IsOptional || p.HasDefaultValue );
		}

		static readonly MethodInfo DirectInstanceMethod = typeof( EventAction ).GetMethod( nameof( DirectInstance ), BindingFlags.Static | BindingFlags.NonPublic );

		static Action<object> DirectInstance<T>( MethodInfo method ) where T : class
		{
			var call = method.CreateDelegate<Action<T>>();
			return target => call( (T)target );
		}

		public int CompareTo( EventAction other )
		{
			return other.Priority.CompareTo( Priority );
		}

		void Invoke( object target, object[] args )
		{
			if ( (args?.Length ?? 0) != parameterCount )
			{
				if ( args is null )
					throw new ArgumentException( $"{method.Name} expects {parameterCount} arguments but event passed none" );

				var expected = parameterCount == 0 ? "none" : parameterCount.ToString();
				throw new ArgumentException( $"Event passed {args.Length} arguments but {method.Name} expects {expected}" );
			}

			if ( directCall is not null )
			{
				try
				{
					directCall( target );
				}
				catch ( Exception ex )
				{
					// Match MethodInfo.Invoke so dispatch reports the original handler exception.
					throw new TargetInvocationException( ex );
				}

				return;
			}

			method.Invoke( target, copyArguments ? (object[])args.Clone() : args );
		}

		/// <summary>
		/// Run this event action, aggregating any exceptions.
		/// </summary>
		internal void Run( string name, object[] args )
		{
			if ( IsStatic )
			{
				try
				{
					Invoke( null, args );
					return;
				}
				catch ( Exception ex )
				{
					throw new TargetInvocationException( $"Error calling event '{name}' on '{Class.Type}'", ex.InnerException ?? ex );
				}
			}

			List<Exception> innerExceptions = null;

			for ( int i = Class.Targets.Count - 1; i >= 0; i-- )
			{
				var target = Class.Targets[i];

				try
				{
					Invoke( target, args );
				}
				catch ( Exception ex )
				{
					innerExceptions ??= new();
					innerExceptions.Add( new TargetInvocationException( $"Error calling event '{name}' on '{target}'", ex.InnerException ?? ex ) );
				}
			}

			ThrowExceptions( innerExceptions );
		}
	}

	/// <summary>
	/// A list of events, usually indexed by the event name
	/// </summary>
	sealed class EventList : List<EventAction>
	{
		EventAction[] runnable = [];
		bool runnableChanged = true;

		/// <summary>
		/// An action was added or removed, or a type gained its first target or lost its last.
		/// </summary>
		internal void ActionsChanged() => runnableChanged = true;

		/// <summary>
		/// The actions with anything to run, in the list's order: static ones, and instance ones whose type has targets. Most of
		/// the editor's per-frame handlers are on types with no instance alive (editors and widgets not open), so a frame doesn't
		/// visit them. Made again when the list or a type's targets change; a new array each time, so a run already going
		/// through the old one isn't changed under it.
		/// </summary>
		EventAction[] Runnable()
		{
			if ( !runnableChanged ) return runnable;
			runnableChanged = false;

			var list = new List<EventAction>( Count );
			foreach ( var action in this )
			{
				if ( action.IsStatic || action.Class.Targets.Count > 0 ) list.Add( action );
			}

			runnable = list.ToArray();
			return runnable;
		}

		/// <summary>
		/// Run this event list, aggregating any exceptions. A type that gets its first target while the list runs is first
		/// called the next time it runs, as a target added to a type that already has some is.
		/// </summary>
		internal void Run( string name, object[] args )
		{
			List<Exception> innerExceptions = null;
			var actions = Runnable();

			for ( int i = actions.Length - 1; i >= 0; i-- )
			{
				var action = actions[i];

				try
				{
					action.Run( name, args );
				}
				catch ( Exception ex )
				{
					innerExceptions ??= new();
					innerExceptions.Add( new TargetInvocationException( $"Error calling event '{name}' on '{action.Class.Type}'", ex.InnerException ?? ex ) );
				}
			}

			ThrowExceptions( innerExceptions );
		}
	}

	static void ThrowExceptions( List<Exception> exceptions )
	{
		if ( exceptions is { Count: 1 } )
			throw exceptions[0];

		if ( exceptions is { Count: > 1 } )
			throw new AggregateException( exceptions );
	}

	Dictionary<string, EventClass> Classes = new();
	Dictionary<string, EventList> Groups = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Listeners to restore when their assembly is registered again after hotload.
	/// </summary>
	HashSet<object> OrphanedInstances = new();

	string TypeKey( Type t )
	{
		return $"{t.Assembly.GetName().Name}/{t.FullName}";
	}

	void AddEventsForType( Type t, Type rootType = null )
	{
		EventClass classEvent = null;

		foreach ( var m in t.GetMethods( BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic ) )
		{
			if ( rootType != null && !m.IsPrivate ) continue;

			foreach ( var attr in m.GetCustomAttributes<EventAttribute>( true ) )
			{
				if ( classEvent == null )
				{
					var typeKey = TypeKey( rootType ?? t );
					if ( !Classes.TryGetValue( typeKey, out classEvent ) )
					{
						classEvent = new EventClass { Assembly = (rootType ?? t).Assembly.GetName().Name, Type = rootType ?? t };
						Classes[typeKey] = classEvent;
					}
				}

				log.Trace( $" {t} -> {m} - {attr}" );

				if ( !Groups.TryGetValue( attr.EventName, out var group ) )
				{
					group = new EventList();
					Groups.Add( attr.EventName, group );
				}

				var eventAction = new EventAction( m )
				{
					Priority = attr.Priority,
					Class = classEvent,
					Group = group
				};

				classEvent.Events.Add( eventAction );
				group.Add( eventAction );

				group.Sort();
				group.ActionsChanged();
			}
		}

		if ( t.BaseType is not null && t.BaseType != typeof( object ) )
			AddEventsForType( t.BaseType, rootType ?? t );
	}

	/// <summary>
	/// Remove the assembly's handlers and keep its listeners for hotload.
	/// </summary>
	internal void UnregisterAssembly( Assembly assm )
	{
		log.Trace( $"Removing {assm}" );

		var assemblyName = assm.GetName().Name;
		var classes = Classes.Where( x => x.Value.Assembly == assemblyName ).ToArray();
		log.Trace( $"Found {classes.Length} Types" );

		int instanceCount = 0;
		foreach ( var (key, eventClass) in classes )
		{
			instanceCount += eventClass.Targets.Count;
			OrphanedInstances.UnionWith( eventClass.Targets );
			eventClass.Destroy();
			Classes.Remove( key );
		}

		log.Trace( $"Found {instanceCount} Instances" );
	}

	/// <summary>
	/// Discover the assembly's handlers and restore its listeners after hotload.
	/// </summary>
	internal void RegisterAssembly( Assembly assm )
	{
		log.Trace( $"Registering Event System: [{assm}]" );

		if ( assm == null )
			return;

		var assemblyName = assm.GetName().Name;
		Assert.False( Classes.Values.Any( x => x.Assembly == assemblyName ), "Register Assembly - event already contains this dll!" );

		foreach ( var type in assm.GetTypes() )
		{
			AddEventsForType( type );
		}

		foreach ( var instance in OrphanedInstances.ToArray() )
		{
			// instance could have turned null because hotload lost the type
			if ( instance is null )
			{
				OrphanedInstances.Remove( instance );
				continue;
			}

			if ( instance.GetType().Assembly == assm )
			{
				log.Trace( $"Re-registering orphan: {instance}" );
				Register( instance );
				OrphanedInstances.Remove( instance );
				continue;
			}

			// If the assembly didn't match - then if the assembly has the same name
			// it could be a sign of trouble - because all the instances should have
			// been swapped to the new assembly type
			if ( instance.GetType().Assembly.GetName().Name == assemblyName )
			{
				log.Warning( $"instance {instance} is from {instance.GetType().Assembly} - but doesn't match {assm} - should this have been hotload swapped?" );
			}
		}
	}

	internal void Run( string name ) => Run( name, null );

	internal void Run( string name, params object[] args )
	{
		try
		{
			RunHandlers( name, args );
		}
		catch ( Exception ex )
		{
			log.Error( ex );
		}
	}

	internal void RunHandlers( string name, params object[] args ) => Groups.GetValueOrDefault( name )?.Run( name, args );

	readonly WeakHashSet<object> AllTargets = new();

	internal void RunInterface<T>( Action<T> action )
	{
		// A snapshot: registering or unregistering makes a new array rather than changing this one
		foreach ( var target in AllTargets.GetLiveItems() )
		{
			if ( target is T listener )
				action( listener );
		}
	}

	internal void Register( object obj )
	{
		AllTargets.Add( obj );

		if ( !Classes.TryGetValue( TypeKey( obj.GetType() ), out var type ) )
			return;

		type.Targets.Add( obj );
		if ( type.Targets.Count == 1 ) type.TargetsChanged();
	}

	internal void Unregister( object obj )
	{
		AllTargets.Remove( obj );

		if ( OrphanedInstances.Remove( obj ) )
			return;

		if ( !Classes.TryGetValue( TypeKey( obj.GetType() ), out var type ) )
			return;

		if ( type.Targets.Remove( obj ) && type.Targets.Count == 0 ) type.TargetsChanged();
	}

	/// <summary>
	/// Release the registered handlers and pending hotload listeners.
	/// </summary>
	public void Dispose()
	{
		OrphanedInstances.Clear();
		Classes.Clear();
		Classes = null;

		Groups.Clear();
		Groups = null;
	}
}

#nullable enable

internal sealed class WeakHashSet<T> : IEnumerable<T>
	where T : class
{
	private readonly ConditionalWeakTable<T, object?> _weakTable = new();

	private int _lastGcCount;

	public void Add( T item )
	{
		ClearCaches();
		_weakTable.AddOrUpdate( item, null );
	}

	public bool Remove( T item )
	{
		ClearCaches();
		return _weakTable.Remove( item );
	}

	#region IEnumerable

	private readonly object _lock = new();
	private WeakReference<T[]>? _liveItemCache;

	private void ClearCaches() => _liveItemCache = null;

	public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)GetLiveItems()).GetEnumerator();

	/// <summary>
	/// The live items, shared and cached - don't modify it. Iterating the array directly doesn't allocate,
	/// unlike going through <see cref="GetEnumerator"/>.
	/// </summary>
	public T[] GetLiveItems()
	{
		// Enumerating using _weakTable.Select( x => x.Key ) is quite slow, so we cache it.

		// The cache is invalidated:
		//   1) Explicitly when items are added / removed
		//   2) Implicitly when a GC happens

		var gcCount = GC.CollectionCount( 0 );

		if ( _lastGcCount == gcCount && _liveItemCache?.TryGetTarget( out var items ) is true )
		{
			return items;
		}

		lock ( _lock )
		{
			_lastGcCount = gcCount;
			_liveItemCache = new WeakReference<T[]>( items = _weakTable.Select( x => x.Key ).ToArray() );
		}

		return items;
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	#endregion
}
