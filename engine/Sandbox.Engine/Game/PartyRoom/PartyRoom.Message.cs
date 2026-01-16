namespace Sandbox;

partial class PartyRoom
{
	const int ProtocolIdentity = 5084;

	enum MessageIdentity : long
	{
		ChatMessage = 1001,
		VoiceMessage = 1002,
	}

	public void SendChatMessage( string text )
	{
		using var bs = ByteStream.Create( 128 );
		bs.Write( ProtocolIdentity );
		bs.Write( MessageIdentity.ChatMessage );
		bs.Write( text );

		steamLobby.SendChatData( bs.ToArray() );
	}

	void ILobby.OnMemberMessage( Friend friend, ByteStream stream )
	{
		var protocol = stream.Read<int>();

		if ( protocol != ProtocolIdentity )
		{
			Log.Warning( $"Unknown Protocol from {friend}" );
			return;
		}

		var ident = stream.Read<MessageIdentity>();

		if ( ident == MessageIdentity.ChatMessage )
		{
			var contents = stream.Read<string>();
			Log.Info( $"[Party] {friend}: {contents}" );
			OnChatMessage?.Invoke( friend, contents );
			return;
		}

		Log.Warning( $"Unhandled message from {friend}" );

	}
}
