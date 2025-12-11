
using Sandbox;

namespace Editor.MovieMaker;

#nullable enable

partial class MovieEditor : EditorEvent.ISceneView
{
	void EditorEvent.ISceneView.DrawGizmos( Scene scene )
	{
		if ( scene != Session?.Player.Scene ) return;

		Session.DrawGizmos();
	}

	void EditorEvent.ISceneView.ShowContextMenu( EditorEvent.ShowContextMenuEvent ev )
	{
		if ( ev.Session.Scene != Session?.Player.Scene ) return;

		Session.ShowContextMenu( ev );
	}
}
