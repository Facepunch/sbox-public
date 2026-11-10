using Sandbox.Engine;
using System.Reflection;

namespace Sandbox;

/// <summary>
/// The event system.
/// </summary>
internal static partial class Event
{
	public static Internal.EventSystem EventSystem => GlobalContext.Current.EventSystem;

	/// <summary>
	/// Register an assembly. If old assembly is valid, we try to remove all of the old event hooks
	/// from this assembly, while retaining a list of objects.
	/// </summary>
	internal static void UnregisterAssembly( Assembly outgoing )
	{
		EventSystem.UnregisterAssembly( outgoing );
	}

	/// <summary>
	/// Register an assembly. If old assembly is valid, we try to remove all of the old event hooks
	/// from this assembly, while retaining a list of objects.
	/// </summary>
	internal static void RegisterAssembly( Assembly incoming )
	{
		EventSystem.RegisterAssembly( incoming );
	}

	/// <summary>
	/// Register an object to start receiving events
	/// </summary>
	public static void Register( object obj )
	{
		EventSystem?.Register( obj );
	}

	/// <summary>
	/// Unregister an object, stop reviving events
	/// </summary>
	public static void Unregister( object obj )
	{
		EventSystem?.Unregister( obj );
	}

	/// <summary>
	/// Run an event.
	/// </summary>
	public static void Run( string name )
	{
		EventSystem?.Run( name );
	}

	/// <summary>
	/// Run an event with an argument of arbitrary type.
	/// </summary>
	/// <typeparam name="T">Arbitrary type for the argument.</typeparam>
	/// <param name="name">Name of the event to run.</param>
	/// <param name="arg0">Argument to pass down to event handlers.</param>
	public static void Run<T>( string name, T arg0 )
	{
		EventSystem?.Run( name, arg0 );
	}

	/// <summary>
	/// Run an event with 2 arguments of arbitrary type.
	/// </summary>
	/// <typeparam name="T">Arbitrary type for the first argument.</typeparam>
	/// <typeparam name="U">Arbitrary type for the second argument.</typeparam>
	/// <param name="name">Name of the event to run.</param>
	/// <param name="arg0">First argument to pass down to event handlers.</param>
	/// <param name="arg1">Second argument to pass down to event handlers.</param>
	public static void Run<T, U>( string name, T arg0, U arg1 )
	{
		EventSystem?.Run( name, arg0, arg1 );
	}

	/// <summary>
	/// Run an event with 3 arguments of arbitrary type.
	/// </summary>
	/// <typeparam name="T">Arbitrary type for the first argument.</typeparam>
	/// <typeparam name="U">Arbitrary type for the second argument.</typeparam>
	/// <typeparam name="V">Arbitrary type for the third argument.</typeparam>
	/// <param name="name">Name of the event to run.</param>
	/// <param name="arg0">First argument to pass down to event handlers.</param>
	/// <param name="arg1">Second argument to pass down to event handlers.</param>
	/// <param name="arg2">Third argument to pass down to event handlers.</param>
	public static void Run<T, U, V>( string name, T arg0, U arg1, V arg2 )
	{
		EventSystem?.Run( name, arg0, arg1, arg2 );
	}
}
