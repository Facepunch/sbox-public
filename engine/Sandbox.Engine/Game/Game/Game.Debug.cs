using System.Text.Json.Nodes;

namespace Sandbox;

public static partial class Game
{
	[ConCmd( "dump_scene" )]
	internal static void DumpScene()
	{
		var scene = ActiveScene;
		if ( !scene.IsValid() )
			return;

		using var sceneScope = scene.Push();

		var children = new JsonArray();
		var options = new GameObject.SerializeOptions();

		foreach ( var child in scene.Children )
		{
			var jso = child.Serialize( options );
			if ( jso is null ) continue;

			children.Add( jso );
		}

		if ( children.Count <= 0 )
			return;

		var json = children.ToJsonString();
		if ( string.IsNullOrWhiteSpace( json ) )
			return;

		NativeEngine.EngineGlobal.SDL_SetClipboardText( json );
		Log.Info( $"Copied {scene.Name} scene to clipboard, paste it into a new scene in the editor." );
	}
}
