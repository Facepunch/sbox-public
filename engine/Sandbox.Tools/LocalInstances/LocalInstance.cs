using Sandbox;
using System;

namespace Editor;

/// <summary>
/// Editor entry point for docked in-process clients: "Client N" tabs, each a genuine
/// multiplayer client running inside the editor process.
/// </summary>
public static class LocalInstance
{
	/// <summary>
	/// Add a docked in-process client as a tab next to the scene tabs. Only valid while hosting.
	/// </summary>
	public static void AddDockedClient()
	{
		if ( !EditorUtility.Network.Hosting )
		{
			Log.Warning( "Can't add a docked client - the editor is not hosting" );
			return;
		}

		if ( Sandbox.VR.VRSystem.IsActive )
		{
			Log.Warning( "VR is active - docked clients report as non-VR players and don't receive VR input" );
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
}
