global using ProtoBuf;

namespace Sandbox.Protobuf;

public static class ProtobufHelper
{
	static Dictionary<ushort, Type> MessageTypes = new Dictionary<ushort, Type>();

	static ProtobufHelper()
	{
		var types = typeof( ProtobufHelper ).Assembly.DefinedTypes
								.Where( x => x.ImplementedInterfaces.Contains( typeof( IMessage ) ) );

		foreach ( var t in types )
		{
			var id = t.GetProperty( "MessageIdent", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public );
			if ( id is null ) continue;

			var messageId = (MessageId?)id.GetValue( null );
			if ( messageId is null ) continue;

			var key = (ushort)messageId.Value;

			if ( MessageTypes.ContainsKey( key ) )
				throw new System.Exception( $"Duplicate Message Ident {messageId.Value} found in {t} and {MessageTypes[key]}" );

			MessageTypes[key] = t;
		}
	}

	public static T From<T>( Stream stream )
	{
		return Serializer.Deserialize<T>( stream );
	}

	public static byte[] GetBytes<T>( T obj )
	{
		using var stream = new MemoryStream();
		ToStream( obj, stream );
		return stream.ToArray();
	}

	public static void ToStream<T>( T obj, Stream stream )
	{
		Serializer.Serialize( stream, obj );
	}

	/// <summary>
	/// Read an object from the wire. This is a message type, then the message.
	/// </summary>
	public static object FromWire( Stream stream )
	{
		if ( stream.Length <= 4 )
			return default;

		using var br = new BinaryReader( stream, System.Text.Encoding.UTF8, true );
		var messageId = br.ReadUInt16();

		if ( !MessageTypes.TryGetValue( messageId, out var messageType ) )
			return default;

		return Serializer.Deserialize( messageType, stream );
	}

	/// <summary>
	/// Write this IMessage to the wire. Automatically encodes the message id.
	/// </summary>
	public static void ToWire<T>( T msg, Stream stream ) where T : IMessage
	{
		using var bw = new BinaryWriter( stream, System.Text.Encoding.UTF8, true );
		bw.Write( (ushort)T.MessageIdent );
		ToStream( msg, stream );
	}
}
