namespace Sandbox.Platform;

/// <summary>
/// The game's voice chat. Without <see cref="Voice"/> components everyone can hear everyone.
/// Once the scene has any, only people talking through one are heard.
/// </summary>
[Expose]
internal sealed class VoiceChatSystem : GameObjectSystem<VoiceChatSystem>
{
	/// <summary>
	/// Every <see cref="Voice"/> in the scene. Disabled ones too, they still mean the game uses them.
	/// </summary>
	private readonly HashSet<Voice> _voices = new();

	/// <summary>
	/// Playback for people talking to everyone. A <see cref="Voice"/> plays its own.
	/// </summary>
	private readonly Dictionary<Connection, VoiceChat.Speaker> _speakers = new();

	/// <summary>
	/// The local player's <see cref="Voice"/>, or null if they don't have one.
	/// </summary>
	internal Voice LocalVoice { get; private set; }

	public VoiceChatSystem( Scene scene ) : base( scene )
	{
		// Servers don't capture or play voice, and never tick this to forget destroyed Voices
		if ( scene.IsEditor || Application.IsHeadless ) return;

		// Systems get recreated on hotload, so pick up any Voices that already exist
		foreach ( var component in scene.Directory.AllComponents )
		{
			OnComponentAdded( component );
		}

		scene.Directory.OnComponentAdded += OnComponentAdded;
	}

	private void OnComponentAdded( Component component )
	{
		if ( component is Voice voice )
		{
			_voices.Add( voice );
		}
	}

	/// <summary>
	/// The player hasn't turned voice chat off.
	/// </summary>
	private bool IsActive => !Scene.IsEditor && Preferences.VoiceEnabled;

	/// <summary>
	/// Once the scene has a <see cref="Voice"/>, enabled or not, nobody talks to everyone
	/// and the game shows its own list of who's talking.
	/// </summary>
	internal bool UsesVoiceComponents => _voices.Count > 0;

	/// <summary>
	/// Can people without a <see cref="Voice"/> talk to everyone? Only if the game allows it and doesn't use Voice components.
	/// </summary>
	private bool CanTalkToEveryone => VoiceChat.Enabled && !UsesVoiceComponents;

	/// <summary>
	/// Can the local player's voice go out right now, and through which <see cref="Voice"/>?
	/// Null means to everyone.
	/// </summary>
	private bool CanSend( out Voice voice )
	{
		voice = LocalVoice;

		if ( !IsActive ) return false;

		// A disabled Voice mutes its owner

		if ( voice is not null ) return voice.Active;

		// Talking to everyone is for multiplayer games

		return CanTalkToEveryone && Networking.System is not null;
	}

	/// <summary>
	/// Does the local player want to send their voice right now?
	/// </summary>
	internal bool IsListening => CanSend( out var voice ) && (voice?.IsListening ?? VoiceChat.CanListen());

	internal void Tick()
	{
		_voices.RemoveWhere( x => !x.IsValid );

		UpdateLocalVoice();

		foreach ( var (connection, speaker) in _speakers )
		{
			if ( !speaker.IsIdle ) continue;

			_speakers.Remove( connection );
			speaker.Dispose();
		}
	}

	/// <summary>
	/// Switch to whichever <see cref="Voice"/> the local player is talking through. Otherwise keep the last one,
	/// so the tail end of push-to-talk still goes out through it, or failing that any they own.
	/// </summary>
	private void UpdateLocalVoice()
	{
		if ( LocalVoice is { IsValid: false } or { IsProxy: true } )
		{
			LocalVoice = null;
		}

		foreach ( var voice in _voices )
		{
			if ( voice.IsProxy ) continue;

			if ( voice.Active && voice.IsListening )
			{
				LocalVoice = voice;
				return;
			}

			LocalVoice ??= voice;
		}
	}

	/// <summary>
	/// The loudest thing this connection is saying, or null if we haven't heard from them.
	/// </summary>
	internal VoiceChat.Speaker FindSpeaker( Connection connection )
	{
		if ( connection is null ) return null;

		var loudest = _speakers.GetValueOrDefault( connection );

		foreach ( var voice in _voices )
		{
			if ( voice.Speaker is not { } speaker ) continue;
			if ( voice.Owner != connection ) continue;
			if ( loudest is not null && loudest.Amplitude >= speaker.Amplitude ) continue;

			loudest = speaker;
		}

		return loudest;
	}

	internal static void NetworkInitialize( SceneNetworkSystem system )
	{
		system.AddHandler<VoiceChatBroadcastMsg>( OnReceivePacket );
	}

	private static void OnReceivePacket( VoiceChatBroadcastMsg msg, Connection source, Guid guid )
	{
		if ( Game.ActiveScene?.GetSystem<VoiceChatSystem>() is not { } system ) return;

		Voice voice = null;

		if ( msg.VoiceId != Guid.Empty )
		{
			// They talked through a Voice, so it only plays there. Not if it isn't networked to us,
			// it's turned off here, or it isn't theirs.

			voice = system.Scene.Directory.FindComponentByGuid( msg.VoiceId ) as Voice;

			if ( voice is not { Active: true } || voice.Owner != source ) return;
		}

		system.ReceivePacket( source, voice, msg.Packet );
	}

	internal void BroadcastPacket( ReadOnlySpan<byte> packet )
	{
		if ( !CanSend( out var voice ) ) return;

		Networking.System?.Broadcast( new VoiceChatBroadcastMsg
		{
			VoiceId = voice?.Id ?? Guid.Empty,
			// TODO: avoid allocation
			Packet = [.. packet]
		}, flags: NetFlags.UnreliableNoDelay, filter: voice?.GetBroadcastFilter() );

		// Loopback, so we can see and hear ourselves talking

		ReceivePacket( Connection.Local, voice, packet );
	}

	private void ReceivePacket( Connection source, Voice voice, ReadOnlySpan<byte> packet )
	{
		if ( !IsActive ) return;
		if ( Application.IsHeadless ) return;
		if ( source != Connection.Local && new Friend( source.SteamId ).IsBlocked ) return;

		var speaker = voice is null ? PlayToEveryone( source, packet ) : voice.Play( packet );

		if ( speaker is null ) return;

		VoiceChat.OnPacket?.Invoke( new VoiceChat.PacketEvent( source, speaker.SoundHandle ) );
	}

	private VoiceChat.Speaker PlayToEveryone( Connection source, ReadOnlySpan<byte> packet )
	{
		if ( !CanTalkToEveryone ) return null;

		if ( !_speakers.TryGetValue( source, out var speaker ) )
		{
			_speakers[source] = speaker = new VoiceChat.Speaker( isLoopback: source == Connection.Local );
		}

		return speaker.WriteVoiceData( packet ) ? speaker : null;
	}

	public override void Dispose()
	{
		foreach ( var speaker in _speakers.Values )
		{
			speaker.Dispose();
		}

		_speakers.Clear();

		if ( Scene?.Directory is { } directory )
		{
			directory.OnComponentAdded -= OnComponentAdded;
		}

		base.Dispose();
	}
}

/// <summary>
/// Net message broadcast from someone recording their voice.
/// </summary>
[Expose]
internal struct VoiceChatBroadcastMsg
{
	/// <summary>
	/// The sender's <see cref="Voice"/> to play this through, or empty if they're talking to everyone.
	/// </summary>
	public Guid VoiceId { get; set; }

	public byte[] Packet { get; set; }
}
