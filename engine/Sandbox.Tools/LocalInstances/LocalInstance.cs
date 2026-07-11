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
		widget.SetWindowIcon( "connected_tv" );

		widget.Parent = EditorWindow;
		widget.Visible = true;

		// Dock as a tab in the same area as the scene tabs.
		var sibling = SceneEditorSession.Active?.SceneDock;
		EditorWindow.DockManager.AddDock( sibling, widget, sibling.IsValid() ? DockArea.Inside : DockArea.Right );
		EditorWindow.DockManager.RaiseDock( widget );
	}
}
