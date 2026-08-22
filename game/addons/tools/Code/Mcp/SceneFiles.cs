namespace Editor.Mcp;

/// <summary>
/// Opening and switching scene tabs, so play_start runs the scene you meant.
/// </summary>
[McpToolset( "scenefile", "Opening, switching and closing scene tabs" )]
public static partial class SceneFileTools
{
	/// <summary>
	/// Open a scene from the asset system and make it the active tab. Already open scenes are just
	/// brought to the front. Play mode has to be stopped first - play_start then runs this scene.
	/// </summary>
	/// <param name="path">Scene asset path, like "scenes/menu-main.scene". asset_search finds them.</param>
	[McpTool( "open_scene" )]
	public static OpenedScene OpenScene( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new Exception( "Give a scene path - asset_search with type:scene lists them" );

		if ( Game.IsPlaying )
			throw new Exception( "Playing - play_stop first, or the tab you open won't be what plays" );

		var asset = AssetSystem.FindByPath( path );
		if ( asset is null )
			throw new Exception( $"No asset at '{path}' - asset_search with type:scene lists them" );

		if ( asset.LoadResource<SceneFile>() is not { } scene )
			throw new Exception( $"'{path}' isn't a scene" );

		EditorScene.OpenScene( scene );

		return Current();
	}

	/// <summary>
	/// Switch to an already open scene tab, by name or resource path. list_scenes says what's open.
	/// </summary>
	/// <param name="scene">Scene name or resource path, as list_scenes reports it.</param>
	[McpTool( "switch_scene" )]
	public static OpenedScene SwitchScene( string scene )
	{
		var session = Find( scene );

		if ( session is null )
			throw new Exception( $"'{scene}' isn't open - list_scenes says what is, open_scene opens more" );

		session.MakeActive();

		return Current();
	}

	/// <summary>
	/// Close a scene tab. Refuses to throw away unsaved work unless you say so.
	/// </summary>
	/// <param name="scene">Scene name or resource path. Empty for the active one.</param>
	/// <param name="discardChanges">Close even if the scene has unsaved changes.</param>
	[McpTool( "close_scene" )]
	public static OpenedScene CloseScene( string scene = null, bool discardChanges = false )
	{
		var session = string.IsNullOrWhiteSpace( scene ) ? SceneEditorSession.Active : Find( scene );

		if ( session is null )
			throw new Exception( $"'{scene}' isn't open - list_scenes says what is" );

		if ( session.HasUnsavedChanges && !discardChanges )
			throw new Exception( $"'{session.Scene?.Name}' has unsaved changes - save_scene first, or pass discardChanges" );

		session.Destroy();

		return Current();
	}

	static SceneEditorSession Find( string scene )
	{
		if ( string.IsNullOrWhiteSpace( scene ) ) return null;

		return SceneEditorSession.All.FirstOrDefault( x =>
			string.Equals( x.Scene?.Name, scene, StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( x.Scene?.Source?.ResourcePath, scene, StringComparison.OrdinalIgnoreCase ) );
	}

	static OpenedScene Current() => new()
	{
		Active = SceneEditorSession.Active?.Scene?.Name,
		ActivePath = SceneEditorSession.Active?.Scene?.Source?.ResourcePath,
		Open = SceneEditorSession.All.Select( x => x.Scene?.Name ).ToArray()
	};

	/// <summary>What's active after the change, and everything open.</summary>
	public class OpenedScene
	{
		public string Active { get; set; }
		public string ActivePath { get; set; }
		public string[] Open { get; set; }
	}
}
