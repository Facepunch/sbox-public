using System.Text.Json;
using System.Text.Json.Nodes;

public partial class DuplicatorTool
{
	/// <summary>
	/// Render duplicator Json to a bitmap
	/// </summary>
	public static void RenderIconToBitmap( string duplicatorJson, Bitmap bitmap )
	{
		var jsonObject = Json.ParseToJsonObject( duplicatorJson );
		bitmap.Clear( Color.Transparent );

		Transform dest = new();

		var scene = Scene.CreateEditorScene();
		using ( scene.Push() )
		{
			var root = new GameObject();
			foreach ( var entry in jsonObject["Objects"] as JsonArray )
			{
				if ( entry is JsonObject obj )
				{
					var pos = entry["Position"]?.Deserialize<Vector3>() ?? default;
					var rot = entry["Rotation"]?.Deserialize<Rotation>() ?? Rotation.Identity;

					var world = dest.ToWorld( new Transform( pos, rot ) );

					var go = new GameObject( false );
					go.Deserialize( obj, new GameObject.DeserializeOptions { TransformOverride = world } );
					go.NetworkSpawn( true, null );

					go.Parent = root;
				}
			}

			foreach ( var block in root.GetComponentsInChildren<BuilderProp>( true ) )
				block.Rebuild();

			SceneUtility.RenderGameObjectToBitmap( root, bitmap );
			scene.Destroy();
		}
	}

}
