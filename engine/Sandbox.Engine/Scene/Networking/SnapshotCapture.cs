using Sandbox.Network;
using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>
/// Owns the data captured from a scene. Materialization only reads detached JSON and bytes;
/// scene callbacks, sync tables and blob serializers have already run on the main thread.
/// </summary>
internal sealed class SnapshotCapture
{
	internal SnapshotMsg Snapshot = SnapshotMsg.Create();
	internal Lazy<string> SceneJson;
	internal Lazy<byte[]> SceneBlobs;
	readonly List<JsonNode> objectJson = new();
	readonly List<BlobDataSerializer.CapturedBlob[]> objectBlobs = new();

	internal void AddObject( JsonNode node, BlobDataSerializer.BlobContext blobs = null )
	{
		objectJson.Add( Detach( node ) );
		objectBlobs.Add( blobs?.Detach() );
	}

	internal byte[][] Encode<T>( BytePack serializer, Func<SnapshotMsg, T> envelope, NetFlags flags )
	{
		var stream = ByteStream.Create( 1024 );
		try
		{
			stream.Write( InternalMessageType.Packed );
			serializer.SerializeTo( ref stream, envelope( Materialize() ) );
			return Connection.CreatePackets( Connection.Encode( stream ), flags );
		}
		finally
		{
			stream.Dispose();
		}
	}

	internal SnapshotMsg Materialize()
	{
		var result = new SnapshotMsg
		{
			Time = Snapshot.Time,
			SceneData = SceneJson.Value,
			BlobData = SceneBlobs?.Value ?? Snapshot.BlobData,
			GameObjectSystems = Snapshot.GameObjectSystems,
			NetworkObjects = new List<object>( Snapshot.NetworkObjects.Count )
		};

		for ( int i = 0; i < Snapshot.NetworkObjects.Count; i++ )
		{
			var message = (ObjectCreateMsg)Snapshot.NetworkObjects[i];
			message.JsonData = objectJson[i].ToJsonString();
			if ( objectBlobs[i] is { } blobs )
			{
				message.BlobData = BlobDataSerializer.PackBlobs( blobs );
			}
			result.NetworkObjects.Add( message );
		}

		return result;
	}

	// Custom JSON populators can return nodes they still own, or JsonValue<T> with a
	// deferred converter. Copy the tree and execute those converters during capture.
	internal static JsonNode Detach( JsonNode node ) => node?.DeepClone();
}
