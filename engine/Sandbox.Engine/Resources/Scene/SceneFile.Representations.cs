using System.IO;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

public partial class SceneFile
{
	[JsonInclude, JsonPropertyName( "__scene_compiled" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	internal bool IsCompiled { get; set; }

	[JsonInclude, JsonPropertyName( "__scene_compile_error" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	internal string CompileError { get; set; }

	[JsonIgnore]
	internal bool IsSourceSnapshot { get; private set; }

	[JsonIgnore]
	internal byte[] SceneBinaryData { get; private set; }

	internal static Func<SceneFile, SceneFile> ResolveRuntimeScene { get; set; }

	protected override void OnJsonDeserialize( JsonObject node )
	{
		IsCompiled = false;
		CompileError = null;
		SceneBinaryData = null;
	}

	internal BlobDataSerializer.BlobContext LoadBlobData()
	{
		SceneBinaryData = BinaryData ?? SceneBinaryData;
		var context = BlobDataSerializer.Load( SceneBinaryData, ResourcePath );
		BinaryData = null;
		return context;
	}

	internal void InitializeSource( string path, Guid guid )
	{
		InitializeIdentity( path, guid );
		IsSourceSnapshot = true;
	}

	void InitializeIdentity( string path, Guid guid )
	{
		ResourcePath = FixPath( path );
		ResourceName = Path.GetFileNameWithoutExtension( ResourcePath );
		Guid = guid;
		ResourceIdLong = ResourcePath.FastHash64();
#pragma warning disable CS0618
		ResourceId = ResourcePath.FastHash();
#pragma warning restore CS0618
	}

	internal static SceneFile FromSource( string path, Guid guid, string json, byte[] binaryData )
	{
		var file = new SceneFile();
		file.InitializeSource( path, guid );
		file.BinaryData = binaryData ?? [];
		file.LoadFromJson( json );
		file.SceneBinaryData = binaryData ?? [];
		file.LastSavedSourceHash = json.FastHash();
		return file;
	}

	internal static SceneFile FromCompiled( string path, Guid guid, byte[] data )
	{
		var file = new SceneFile();
		file.InitializeIdentity( path, guid );
		if ( !file.TryLoadFromData( data ) )
			throw new InvalidDataException( $"Could not read compiled scene '{path}'." );

		return file;
	}

	internal override bool LoadFromResource( Span<byte> data )
	{
		// Never resolve a compiled scene's blobs through its editable .scene_d sidecar.
		SceneBinaryData = Game.Resources.ReadCompiledResourceBlock( BlobDataSerializer.CompiledBlobName, data ) ?? [];
		return true;
	}
}
