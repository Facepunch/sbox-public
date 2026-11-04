using NativeEngine;
using Sandbox.Engine;
using Sandbox.Platform;
using Steamworks;
using Steamworks.Data;
using System.Runtime.CompilerServices;

namespace Sandbox;

partial class PartyRoom
{
	/// <summary>
	/// Keep sending for a bit after the last <see cref="SetBroadcastVoice"/>, it's called every tick.
	/// </summary>
	private const float VoiceSendTailTime = 0.1f;

	private readonly Dictionary<Friend, VoiceChat.Speaker> _voiceSpeakers = new();

	private RealTimeSince _timeSinceWantVoiceSend = float.PositiveInfinity;

	/// <summary>
	/// Party voice plays in the menu. Games have their own voice chat.
	/// </summary>
	internal bool IsVoiceActive => Current == this && IGameInstance.Current is null && Preferences.VoiceEnabled;

	/// <summary>
	/// Allow communication via voice when in the main menu.
	/// </summary>
	[Obsolete( $"Handled by {nameof( Platform.VoiceChat )}." )]
	public bool VoiceCommunicationAllowed => IsVoiceActive;

	/// <summary>
	/// Whether we are recording voice for the party.
	/// </summary>
	public bool VoiceRecording
	{
		get => IsVoiceListening;

		// This was public but was getting stomped during Tick if anyone set it

		[Obsolete( $"Use {nameof( SetBroadcastVoice )}" )]
		set
		{
			if ( value ) SetBroadcastVoice();
		}
	}

	internal bool IsVoiceListening
	{
		get
		{
			if ( !IsVoiceActive ) return false;
			if ( _timeSinceWantVoiceSend >= VoiceSendTailTime ) return false;

			// CanListen might check input, which needs the menu scope

			using var scope = GlobalContext.MenuScope();

			return VoiceChat.CanListen();
		}
	}

	// This seems odd, do we just want to truncate the ID?
	private int NetworkChannel => (int)(Id % int.MaxValue);

	public bool IsMemberTalking( Friend member ) => _voiceSpeakers.TryGetValue( member, out var speaker ) && speaker.IsTalking;

	/// <summary>
	/// Call every tick when you want the local user to be able to broadcast.
	/// We still check for voice preferences (push-to-talk etc).
	/// </summary>
	public void SetBroadcastVoice()
	{
		_timeSinceWantVoiceSend = 0;
	}

	private void TickVoice()
	{
		ReadVoiceChannel();

		foreach ( var (friend, speaker) in _voiceSpeakers )
		{
			if ( IsVoiceActive && !speaker.IsIdle ) continue;

			_voiceSpeakers.Remove( friend );
			speaker.Dispose();
		}
	}

	private void RemoveVoiceSpeakers()
	{
		foreach ( var speaker in _voiceSpeakers.Values )
		{
			speaker.Dispose();
		}

		_voiceSpeakers.Clear();
	}

	internal void BroadcastVoicePacket( ReadOnlySpan<byte> packet )
	{
		using var scope = GlobalContext.MenuScope();

		using var bs = ByteStream.Create( 32 );
		bs.Write( MessageIdentity.VoiceMessage );
		bs.WriteArray( packet );

		var flags = Defines.k_nSteamNetworkingSend_Reliable | Defines.k_nSteamNetworkingSend_AutoRestartBrokenSession;

		foreach ( var friend in Members )
		{
			if ( friend.IsMe ) continue;

			unsafe
			{
				fixed ( byte* pData = bs.ToSpan() )
				{
					Steam.SteamNetworkingMessages().SendMessageToUser( friend.Id, (IntPtr)pData, bs.Length, flags, NetworkChannel );
				}
			}
		}

		ReceivePacket( new Friend( SteamClient.SteamId ), packet );
	}

	private unsafe void ReadVoiceChannel()
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
				var msg = Unsafe.Read<Network.SteamNetworkMessage>( (void*)ptr[i] );
				var friend = new Friend( msg.IdentitySteamId );

				try
				{
					if ( !Members.Any( x => x.Id == friend.Id ) )
					{
						Log.Trace( $"Dropping message from {friend} not in party" );
						continue;
					}

					using var data = new ByteStream( msg.Data, msg.Size );

					var iMessageType = data.Read<MessageIdentity>();

					if ( iMessageType == MessageIdentity.VoiceMessage )
					{
						var voicePacket = data.ReadArraySpan<byte>( VoiceManager.MaxPacketLength );

						ReceivePacket( friend, voicePacket );
					}
				}
				finally
				{
					net.ReleaseMessage( ptr[i] );
				}
			}
		}
	}

	private void ReceivePacket( Friend friend, ReadOnlySpan<byte> packet )
	{
		PlayVoice( friend, packet );

		// TODO: having to allocate a byte[] sucks! Can we change this event to accept ReadOnlyMemory<byte>?

		var voicePacketBytes = packet.ToArray();

		// TODO: do we need these any more?

		OnVoiceData?.Invoke( friend, voicePacketBytes );

		using ( GlobalContext.MenuScope() )
		{
			Event.EventSystem.RunInterface<IEventListener>( x => x.OnVoiceMessage( friend, voicePacketBytes ) );
		}
	}

	private void PlayVoice( Friend friend, ReadOnlySpan<byte> packet )
	{
		if ( !IsVoiceActive ) return;
		if ( Application.IsHeadless ) return;

		if ( !_voiceSpeakers.TryGetValue( friend, out var speaker ) )
		{
			if ( friend.IsBlocked ) return;

			_voiceSpeakers[friend] = speaker = new VoiceChat.Speaker( isLoopback: friend.IsMe );
		}

		speaker.WriteVoiceData( packet );
	}
}
