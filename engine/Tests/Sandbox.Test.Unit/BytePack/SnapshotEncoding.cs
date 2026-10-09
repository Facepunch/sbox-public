using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Sandbox.Internal;
using Sandbox.Network;

namespace BytePackTests;

[TestClass]
public class SnapshotEncodingTest
{
	sealed class UnexpectedCollectionPacker( Type type ) : BytePack.Packer
	{
		public override Type TargetType => type;
		public override void Write( ref ByteStream stream, object value ) => throw new Exception( "Collections must use their existing wire format" );
	}

	[TestMethod]
	public void CachedRuntimePackersDoNotOverrideCollectionFormats()
	{
		var packer = new BytePack();
		packer.Add( new UnexpectedCollectionPacker( typeof( List<int> ) ) );
		packer.Add( new UnexpectedCollectionPacker( typeof( Dictionary<string, int> ) ) );
		var values = new List<int> { 1, 2, 3 };
		var dictionary = new Dictionary<string, int> { ["one"] = 1 };
		var baseline = new BytePack();
		CollectionAssert.AreEqual( baseline.Serialize( values ), packer.Serialize( values ) );
		CollectionAssert.AreEqual( baseline.Serialize( dictionary ), packer.Serialize( dictionary ) );
	}

	[DataTestMethod]
	[DataRow( 0 )]
	[DataRow( 3 )]
	[DataRow( 4096 )]
	public void ByteBuffersKeepTheValueArrayWireFormat( int length )
	{
		var bytes = new byte[length];
		new Random( 42 ).NextBytes( bytes );
		var stream = ByteStream.Create( Math.Max( 16, length + 6 ) );
		try
		{
			stream.Write( BytePack.Identifier.ArrayValue );
			stream.Write( length );
			stream.Write( BytePack.Identifier.Byte );
			stream.Write( bytes );
			var packer = new BytePack();
			CollectionAssert.AreEqual( stream.ToArray(), packer.Serialize( bytes ) );
			CollectionAssert.AreEqual( bytes, (byte[])packer.Deserialize( stream.ToArray() ) );
		}
		finally
		{
			stream.Dispose();
		}
	}

	sealed class MutableValue { public int Number { get; set; } }
	sealed class CaptureConverter : JsonConverter<MutableValue>
	{
		public int Writes;
		public int Thread;
		public override MutableValue Read( ref Utf8JsonReader reader, Type type, JsonSerializerOptions options ) => throw new NotSupportedException();
		public override void Write( Utf8JsonWriter writer, MutableValue value, JsonSerializerOptions options )
		{
			Writes++;
			Thread = Environment.CurrentManagedThreadId;
			writer.WriteNumberValue( value.Number );
		}
	}

	[TestMethod]
	public void CaptureDetachesCustomValuesAndRunsConvertersBeforeWorker()
	{
		var converter = new CaptureConverter();
		var options = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
		options.Converters.Add( converter );
		var value = new MutableValue { Number = 7 };
		var original = new JsonObject
		{
			["Custom"] = JsonValue.Create( value, (JsonTypeInfo<MutableValue>)options.GetTypeInfo( typeof( MutableValue ) ) ),
			["Nested"] = new JsonArray( 1, 2, 3 )
		};
		var detached = SnapshotCapture.Detach( original );
		Assert.AreEqual( 1, converter.Writes );
		Assert.AreEqual( Environment.CurrentManagedThreadId, converter.Thread );
		value.Number = 99;
		original["Nested"][0] = 99;
		var json = Task.Run( () => detached.ToJsonString() ).GetAwaiter().GetResult();
		Assert.AreEqual( "{\"Custom\":7,\"Nested\":[1,2,3]}", json );
		Assert.AreEqual( 1, converter.Writes );
	}

	sealed class RecordingConnection : Connection
	{
		internal readonly List<byte[]> Packets = new();
		internal int Closes;
		internal override void InternalSend( byte[] data, NetFlags flags ) => Packets.Add( data );
		internal override void InternalRecv( NetworkSystem.MessageHandler handler ) { }
		internal override void InternalClose( int code, string reason ) => Closes++;
	}

	[TestMethod]
	public void PendingSnapshotOrdersUpdatesAndKeepsChunksTogether()
	{
		var connection = new RecordingConnection();
		var pending = new TaskCompletionSource<byte[][]>();
		connection.QueueSend( pending.Task, NetFlags.Reliable );
		connection.Send( [3], NetFlags.Reliable );
		connection.FlushPendingSends();
		Assert.AreEqual( 0, connection.Packets.Count );
		pending.SetResult( [[1], [2]] );
		connection.FlushPendingSends();
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, connection.Packets.Select( p => p[0] ).ToArray() );
	}

	[TestMethod]
	public void CloseDiscardsPendingSnapshotAndUpdates()
	{
		var connection = new RecordingConnection();
		var pending = new TaskCompletionSource<byte[][]>();
		connection.QueueSend( pending.Task, NetFlags.Reliable );
		connection.Send( [2], NetFlags.Reliable );
		connection.Close( 0, "Closed" );
		pending.SetResult( [[1]] );
		connection.FlushPendingSends();
		connection.Send( [3], NetFlags.Reliable );
		Assert.AreEqual( 0, connection.Packets.Count );
		Assert.IsFalse( connection.HasPendingSends );
	}

	[TestMethod]
	public void FailedSnapshotDiscardsUpdates()
	{
		var connection = new RecordingConnection();
		connection.QueueSend( Task.FromException<byte[][]>( new InvalidOperationException( "Synthetic failure" ) ), NetFlags.Reliable );
		connection.Send( [2], NetFlags.Reliable );
		connection.FlushPendingSends();
		Assert.AreEqual( 0, connection.Packets.Count );
		Assert.AreEqual( 1, connection.Closes );
	}

	[TestMethod]
	public void DetachedSerializerMatchesWireAndSurvivesLibraryReset()
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( Scene ).Assembly, false );
		var serializer = library.CreateDetachedSerializer( typeof( InitialSnapshotResponse ), typeof( SnapshotMsg ),
			typeof( SnapshotMsg.GameObjectSystemData ), typeof( ObjectCreateMsg ) );
		var capture = new SnapshotCapture { SceneJson = new Lazy<string>( () => "{}" ) };
		capture.AddObject( new JsonObject { ["Name"] = "Captured" } );
		capture.Snapshot.NetworkObjects.Add( new ObjectCreateMsg { Guid = Guid.NewGuid(), TableData = [1, 2, 3] } );
		capture.Snapshot.GameObjectSystems.Add( new SnapshotMsg.GameObjectSystemData { Id = Guid.NewGuid(), Type = 42, SnapshotData = [4, 5] } );
		var snapshot = capture.Materialize();
		var expected = library.ToBytes( new InitialSnapshotResponse { Snapshot = snapshot } );
		// Adding another assembly rebuilds the library's shared packer cache.
		library.AddAssembly( typeof( SnapshotEncodingTest ).Assembly, false );
		var actual = Task.Run( () => serializer.Serialize( new InitialSnapshotResponse { Snapshot = capture.Materialize() } ) ).GetAwaiter().GetResult();
		CollectionAssert.AreEqual( expected, actual );
		serializer.Dispose();
	}
}
