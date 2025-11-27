using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>
/// Component that stores layer data for a scene.
/// This component is automatically added to the scene root and manages
/// serialization of layer definitions.
/// </summary>
[Title( "Scene Layers" )]
[Category( "Scene" )]
[Icon( "layers" )]
[Expose]
[ComponentFlags( ComponentFlags.Hidden | ComponentFlags.NotNetworked )]
public sealed class SceneLayerData : Component, ExecuteInEditor
{
	/// <summary>
	/// JSON string containing serialized layer data.
	/// This is used for serialization/deserialization with the scene.
	/// </summary>
	[Property, Hide]
	public string LayerDataJson
	{
		get => SerializeLayers();
		set => DeserializeLayers( value );
	}

	/// <summary>
	/// The layer manager for this scene.
	/// </summary>
	private LayerManager Manager => LayerManager.Get( Scene );

	protected override void OnEnabled()
	{
		base.OnEnabled();

		// Register with the layer manager
		var manager = Manager;
		if ( manager is not null )
		{
			manager.OnLayersModified += MarkDirty;
		}
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		var manager = Manager;
		if ( manager is not null )
		{
			manager.OnLayersModified -= MarkDirty;
		}
	}

	private void MarkDirty()
	{
		// This triggers the scene to mark itself as having unsaved changes
		// when layers are modified
	}

	private string SerializeLayers()
	{
		var manager = Manager;
		if ( manager is null ) return null;

		var json = manager.Serialize();
		return json.ToJsonString();
	}

	private void DeserializeLayers( string jsonString )
	{
		if ( string.IsNullOrEmpty( jsonString ) ) return;

		var manager = Manager;
		if ( manager is null ) return;

		try
		{
			var json = JsonNode.Parse( jsonString ) as JsonObject;
			if ( json is not null )
			{
				manager.Deserialize( json );
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"Failed to deserialize layer data: {e.Message}" );
		}
	}

	protected override void OnValidate()
	{
		base.OnValidate();

		// When the component is deserialized, apply the layer data
		if ( !string.IsNullOrEmpty( LayerDataJson ) )
		{
			DeserializeLayers( LayerDataJson );
		}
	}

	/// <summary>
	/// Gets or creates the SceneLayerData component for the given scene.
	/// </summary>
	public static SceneLayerData GetOrCreate( Scene scene )
	{
		if ( scene is null ) return null;

		var existing = scene.Components.Get<SceneLayerData>();
		if ( existing is not null ) return existing;

		return scene.Components.Create<SceneLayerData>();
	}

	/// <summary>
	/// Gets the SceneLayerData component for the given scene.
	/// </summary>
	public static SceneLayerData Get( Scene scene )
	{
		return scene?.Components.Get<SceneLayerData>();
	}
}
