using System;
using System.Reflection;

namespace Editor;

public static partial class EditorEvent
{
	/// <summary>
	/// Called every frame for tools
	/// </summary>
	public class FrameAttribute : EventAttribute { public FrameAttribute() : base( "tool.frame" ) { } }

	public class HotloadAttribute : EventAttribute { public HotloadAttribute() : base( "hotloaded" ) { } }


	static Sandbox.Internal.EventSystem eventManager;

	internal static void Init()
	{
		eventManager?.Dispose();
		eventManager = new Sandbox.Internal.EventSystem();
	}


	/// <summary>
	/// Register an assembly. If old assembly is valid, we try to remove all of the old event hooks
	/// from this assembly, while retaining a list of objects.
	/// </summary>
	internal static void UnregisterAssembly( Assembly outgoing )
	{
		eventManager.UnregisterAssembly( outgoing );
	}

	/// <summary>
	/// Register an assembly. If old assembly is valid, we try to remove all of the old event hooks
	/// from this assembly, while retaining a list of objects.
	/// </summary>
	internal static void RegisterAssembly( Assembly incoming )
	{
		eventManager.RegisterAssembly( incoming );
	}

	/// <summary>
	/// Register an object, start receiving events
	/// </summary>
	public static void Register( object obj ) => eventManager?.Register( obj );

	/// <summary>
	/// Unregister an object, stop receiving events
	/// </summary>
	public static void Unregister( object obj ) => eventManager?.Unregister( obj );

	/// <summary>
	/// Run an event.
	/// </summary>
	public static void Run( string name ) => eventManager?.Run( name );

	/// <summary>
	/// Run an event with an argument of arbitrary type.
	/// </summary>
	/// <typeparam name="T">Arbitrary type for the argument.</typeparam>
	/// <param name="name">Name of the event to run.</param>
	/// <param name="arg0">Argument to pass down to event handlers.</param>
	public static void Run<T>( string name, T arg0 )
	{
		eventManager?.Run( name, arg0 );
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
		eventManager?.Run( name, arg0, arg1 );
	}

	/// <summary>
	/// Run an interface based event
	/// </summary>
	public static void RunInterface<T>( Action<T> action )
	{
		eventManager?.RunInterface<T>( action );
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
		eventManager?.Run( name, arg0, arg1, arg2 );
	}

	public interface IEventListener;

	public interface ISceneEdited : IEventListener
	{
		/// <summary>
		/// Called when a property on a <see cref="GameObject"/> is about to be edited, so the old value can be inspected.
		/// </summary>
		void GameObjectPreEdited( GameObject go, string propertyName ) { }

		/// <summary>
		/// Called when a <see cref="GameObject"/> has been edited, so the new value can be inspected.
		/// </summary>
		void GameObjectEdited( GameObject go, string propertyName );

		/// <summary>
		/// Called when a property on a <see cref="Component"/> is about to be edited, so the old value can be inspected.
		/// </summary>
		void ComponentPreEdited( Component cmp, string propertyName ) { }

		/// <summary>
		/// Called when a <see cref="Component"/> has been edited, so the new value can be inspected.
		/// </summary>
		void ComponentEdited( Component cmp, string propertyName );
	}

	/// <summary>
	/// Event args for <see cref="ISceneView.ShowContextMenu"/> events.
	/// </summary>
	/// <param name="Session">Scene editor session that the context menu is being opened for.</param>
	/// <param name="Menu">Context menu being opened. Feel free to add options to it in your handler.</param>
	/// <param name="CursorRay">Cursor ray when right-click was pressed.</param>
	/// <param name="Trace">Trace result if we hit an object in the scene when right-clicking.</param>
	public sealed record ShowContextMenuEvent( SceneEditorSession Session, Menu Menu, Ray CursorRay, SceneTraceResult? Trace );

	/// <summary>
	/// Allows tools to inject behaviour in the scene editor.
	/// </summary>
	public interface ISceneView : IEventListener
	{
		/// <summary>
		/// Called when a scene editor viewport is drawing gizmos.
		/// </summary>
		/// <param name="scene">Scene that gizmos are being drawn for.</param>
		void DrawGizmos( Scene scene ) { }

		/// <summary>
		/// Called when a scene editor viewport wants to show a context menu.
		/// </summary>
		/// <param name="ev">Event arguments describing what the context menu was opened on.</param>
		void ShowContextMenu( ShowContextMenuEvent ev ) { }
	}

	/// <summary>
	/// Event args for <see cref="IHierarchyView.ShowContextMenu"/> events.
	/// </summary>
	/// <param name="GameObject">The GameObject being right-clicked.</param>
	/// <param name="Menu">Context menu being opened. Feel free to add options to it in your handler.</param>
	public sealed record GameObjectContextMenuEvent(
		GameObject GameObject,
		Menu Menu
	);

	/// <summary>Allows tools to inject behaviour in the hierarchy editor.</summary>
	public interface IHierarchyView : IEventListener
	{
		/// <summary>
		/// Called when the hierarchy viewport wants to show a context menu for a GameObject.
		/// </summary>
		void ShowContextMenu( GameObjectContextMenuEvent ev ) { }
	}
}
