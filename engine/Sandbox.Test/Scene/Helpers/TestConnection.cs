using System;
using System.Collections.Generic;
using Sandbox.Internal;
using Sandbox.Network;

namespace Sandbox.SceneTests;

#nullable enable

internal sealed class TestConnection : Connection
{
	public record struct Message( InternalMessageType Type, object? Payload = null );

	public List<Message> Messages { get; } = new();

	public override bool IsHost { get; }

	public TestConnection( Guid id, bool isHost = false )
	{
		IsHost = isHost;
		Id = id;
	}

	public TestConnection()
	{

	}

	public void ProcessMessages( InternalMessageType type, Action<ByteStream> callback )
	{
		foreach ( var m in Messages.Where( p => p.Type == type ) )
		{
			using var reader = ByteStream.CreateReader( m.Payload as byte[] );
			callback( reader );
		}
	}

	internal override void InternalSend( byte[] data, NetFlags flags )
	{
		if ( data[0] == FlagChunk )
			throw new NotImplementedException( "TestConnection does not support chunked messages" );

		// Decode the wire envelope and dispatch by InternalMessageType.
		var decoded = Connection.Decode( data );
		var reader = ByteStream.CreateReader( decoded );

		var type = reader.Read<InternalMessageType>();

		switch ( type )
		{
			case InternalMessageType.Packed:
				Messages.Add( new Message( type, GlobalGameNamespace.TypeLibrary.FromBytes<object>( ref reader ) ) );
				break;

			default:
				Messages.Add( new Message( type, reader.GetRemainingBytes().ToArray() ) );
				break;
		}

		reader.Dispose();
	}

	internal override void InternalRecv( NetworkSystem.MessageHandler handler )
	{

	}

	internal override void InternalClose( int closeCode, string closeReason )
	{

	}
}
