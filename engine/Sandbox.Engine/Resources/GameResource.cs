using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using Sandbox.Engine;
using Sandbox.Internal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Assets defined in C# and created through tools.
/// You can define your own <a href="https://sbox.game/dev/doc/assetsresources/custom-assets/">Custom Asset Types</a>.
/// </summary>
[Library]
public abstract partial class GameResource : Resource, ISourceLineProvider
{
	/// <summary>
	/// Allows tools to post process the serialized json object
	/// </summary>
	internal static Action<object, JsonObject> ProcessSerializedObject { get; set; }

	[JsonInclude]
	[JsonPropertyName( "__references" )]
	internal string[] referencedPackages { get; set; }

	/// <summary>
	/// The last saved compiled hash for this file.
	/// </summary>
	private int _jsonHash;

	/// <summary>
	/// The last saved uncompiled hash for this file. We use this to detect external changes in the editor, it's never serialized.
	/// </summary>
	[Hide, JsonIgnore]
	internal int LastSavedSourceHash { get; set; }

	/// <summary>
	/// Re-use ActionGraph instances when deserializing this resource.
	/// </summary>
	[JsonIgnore]
	internal ActionGraphCache ActionGraphCache { get; } = new();

	bool _unsavedChanges;

	/// <summary>
	/// True if this resource has changed but the changes aren't written to disk
	/// </summary>
	[Hide, JsonIgnore]
	public sealed override bool HasUnsavedChanges => _unsavedChanges;

	/// <summary>
	/// Binary data to be written alongside the JSON file.
	/// </summary>
	[Hide, JsonIgnore]
	internal byte[] BinaryData { get; set; }

	/// <summary>
	/// Should be called after the resource has been edited by the inspector
	/// </summary>
	public sealed override void StateHasChanged()
	{
		_unsavedChanges = true;
	}

	/// <summary>
	/// True if we're waiting for our load to complete
	/// </summary>
	bool _awaitingLoad;

	/// <summary>
	/// True if we're a promise, waiting to finalize the load
	/// </summary>
	internal bool IsPromise => _awaitingLoad;

	/// <summary>
	/// Get a list of packages that are needed to load this asset
	/// </summary>
	public IEnumerable<string> GetReferencedPackages()
	{
		return referencedPackages ?? Array.Empty<string>();
	}

	/// <summary>
	/// Called when the asset is first loaded from disk.
	/// </summary>
	protected virtual void PostLoad()
	{
	}

	internal bool PostLoadInternal()
	{
		_awaitingLoad = false;

		try
		{
			PostLoad();
			return true;
		}
		catch ( System.Exception e )
		{
			Log.Error( e );
			return false;
		}
	}

	/// <summary>
	/// Called when the asset is recompiled/reloaded from disk.
	/// </summary>
	protected virtual void PostReload()
	{
	}

	internal bool PostReloadInternal()
	{
		_awaitingLoad = false;

		try
		{
			PostReload();
			return true;
		}
		catch ( System.Exception e )
		{
			Log.Error( e );
			return false;
		}
	}

	/// <summary>
	/// Creates an instance of this type that will get loaded into later. This allows us to
	/// have resources that reference other resources that aren't loaded yet (or are missing).
	/// </summary>
	internal static GameResource GetPromise( System.Type type, string filename )
	{
		var path = FixPath( filename );
		var hash = path.FastHash();

		var obj = Game.Resources.Get( type, hash ) as GameResource;
		if ( obj != null ) return obj;

		obj = System.Activator.CreateInstance( type ) as GameResource;

		if ( obj is null )
		{
			Log.Warning( $"Failed to create '{type.FullName}'" );
			return default;
		}

		obj.InternalInitialize( filename );

		Game.Resources.Register( obj );
		return obj;
	}

	private void InternalInitialize( string filename )
	{
		ResourcePath = FixPath( filename );
		ResourceName = System.IO.Path.GetFileNameWithoutExtension( ResourcePath );
		ResourceId = ResourcePath.FastHash();

		Manifest = AsyncResourceLoader.Load( ResourcePath );

		_awaitingLoad = true;
	}

	/// <summary>
	/// Makes sure all properties are derived properly from filename, and then registered to ResourceLibrary
	/// </summary>
	internal void Register( string filename )
	{
		if ( string.IsNullOrEmpty( filename ) ) return;

		ResourcePath = FixPath( filename );
		ResourceName = System.IO.Path.GetFileNameWithoutExtension( ResourcePath );
		ResourceId = ResourcePath.FastHash();

		Game.Resources.Register( this );
	}

	/// <summary>
	/// Loads a game resource from given file.
	/// </summary>
	internal static T Load<T>( string filename ) where T : GameResource
	{
		if ( ResourceLibrary.TryGet<T>( filename, out var resource ) )
		{
			return resource;
		}

		return null;
	}

	private SerializationOptions _serializationOptions;

	[JsonIgnore, Hide]
	internal SerializationOptions SerializationOptions => _serializationOptions ??= CreateSerializationOptions();

	/// <summary>
	/// Target type used for any action graphs contained in this resource.
	/// Defaults to this resource's type.
	/// </summary>
	[JsonIgnore, Hide]
	protected virtual Type ActionGraphTargetType => ActionGraphTarget?.GetType() ?? GetType();

	/// <summary>
	/// Target instance used for any action graphs contained in this resource.
	/// Defaults to this resource itself.
	/// </summary>
	[JsonIgnore, Hide]
	protected virtual object ActionGraphTarget => this;

	private SerializationOptions CreateSerializationOptions()
	{
		return new(
			Cache: ActionGraphCache,
			WriteCacheReferences: false,
			ForceUpdateCached: true,
			SourceLocation: new GameResourceSourceLocation( this ),
			ImpliedTarget: ActionGraphTargetType is { } targetType
				? InputDefinition.Target( targetType, ActionGraphTarget )
				: null );
	}

	/// <summary>
	/// Pushes a context in which action graphs belonging to this resource can be serialized or deserialized.
	/// </summary>
	protected internal IDisposable PushSerializationScope()
	{
		return ActionGraph.PushSerializationOptions( SerializationOptions );
	}

	/// <summary>
	/// Serialize the current state to a JsonObject
	/// </summary>
	public JsonObject Serialize()
	{
		JsonObject jsobj;

		using ( PushSerializationScope() )
		using ( var blobs = BlobDataSerializer.Capture() )
		{
			jsobj = Json.SerializeAsObject( this );
			OnJsonSerialize( jsobj );

			var capturedData = blobs.ToByteArray();
			if ( capturedData != null || BinaryData == null )
			{
				BinaryData = capturedData;
			}
		}

		jsobj["__version"] = ResourceVersion;
		ProcessSerializedObject?.Invoke( this, jsobj );

		return jsobj;
	}

	/// <summary>
	/// called to upgrade a bunch of json to the latest version
	/// </summary>
	/// <param name="node"></param>
	void JsonUpgrade( JsonObject node )
	{
		if ( ResourceVersion == 0 )
			return;

		var serializedVersion = (int)(node["__version"] ?? 0);
		if ( serializedVersion >= ResourceVersion ) return;

		JsonUpgrader.Upgrade( serializedVersion, node, GetType() );
	}

	string ISourcePathProvider.Path => ResourcePath;
	int ISourceLineProvider.Line => 0;

	/// <summary>
	/// The version of the component. Used by <see cref="JsonUpgrader"/>.
	/// </summary>
	[JsonIgnore, Hide] public virtual int ResourceVersion => 0;

	public void LoadFromJson( string json )
	{
		Assert.NotNull( json, "json should not be null" );

		_jsonHash = json.FastHash();

		var docOptions = new JsonDocumentOptions();
		docOptions.MaxDepth = 512;

		var nodeOptions = new JsonNodeOptions();
		nodeOptions.PropertyNameCaseInsensitive = true;

		var node = JsonNode.Parse( json, nodeOptions, docOptions );

		if ( node is not JsonObject jso )
		{
			throw new ArgumentException( "Couldn't load json" );
		}

		JsonUpgrade( jso );
		jso.Remove( "__version" );

		// Load binary data for deserialization
		using var blobs = BlobDataSerializer.Load( BinaryData, ResourcePath );

		Deserialize( jso );

		BinaryData = null;
		_awaitingLoad = false;
	}

	/// <summary>
	/// Deserialize values from a JsonObject
	/// </summary>
	public void Deserialize( JsonObject jso )
	{
		using ( PushSerializationScope() )
		{
			Json.DeserializeToObject( this, jso );
		}
	}

	/// <summary>
	/// Called after we serialize, allowing you to store any extra or modify the output.
	/// </summary>
	protected virtual void OnJsonSerialize( JsonObject node )
	{
	}

	internal bool TryLoadFromData( Span<byte> data )
	{
		var json = Game.Resources.ReadCompiledResourceJson( data );
		if ( json is null )
			return false;

		// Prevent loading the same data twice
		// todo - this should be a CRC on data
		var newHash = json.FastHash();
		if ( newHash == _jsonHash )
			return false;

		// Load binary data from compiled resource binary blobs if present
		BinaryData = Game.Resources.ReadCompiledResourceBlock( BlobDataSerializer.CompiledBlobName, data );

		LoadFromJson( json );
		LoadFromResource( data );
		return true;
	}

	internal virtual bool LoadFromResource( Span<byte> data )
	{
		return false;
	}

	internal virtual void SaveToDisk( string filename, string jsonString )
	{
		_unsavedChanges = false;

		LastSavedSourceHash = jsonString.FastHash();
		System.IO.File.WriteAllText( filename, jsonString );

		// Write binary data if present
		if ( BinaryData != null && BinaryData.Length > 0 )
		{
			try
			{
				var blobPath = filename + "_d";
				System.IO.File.WriteAllBytes( blobPath, BinaryData );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Failed to write binary data file: {e.Message}" );
			}
			finally
			{
				BinaryData = null;
			}
		}

		IToolsDll.Current?.RunEvent<ResourceLibrary.IEventListener>( i => i.OnSave( this ) );
	}

	internal virtual bool CanLoadFromJson()
	{
		return true;
	}

	private bool _destroyed;

	[Hide, JsonIgnore] public sealed override bool IsValid => !_destroyed;

	/// <summary>
	/// Called when this resource is being unloaded.
	/// Clean up any resources owned by this instance here.
	/// </summary>
	protected virtual void OnDestroy()
	{

	}

	internal void DestroyInternal()
	{
		if ( _destroyed ) return;

		_destroyed = true;

		Game.Resources.Unregister( this );

		try
		{
			OnDestroy();
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"{ex.GetType().Name} when destroying {ResourcePath}" );
		}
	}
}

