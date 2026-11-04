using System.IO;

namespace Sandbox;

public partial class SceneFile
{
	internal void InitializeSource( string path, Guid guid )
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
		file.BinaryData = binaryData ?? [];
		file.LastSavedSourceHash = json.FastHash();
		return file;
	}

	internal static SceneFile FromCompiled( string path, Guid guid, byte[] data )
	{
		var file = new SceneFile();
		file.InitializeSource( path, guid );
		if ( !file.TryLoadFromData( data ) )
			throw new InvalidDataException( $"Could not read compiled scene '{path}'." );

		return file;
	}

	internal override bool LoadFromResource( Span<byte> data )
	{
		BinaryData = Game.Resources.ReadCompiledResourceBlock( BlobDataSerializer.CompiledBlobName, data ) ?? [];
		return true;
	}
}
