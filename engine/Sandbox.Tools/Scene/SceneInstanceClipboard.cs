using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>Carries scene-local links with copied objects without exposing them as assets.</summary>
internal static class SceneInstanceClipboard
{
	/// <summary>Preserves the usual clipboard format when no instance links are needed.</summary>
	internal static string Write( Scene scene, IEnumerable<GameObject> selection, IEnumerable<JsonObject> objects )
	{
		var roots = selection.SelectMany( x => x.GetAllObjects( false ) ).Select( x => x.Id );
		var state = scene.ExportInstanceState( roots );
		if ( state.Instances.IsEmpty )
			return Json.Serialize( objects );

		return new JsonObject
		{
			["Scene"] = JsonValue.Create( scene.Id ),
			["GameObjects"] = new JsonArray( objects.Select( x => (JsonNode)x ).ToArray() ),
			["Instances"] = Json.ToNode( state )
		}.ToJsonString();
	}

	/// <summary>Reads ordinary objects or an instance-aware scene clipboard package.</summary>
	internal static (JsonObject[] Objects, SceneInstanceState Instances) Read( Scene scene, string text )
	{
		var node = JsonNode.Parse( text );
		var objects = ReadObjects( node );
		if ( node is not JsonObject package )
			return (objects, SceneInstanceState.Empty);

		var state = Json.FromNode<SceneInstanceState>( package["Instances"] );
		if ( package["Scene"]?.GetValue<Guid>() != scene.Id )
			state = Scene.ForkInstanceSources( state );
		return (objects, state);
	}

	/// <summary>Reads expanded objects without importing authoring links, for consumers such as Hammer.</summary>
	internal static JsonObject[] ReadObjects( string text ) => ReadObjects( JsonNode.Parse( text ) );

	private static JsonObject[] ReadObjects( JsonNode node )
	{
		var objects = node as JsonArray ?? (node as JsonObject)?["GameObjects"] as JsonArray
			?? throw new JsonException( "The clipboard does not contain scene objects." );
		return objects.Select( x => x as JsonObject ?? throw new JsonException( "The clipboard contains an invalid scene object." ) ).ToArray();
	}

	/// <summary>Pastes fresh object identities while keeping copies of the same definition linked.</summary>
	internal static GameObject Paste( Scene scene, JsonObject template, SceneInstanceState instances )
	{
		var json = template.DeepClone().AsObject();
		var ids = SceneUtility.MakeIdGuidsUnique( json );
		using var blobs = BlobDataSerializer.LoadFrom( json );
		var go = scene.CreateObject();
		go.Deserialize( json );
		if ( !instances.Instances.IsEmpty )
			scene.ImportInstanceLinks( instances, ids );
		return go;
	}
}
