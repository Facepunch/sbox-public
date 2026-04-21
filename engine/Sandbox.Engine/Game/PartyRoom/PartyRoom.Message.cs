namespace Sandbox;

partial class PartyRoom
{
	const int ProtocolIdentity = 5084;

	enum MessageIdentity : long
	{
		ChatMessage = 1001,
		VoiceMessage = 1002,
		Kicked = 1003,
	}

	public void SendChatMessage( string text )
	{
		using var bs = ByteStream.Create( 128 );
		bs.Write( ProtocolIdentity );
		bs.Write( MessageIdentity.ChatMessage );
		bs.Write( text );

		steamLobby.SendChatData( bs.ToArray() );
	}

	/// <summary>
	/// Kick a member from the lobby. Only the owner can kick members.
	/// </summary>
	public void Kick( SteamId friend )
	{
		if ( !Owner.IsMe )
			return;

		using var bs = ByteStream.Create( 128 );
		bs.Write( ProtocolIdentity );
		bs.Write( MessageIdentity.Kicked );
		bs.Write( friend );

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
		else if ( ident == MessageIdentity.Kicked )
		{
			var kicked = new Friend( stream.Read<ulong>() );
			if ( !kicked.IsMe )
				return;

			if ( friend.Id != Owner.Id )
				return;

			// kicked, leave the lobby
			Leave();
			return;
		}

		Log.Warning( $"Unhandled message from {friend}" );

	}
}
