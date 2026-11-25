using Sandbox.Diagnostics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox.Internal;

internal class EventSystem : IDisposable
{
	Logger log = new Logger( "EventSystem" );

	public EventSystem()
	{
	}

	/// <summary>
	/// A Type with events on it
	/// </summary>
	public class EventClass
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

		public void Destroy()
		{
			Assembly = null;
			Type = null;
			Targets.Clear();

			foreach ( var e in Events.ToArray() )
			{
				e.Destroy();
			}

			Assert.AreEqual( 0, Events.Count() );
		}
	}

	/// <summary>
	/// A method on a type
	/// </summary>
	public class EventAction : IComparable<EventAction>
	{
		public int Priority;
		public EventClass Class;
		public EventList Group;
		public EventDelegate Delegate;
		public bool IsStatic;

		public int CompareTo( EventAction other )
		{
			return other.Priority.CompareTo( Priority );
		}

		public void Destroy()
		{
			Class.Events.Remove( this );
			Group.Remove( this );
			Group.ActionsChanged();
		}

		/// <summary>
		/// Run this event action, aggregating any exceptions.
		/// </summary>
		internal void Run( string name, object[] args = null )
		{
			if ( IsStatic )
			{
				try
				{
					Delegate( null, args );
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
					Delegate( target, args );
				}
				catch ( Exception ex )
				{
					innerExceptions ??= new();
					innerExceptions.Add( new TargetInvocationException( $"Error calling event '{name}' on '{target}'", ex.InnerException ?? ex ) );
				}
			}

			switch ( innerExceptions )
			{
				case { Count: 1 }:
					throw innerExceptions[0];
				case { Count: > 1 }:
					throw new AggregateException( innerExceptions.ToArray() );
			}
		}
	}

	/// <summary>
	/// A list of events, usually indexed by the event name
	/// </summary>
	public class EventList : List<EventAction>
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
		internal void Run( string name, object[] args = null )
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

			switch ( innerExceptions )
			{
				case { Count: 1 }:
					throw innerExceptions[0];
				case { Count: > 1 }:
					throw new AggregateException( innerExceptions.ToArray() );
			}
		}
	}

	public delegate void EventDelegate( object root, object[] parms );

	Dictionary<string, EventClass> Classes = new();
	Dictionary<string, EventList> Groups = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Instances that have had their assembly removed. We keep them around becuase the
	/// assembly might be re-registered.
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

				var group = GetGroup( attr.EventName, true );

				var eventAction = new EventAction
				{
					Priority = attr.Priority,
					Class = classEvent,
					Group = group,
					Delegate = BuildDelegate( m ),
					IsStatic = m.IsStatic
				};


				classEvent.Events.Add( eventAction );
				eventAction.Group.Add( eventAction );

				group.Sort();
				group.ActionsChanged();
			}
		}

		if ( t.BaseType is not null && t.BaseType != typeof( object ) )
			AddEventsForType( t.BaseType, rootType ?? t );
	}

	EventDelegate BuildDelegate( MethodInfo info )
	{
		var parameters = info.GetParameters();

		// Events without arguments run every frame, so call them directly rather than through reflection
		if ( parameters.Length == 0 && info.ReturnType == typeof( void ) && !info.ContainsGenericParameters && info.DeclaringType is { IsValueType: false, ContainsGenericParameters: false } )
		{
			try
			{
				if ( info.IsStatic )
					return Direct( info.CreateDelegate<Action>() );

				return (EventDelegate)DirectInstanceMethod.MakeGenericMethod( info.DeclaringType ).Invoke( null, [info] );
			}
			catch ( Exception e ) when ( e is ArgumentException or TargetInvocationException { InnerException: ArgumentException } )
			{
				// Can't bind it, reflection still works
			}
		}

		// TODO - handle arguments
		object[] args = default;
		if ( parameters.Length > 0 )
		{
			args = new object[parameters.Length];
		}

		EventDelegate d = ( o, p ) =>
		{
			if ( args != null )
			{
				if ( p == null )
					throw new ArgumentException( $"{info.Name} expects {args.Length} arguments but event passed none" );

				if ( p.Length != args.Length )
					throw new ArgumentException( $"Event passed {p.Length} arguments but {info.Name} expects {args.Length}" );

				for ( int i = 0; i < args.Length; i++ )
				{
					args[i] = p[i];
				}
			}
			else
			{
				if ( p != null && p.Length != 0 )
					throw new ArgumentException( $"Event passed {p.Length} arguments but {info.Name} expects none" );
			}

			info?.Invoke( o, args );
		};

		return d;
	}

	static readonly MethodInfo DirectInstanceMethod = typeof( EventSystem ).GetMethod( nameof( DirectInstance ), BindingFlags.Static | BindingFlags.NonPublic );

	static EventDelegate DirectInstance<T>( MethodInfo info ) where T : class
	{
		var call = info.CreateDelegate<Action<T>>();
		return Direct( call, info.Name );
	}

	static EventDelegate Direct<T>( Action<T> call, string name ) where T : class => ( o, p ) =>
	{
		if ( p != null && p.Length != 0 )
			throw new ArgumentException( $"Event passed {p.Length} arguments but {name} expects none" );

		// Wrapped like MethodInfo.Invoke wraps it, as the callers unwrap it
		try { call( (T)o ); }
		catch ( Exception e ) { throw new TargetInvocationException( e ); }
	};

	static EventDelegate Direct( Action call ) => ( o, p ) =>
	{
		if ( p != null && p.Length != 0 )
			throw new ArgumentException( $"Event passed {p.Length} arguments but {call.Method.Name} expects none" );

		try { call(); }
		catch ( Exception e ) { throw new TargetInvocationException( e ); }
	};

	internal EventList GetGroup( string name, bool create )
	{
		if ( Groups.TryGetValue( name, out var group ) )
			return group;

		if ( !create )
			return null;

		group = new EventList();
		Groups.Add( name, group );
		return group;
	}

	/// <summary>
	/// Register an assembly. If old assembly is valid, we try to remove all of the old event hooks
	/// from this assembly, while retaining a list of objects.
	/// </summary>
	internal void UnregisterAssembly( Assembly assm )
	{
		log.Trace( $"Removing {assm}" );

		// collect instances
		string assemblyName = assm.GetName().Name;
		Dictionary<string, EventClass> classes = Classes.Where( x => x.Value.Assembly == assemblyName ).ToDictionary( x => x.Key, x => x.Value );

		log.Trace( $"Found {classes.Count()} Types" );

		object[] instances = classes.SelectMany( x => x.Value.Targets ).ToArray();

		foreach ( var instance in instances )
		{
			OrphanedInstances.Add( instance );
		}

		log.Trace( $"Found {instances.Count()} Instances" );

		foreach ( var clss in classes )
		{
			clss.Value.Destroy();
			Classes.Remove( clss.Key );
		}
	}

	/// <summary>
	/// Register an assembly. If old assembly is valid, we try to remove all of the old event hooks
	/// from this assembly, while retaining a list of objects.
	/// </summary>
	internal void RegisterAssembly( Assembly assm )
	{
		log.Trace( $"Registering Event System: [{assm}]" );

		if ( assm == null )
			return;

		var assemblyName = assm.GetName().Name;
		EventClass[] classes = Classes.Values.Where( x => x.Assembly == assemblyName ).ToArray();

		Assert.AreEqual( 0, classes.Length, "Register Assembly - event already contains this dll!" );

		var newTypes = assm.GetTypes();

		foreach ( var type in newTypes )
		{
			Classes.TryGetValue( TypeKey( type ), out EventClass classEvent );
			Assert.IsNull( classEvent );

			AddEventsForType( type );
		}

		foreach ( var instance in OrphanedInstances.ToArray() )
		{
			// instance could have turned null because hotload lost the type
			if ( instance is null )
			{
				OrphanedInstances.Remove( instance );
				return;
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

	internal void Run( string v )
	{
		try
		{
			GetGroup( v, false )?.Run( v );
		}
		catch ( Exception ex )
		{
			log.Error( ex );
		}
	}

	internal void Run( string v, params object[] list )
	{
		try
		{
			GetGroup( v, false )?.Run( v, list );
		}
		catch ( Exception ex )
		{
			log.Error( ex );
		}
	}

	WeakHashSet<object> AllTargets = new WeakHashSet<object>();

	internal void RunInterface<T>( Action<T> t )
	{
		// A snapshot: registering or unregistering makes a new array rather than changing this one
		foreach ( var e in AllTargets.GetLiveItems() )
		{
			if ( e is T target ) t.Invoke( target );
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

	public void Dispose()
	{
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
	// TODO: We're only accessing items in this with OfType<T2>, should we index by type?
	// TODO: T is always object, can we simplify?

	private readonly ConditionalWeakTable<T, object?> _weakTable = new();

	private int _lastGcCount;

	public void Add( T item )
	{
		ClearCaches();
		_weakTable.AddOrUpdate( item, null );
	}

	public bool Contains( T item ) => _weakTable.TryGetValue( item, out _ );

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
