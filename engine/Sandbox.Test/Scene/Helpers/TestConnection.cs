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

	internal override void InternalSend( ByteStream stream, NetFlags flags )
	{
		var reader = new ByteStream( stream.ToArray() );

		var type = reader.Read<InternalMessageType>();

		switch ( type )
		{
			case InternalMessageType.Chunk:
				throw new NotImplementedException();

			case InternalMessageType.Packed:
				Messages.Add( new Message( type, GlobalGameNamespace.TypeLibrary.FromBytes<object>( ref reader ) ) );
				break;

			default:
				Messages.Add( new Message( type, reader.GetRemainingBytes().ToArray() ) );
				break;
		}
	}

	internal override void InternalRecv( NetworkSystem.MessageHandler handler )
	{

	}

	internal override void InternalClose( int closeCode, string closeReason )
	{

	}
}
