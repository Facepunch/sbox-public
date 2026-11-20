using Editor.MapEditor;
using System.ComponentModel;

namespace Editor.Mcp;

internal static partial class HammerTools
{
	[McpTool( "hammer_convert_to_scene" )]
	[Description( "Convert a VMAP into a new .scene containing editable scene meshes. Local Hammer instances remain linked scene instances; external VMAP prefabs become .prefab assets beside the scene. Preserves the source map. Reports unsupported features. The output filename must not already exist." )]
	public static async Task<object> ConvertToScene(
		[Description( "Scene filename, absolute or relative to the current project's assets." )] string path,
		[Description( "VMAP asset path. Omit to convert the active Hammer document." )] string source = null )
	{
		var result = string.IsNullOrEmpty( source )
			? await MapSceneImporter.ImportAsync( ActiveMap(), path )
			: await MapSceneImporter.ImportAsync( AssetSystem.FindByPath( source ) ?? throw new System.IO.FileNotFoundException( "VMAP asset not found.", source ), path );
		return new { Scene = result.Scene.ResourcePath, result.Prefabs, result.Warnings };
	}
}
