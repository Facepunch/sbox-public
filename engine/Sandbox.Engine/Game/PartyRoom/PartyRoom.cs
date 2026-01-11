using NativeEngine;
using Sandbox.Engine;
using Sandbox.Network;
using Steamworks.Data;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// A Party. A Party with your friends.
/// </summary>
public partial class PartyRoom : ILobby
{
	public static PartyRoom Current { get; private set; }

	Steamworks.Data.Lobby steamLobby;

	/// <summary>
	/// The unique identifier of this lobby
	/// </summary>
	public SteamId Id => steamLobby.Id.Value;

	internal int NetworkChannel => (int)(Id % int.MaxValue);

	PartyRoom( Lobby value )
	{
		steamLobby = value;
		UpdateLobbyData();

		if ( Owner.IsMe )
		{
			steamLobby.SetData( "lobby_type", "party" );
			steamLobby.SetData( "api", Protocol.Api.ToString() );
			steamLobby.SetData( "protocol", Protocol.Network.ToString() );
			steamLobby.SetData( "buildid", $"{Application.Version}" );
			steamLobby.SetData( "dev", Application.IsEditor ? "1" : "0" );
			steamLobby.SetData( "_ownerid", Owner.Id.ToString() );
		}

		LobbyManager.Register( this );
		VoiceManager.OnCompressedVoiceData += OnVoiceRecorded;
	}

	public void Leave()
	{
		steamLobby.Leave();
		steamLobby = default;

		if ( Current == this )
		{
			Current = default;
		}

		// this is no longer needed, we don't need to get messages about it
		// we don't need to know when people enter and leave, so forget about it.
		LobbyManager.Unregister( this );

		VoiceManager.OnCompressedVoiceData -= OnVoiceRecorded;
	}

	/// <summary>
	/// Set the owner to someone else. You need to be the owner
	/// </summary>
	public bool SetOwner( SteamId friend )
	{
		return steamLobby.SetOwner( friend );
	}


	internal void InviteFriend( SteamId steamid )
	{
		if ( steamLobby.InviteFriend( steamid.ValueUnsigned ) )
		{
			Log.Info( $"Lobby invite to {steamid} sent" );
		}
		else
		{
			Log.Warning( $"Lobby invite to {steamid} was not sent" );
		}
	}

	internal void InviteFriend()
	{
		steamLobby.InviteOverlay();
	}

	/// <summary>
	/// Allow communication via voice when in the main menu.
	/// </summary>
	public bool VoiceCommunicationAllowed => IGameInstance.Current is null;

	string lastConnect;
	RealTimeSince timeSinceUpdate = 0;

	bool _voiceRecording;
	public bool VoiceRecording
	{
		get => _voiceRecording;
		set
		{
			if ( _voiceRecording == value ) return;
			_voiceRecording = value;

			if ( _voiceRecording )
			{
				VoiceManager.StartRecording();
			}
			else
			{
				VoiceManager.StopRecording();
			}
		}
	}

	/// <summary>
	/// Voice data has been recieved. Send it to everyone.
	/// </summary>
	private void OnVoiceRecorded( Memory<byte> memory )
	{
		// Don't send 
		if ( !VoiceCommunicationAllowed )
			return;

		using var bs = ByteStream.Create( 32 );
		bs.Write( MessageIdentity.VoiceMessage );
		bs.WriteArray( memory.ToArray() );

		foreach ( var friend in Members )
		{
			int flags = 0;
			flags = 8; // k_nSteamNetworkingSend_Reliable
			flags |= 32; // k_nSteamNetworkingSend_AutoRestartBrokenSession

			unsafe
			{
				fixed ( byte* pData = bs.ToSpan() )
				{
					Steam.SteamNetworkingMessages().SendMessageToUser( friend.Id, (IntPtr)pData, bs.Length, flags, NetworkChannel );
				}
			}
		}
	}

	internal void Tick()
	{
		// Record the voice if voice button less than 0.3 seconds old
		VoiceRecording = VoiceCommunicationAllowed && timeSinceWantVoiceSend < 0.1f;

		ReadVoiceChannel();

		if ( timeSinceUpdate < 0.5f ) return;
		timeSinceUpdate = 0;

		if ( Owner.IsMe )
		{
			steamLobby.SetData( "api", Protocol.Api.ToString() );
			steamLobby.SetData( "protocol", Protocol.Network.ToString() );
			steamLobby.SetData( "buildid", $"{Application.Version}" );
			steamLobby.SetData( "dev", Application.IsEditor ? "1" : "0" );
			steamLobby.SetData( "_ownerid", Owner.Id.ToString() );
			steamLobby.SetData( "package", Application.GamePackage?.FullIdent );
			steamLobby.SetData( "packagetitle", Application.GamePackage?.Title );

			if ( Networking.System is not null && Networking.System.Sockets.OfType<SteamLobbySocket>().FirstOrDefault() is SteamLobbySocket sl )
			{
				steamLobby.SetData( "gameaddress", sl.LobbySteamId.ToString() );
			}
			else
			{
				steamLobby.DeleteData( "gameaddress" );
			}
		}
		else
		{
			var connect = steamLobby.GetData( "gameaddress" );
			if ( lastConnect != connect )
			{
				lastConnect = connect;
				OnConnectChanged( connect );
			}
		}
	}

	unsafe void ReadVoiceChannel()
	{
		var net = Steam.SteamNetworkingMessages();

		int batchSize = 8;
		IntPtr* ptr = stackalloc IntPtr[batchSize];

		while ( true )
		{
			int count = net.ReceiveMessagesOnChannel( NetworkChannel, (IntPtr)ptr, batchSize );
			if ( count == 0 )
				break;

			for ( int i = 0; i < count; i++ )
			{
				var msg = Unsafe.Read<Sandbox.Network.SteamNetworkMessage>( (void*)ptr[i] );

				if ( !Members.Any( x => x.Id == msg.IdentitySteamId ) )
				{
					Log.Trace( $"Dropping message from {msg.IdentitySteamId} not in party" );
					continue;
				}

				var data = new ByteStream( msg.Data, msg.Size );

				var iMessageType = data.Read<MessageIdentity>();


				if ( iMessageType == MessageIdentity.VoiceMessage )
				{
					var message = data.ReadArraySpan<byte>( 1024 * 1024 );
					OnVoiceData?.Invoke( new Friend( msg.IdentitySteamId ), message.ToArray() );
				}

				net.ReleaseMessage( ptr[i] );
			}
		}
	}

	void OnConnectChanged( string address )
	{
		Log.Info( $"Party Connect changed to '{lastConnect}'" );

		NetworkConsoleCommands.Disconnect();

		if ( address is not null )
		{
			NetworkConsoleCommands.ConnectToServer( address );
		}
	}

	[Obsolete]
	public static Task<PartyRoom> Create( int maxMembers )
	{
		return Create( maxMembers, $"{Utility.Steam.PersonaName}'s Party", true );
	}

	public static async Task<PartyRoom> Create( int maxMembers, string name, bool ispublic )
	{
		var lobby = await Steamworks.SteamMatchmaking.CreateLobbyAsync( maxMembers );

		if ( !lobby.HasValue )
		{
			Log.Warning( "Failed to create lobby for party" );
			return null;
		}

		lobby.Value.SetData( "name", name );

		if ( !ispublic )
		{
			lobby.Value.SetPrivate();
		}

		var room = new PartyRoom( lobby.Value );

		Current = room;

		return room;
	}


	/// <summary>
	/// A list of members in this room
	/// </summary>
	public IEnumerable<Friend> Members => steamLobby.Members.Select( x => new Friend( x ) );

	public Friend Owner { get; private set; }



	public static async Task<Entry[]> Find()
	{
		// todo - filter by lobby_type = party
		var found = await Steamworks.SteamMatchmaking.LobbyList
														.WithKeyValue( "lobby_type", "party" )
														.WithSlotsAvailable( 1 )
														.RequestAsync( default );

		if ( found is null )
			return Array.Empty<Entry>();

		return found.Select( x => new Entry( x ) ).ToArray();
	}

	ulong ILobby.Id => steamLobby.Id;

	void ILobby.OnMemberEnter( Friend friend )
	{
		Log.Info( $"Party member entered {friend}" );
		OnJoin?.Invoke( friend );
	}

	void ILobby.OnMemberLeave( Friend friend )
	{
		Log.Info( $"Party member leave {friend}" );
		OnLeave?.Invoke( friend );
	}

	void ILobby.OnMemberUpdated( Friend friend )
	{
		Log.Info( $"Party member updated {friend}" );
	}

	void ILobby.OnLobbyUpdated()
	{
		UpdateLobbyData();
	}

	void UpdateLobbyData()
	{
		Owner = new Friend( steamLobby.Owner );
	}

	RealTimeSince timeSinceWantVoiceSend = 60;

	/// <summary>
	/// Called each frame that a client wants to broadcast their voice
	/// </summary>
	internal void SetBroadcastVoice()
	{
		timeSinceWantVoiceSend = 0;
	}

	public struct Entry
	{
		private Lobby x;

		public readonly string Name => x.GetData( "name" );
		public readonly int Members => x.MemberCount;
		public readonly bool IsFull => x.MemberCount >= x.MaxMembers;
		public readonly long OwnerId => x.GetData( "_ownerid" ).ToLong( 0 );
		public readonly bool IsPlaying => !string.IsNullOrWhiteSpace( x.GetData( "gameaddress" ) );
		public readonly string Package => x.GetData( "package" );
		public readonly string GameTitle => x.GetData( "packagetitle" );

		internal Entry( Lobby x )
		{
			this.x = x;
		}

		public async Task Join()
		{
			var result = await x.Join();
			if ( result != Steamworks.RoomEnter.Success )
			{
				Log.Warning( $"Failed to join lobby for party ({result})" );
				return;
			}

			Current?.Leave();

			Current = new PartyRoom( x );
		}
	}
}
