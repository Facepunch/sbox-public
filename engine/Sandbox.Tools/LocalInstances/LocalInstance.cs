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
	/// True while a docked client can be added: we're hosting, or we could start hosting
	/// (not connected anywhere as a client).
	/// </summary>
	public static bool CanAdd => EditorUtility.Network.Hosting || !EditorUtility.Network.Active;

	/// <summary>
	/// Add a docked in-process client as a tab next to the scene tabs. If the editor isn't
	/// hosting yet this starts hosting first, entering play mode if needed.
	/// </summary>
	public static void AddDockedClient()
	{
		if ( Sandbox.VR.VRSystem.IsActive )
		{
			Log.Warning( "VR is active - docked clients report as non-VR players and don't receive VR input" );
		}

		if ( !EditorUtility.Network.Hosting )
		{
			if ( EditorUtility.Network.Active )
			{
				Log.Warning( "Can't add a docked client - the editor is connected as a client, not hosting" );
				return;
			}

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
}
