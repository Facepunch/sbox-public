using Sandbox;
using System;

namespace Editor;

/// <summary>
/// Editor entry point for docked in-process clients: "Scene / Client 1 / Client 2 / ..." tabs,
/// each a genuine multiplayer client running inside the editor process. Because everything is
/// shared with the host (assemblies, resources, mounts) a client joins near-instantly - the
/// same seamless experience as pressing play, made multi-tenant.
/// </summary>
public static class LocalInstance
{
	/// <summary>
	/// True while a docked client can be added: we're hosting, or we could start hosting
	/// (not connected anywhere as a client).
	/// </summary>
	public static bool CanAdd => EditorUtility.Network.Hosting || !EditorUtility.Network.Active;

	/// <summary>
	/// Add a docked in-process client as a tab next to the scene tabs. If the editor isn't
	/// hosting yet this starts hosting first (entering play mode if needed) - one click from
	/// any state, exactly like pressing play.
	/// </summary>
	public static void AddDockedClient()
	{
		if ( Sandbox.VR.VRSystem.IsActive )
		{
			// VR tracking and input are process-global - docked clients can't isolate them.
			Log.Warning( "VR is active - docked clients report as non-VR players and don't receive VR input" );
		}

		if ( !EditorUtility.Network.Hosting )
		{
			if ( EditorUtility.Network.Active )
			{
				Log.Warning( "Can't add a docked client - the editor is connected as a client, not hosting" );
				return;
			}

			// Enters play mode if needed and creates the lobby. The host network system comes
			// up synchronously; the tab below attaches itself the moment it's ready.
			EditorUtility.Network.StartHosting();
		}

		var widget = new ClientInstanceWidget
		{
			Name = $"ClientInstance:{Guid.NewGuid():N}",
			WindowTitle = "Client",
		};
		widget.Visible = true;

		var dock = EditorWindow.DockManager.CreateDockWidget( widget.Name, "connected_tv", widget );
		dock.WindowTitle = "Client";

		// Dock as a tab in the same area as the scene tabs.
		var sibling = SceneEditorSession.Active?.SceneDock;
		var siblingDock = sibling.IsValid() ? EditorWindow.DockManager.FindDockWidget( sibling ) : null;

		if ( siblingDock is not null )
		{
			EditorWindow.DockManager.AddDock( dock, siblingDock );
		}
		else
		{
			EditorWindow.DockManager.AddDock( dock, DockArea.Right );
		}

		dock.SetAsCurrentTab();
	}

	//
	// CTRL+F1..F12 gives input focus to docked client 1..12 and raises its tab;
	// pressing it again hands input back to the host. Works from the editor and
	// while playing (engine function keys route into the same shortcuts).
	//

	[Shortcut( "localinstances.focus1", "CTRL+F1", ShortcutType.Window )] static void FocusClient1() => ClientInstanceWidget.FocusInstance( 1 );
	[Shortcut( "localinstances.focus2", "CTRL+F2", ShortcutType.Window )] static void FocusClient2() => ClientInstanceWidget.FocusInstance( 2 );
	[Shortcut( "localinstances.focus3", "CTRL+F3", ShortcutType.Window )] static void FocusClient3() => ClientInstanceWidget.FocusInstance( 3 );
	[Shortcut( "localinstances.focus4", "CTRL+F4", ShortcutType.Window )] static void FocusClient4() => ClientInstanceWidget.FocusInstance( 4 );
	[Shortcut( "localinstances.focus5", "CTRL+F5", ShortcutType.Window )] static void FocusClient5() => ClientInstanceWidget.FocusInstance( 5 );
	[Shortcut( "localinstances.focus6", "CTRL+F6", ShortcutType.Window )] static void FocusClient6() => ClientInstanceWidget.FocusInstance( 6 );
	[Shortcut( "localinstances.focus7", "CTRL+F7", ShortcutType.Window )] static void FocusClient7() => ClientInstanceWidget.FocusInstance( 7 );
	[Shortcut( "localinstances.focus8", "CTRL+F8", ShortcutType.Window )] static void FocusClient8() => ClientInstanceWidget.FocusInstance( 8 );
	[Shortcut( "localinstances.focus9", "CTRL+F9", ShortcutType.Window )] static void FocusClient9() => ClientInstanceWidget.FocusInstance( 9 );
	[Shortcut( "localinstances.focus10", "CTRL+F10", ShortcutType.Window )] static void FocusClient10() => ClientInstanceWidget.FocusInstance( 10 );
	[Shortcut( "localinstances.focus11", "CTRL+F11", ShortcutType.Window )] static void FocusClient11() => ClientInstanceWidget.FocusInstance( 11 );
	[Shortcut( "localinstances.focus12", "CTRL+F12", ShortcutType.Window )] static void FocusClient12() => ClientInstanceWidget.FocusInstance( 12 );
}
