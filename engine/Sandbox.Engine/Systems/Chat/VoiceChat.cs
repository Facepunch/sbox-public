using Sandbox.Engine;

namespace Sandbox.Platform;

/// <summary>
/// Platform-level voice chat. Works without any setup: everyone in a game can hear each other.
/// Once the scene has <see cref="Voice"/> components, only players talking through one are heard,
/// and their Voice decides how it's captured, routed and played back.
/// </summary>
[Expose]
public static partial class VoiceChat
{
	/// <summary>
	/// Called whenever we receive someone's voice.
	/// </summary>
	public readonly record struct PacketEvent( Connection Sender, SoundHandle Sound );

	/// <inheritdoc cref="PlatformSettings.VoiceChatEnabled"/>
	public static bool Enabled => ProjectSettings.Platform.VoiceChatEnabled;

	/// <inheritdoc cref="PlatformSettings.VoiceChatShowSpeakers"/>
	public static bool ShowSpeakers => ProjectSettings.Platform.VoiceChatShowSpeakers && GameVoice is not { UsesVoiceComponents: true };

	/// <summary>
	/// Is this connection's voice playing right now? Works whether or not the game uses <see cref="Voice"/> components.
	/// </summary>
	public static bool IsTalking( Connection connection )
	{
		return GameVoice?.FindSpeaker( connection ) is { IsTalking: true };
	}

	/// <summary>
	/// How loud this connection's voice is right now, in decibels, 0 being full scale.
	/// This is the level they sent, before volume or distance. -96 when they're not talking.
	/// </summary>
	public static float GetLevel( Connection connection )
	{
		return AmplitudeToLevel( GameVoice?.FindSpeaker( connection )?.Amplitude ?? 0f );
	}

	internal static float AmplitudeToLevel( float amplitude )
	{
		if ( amplitude <= 0f ) return VoiceManager.MinLevel;

		return MathF.Max( 20f * MathF.Log10( amplitude ), VoiceManager.MinLevel );
	}

	internal static Action<PacketEvent> OnPacket;

	/// <summary>
	/// Party voice, while we're in the menu with a party.
	/// </summary>
	private static PartyRoom PartyVoice => PartyRoom.Current is { IsVoiceActive: true } party ? party : null;

	/// <summary>
	/// Voice chat for the game we're in. Asks the game's scene directly, the menu overlay calls in from menu scope.
	/// </summary>
	private static VoiceChatSystem GameVoice => GlobalContext.Game.ActiveScene?.GetSystem<VoiceChatSystem>();

	/// <summary>
	/// Does whoever we're talking to want the microphone right now?
	/// </summary>
	internal static bool IsListening => PartyVoice?.IsVoiceListening ?? GameVoice?.IsListening ?? false;

	/// <summary>
	/// Check player preferences for voice chat being enabled, which <see cref="VoiceMode"/> is active,
	/// and if the push-to-talk input is pressed if that mode is active.
	/// </summary>
	internal static bool CanListen()
	{
		return Preferences.VoiceEnabled && Preferences.VoiceMode switch
		{
			VoiceMode.Disabled => false,
			VoiceMode.OpenMicrophone => true,
			_ => Input.Down( "voice" )
		};
	}

	/// <summary>
	/// Register network message handlers on the given network system.
	/// Called once during NetworkSystem construction.
	/// </summary>
	internal static void NetworkInitialize( SceneNetworkSystem system )
	{
		VoiceChatSystem.NetworkInitialize( system );
	}

	internal static void Tick()
	{
		GameVoice?.Tick();
		VoiceManager.Tick();
	}

	internal static void BroadcastPacket( ReadOnlySpan<byte> packet )
	{
		if ( PartyVoice is { } party )
		{
			party.BroadcastVoicePacket( packet );
			return;
		}

		GameVoice?.BroadcastPacket( packet );
	}
}
