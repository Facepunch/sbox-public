namespace Editor;

public static partial class EditorUtility
{
	/// <summary>Converts a VMAP asset into an editable scene at the given path.</summary>
	public static async Task<SceneFile> ImportVmapAsync( Asset source, string scenePath )
	{
		var result = await MapEditor.MapSceneImporter.ImportAsync( source, scenePath );
		foreach ( var warning in result.Warnings ) Log.Warning( warning );
		return result.Scene;
	}
}
