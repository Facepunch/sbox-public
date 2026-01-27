using Facepunch.ActionGraphs;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

public partial class PrefabScene : Scene, IJsonConvert
{
	internal static PrefabScene CreateForEditing() => new PrefabScene( true );

	internal PrefabScene( bool isEditor ) : base( isEditor )
	{

	}

	/// <summary>
	/// A list of variables and their targets for this prefab scene
	/// </summary>
	[Obsolete]
	public VariableCollection Variables { get; } = new VariableCollection();

	public override bool Load( GameResource resource )
	{
		Assert.NotNull( resource );

		Clear();

		if ( resource is not PrefabFile file )
		{
			Log.Warning( "Resource is not a PrefabFile" );
			return false;
		}

		Source = file;

		// Conna: Don't network spawn any GameObjects we create here...
		using var suppressSpawnScope = SceneNetworkSystem.SuppressSpawnMessages();

		using var sourceScope = ActionGraph.PushSerializationOptions( file.SerializationOptions with { ForceUpdateCached = IsEditor } );
		using var sceneScope = Push();
		using var blobs = BlobDataSerializer.Load( file.BinaryData, file.ResourcePath );

		// Clear cached binary data now that we've loaded it
		file.BinaryData = null;

		if ( file.RootObject is null )
		{
			file.RootObject = new GameObject( file.ResourceName ).Serialize();
		}

		using ( CallbackBatch.Isolated() )
		{
			Deserialize( file.RootObject );
		}

		return true;
	}

	public PrefabFile ToPrefabFile()
	{
		var target = (Source as PrefabFile) ?? new PrefabFile();

		// Prefab Scene don't modify anything just write current state to target
		target.RootObject = Serialize();

		var prefabScene = (PrefabCacheScene)SceneUtility.GetPrefabScene( target );
		prefabScene.Refresh( target );

		return target;
	}

	public override JsonObject Serialize( SerializeOptions options = null )
	{
		using var sceneScope = Push();

		var jso = base.Serialize( options );

		jso["__properties"] = Root?.Scene?.SerializeProperties() ?? SerializeProperties();
#pragma warning disable CS0612
		jso["__variables"] = Json.ToNode( Variables?.Serialize() );
#pragma warning restore CS0612

		return jso;
	}

	public override void Deserialize( JsonObject node, DeserializeOptions options )
	{
		using var sceneScope = Push();

		base.Deserialize( node, options );

		LoadVariables( node["__variables"] as JsonArray );
	}

	void LoadVariables( JsonArray array )
	{
		if ( array is null )
			return;

#pragma warning disable CS0612
		Variables.Deserialize( array.Deserialize<List<PrefabVariable>>() );
#pragma warning restore CS0612
	}

	public static new void JsonWrite( object value, Utf8JsonWriter writer )
	{
		if ( value is not PrefabScene prefabScene )
			throw new NotImplementedException();

		if ( !prefabScene.IsValid )
		{
			writer.WriteNullValue();
			return;
		}

		// When writing prefab scene properties we always write the path never the ID.
		JsonSerializer.Serialize( writer, GameObjectReference.FromPrefabPath( prefabScene.Source.ResourcePath ), Json.options );
	}
}


