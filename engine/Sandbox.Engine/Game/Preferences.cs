namespace Sandbox;


/// <summary>
/// Holds information about the current user's preferences.
/// </summary>
public static class Preferences
{
	/// <summary>
	/// The user's preference for their field of view
	/// </summary>
	[ConVar( "default_fov", ConVarFlags.Protected, Help = "Default field of view", Min = 40.0f, Max = 120.0f, Saved = true )]
	public static float FieldOfView { get; internal set; } = 90.0f;

	/// <summary>
	/// The user's preferred Music volume, as set in the options, clamped between 0 and 1
	/// </summary>
	[ConVar( "music_volume", ConVarFlags.Protected, Help = "Music volume", Min = 0.0f, Saved = true )]
	public static float MusicVolume { get; internal set; } = 1.0f;

	/// <summary>
	/// The user's preferred volume for the game's own sounds (the Game mixer), as set in the options, clamped between 0 and 1
	/// </summary>
	[ConVar( "game_volume", ConVarFlags.Protected, Help = "Game volume", Min = 0.0f, Saved = true )]
	public static float GameVolume { get; internal set; } = 1.0f;

	/// <summary>
	/// The user's preferred VOIP volume, as set in the options, clamped between 0 and 1
	/// </summary>
	[ConVar( "voip_volume", ConVarFlags.Protected, Help = "Voice chat volume", Min = 0.0f, Saved = true )]
	public static float VoipVolume { get; internal set; } = 1.0f;

	/// <summary>
	/// The mouse sensitivity
	/// </summary>
	[ConVar( "sensitivity", ConVarFlags.Protected, Help = "Mouse sensitivity", Saved = true )]
	public static float Sensitivity { get; internal set; } = 8;

	[ConVar( "controller_look_speed_yaw", ConVarFlags.Protected, Help = "How fast the camera turns horizontally when the stick is pushed all the way left/right (deg/s)", Saved = true, Min = 0.1f, Max = 360.0f )]
	public static float ControllerLookYawSpeed { get; internal set; } = 270.0f;

	[ConVar( "controller_look_speed_pitch", ConVarFlags.Protected, Help = "How fast the camera turns vertically when the stick is pushed all the way up/down (deg/s)", Saved = true, Min = 0.1f, Max = 360.0f )]
	public static float ControllerLookPitchSpeed { get; internal set; } = 160.0f;

	/// <summary>
	/// The percentage of joystick travel ignored around the neutral position.
	/// </summary>
	[ConVar( "controller_joystick_deadzone", ConVarFlags.Protected, Help = "Percentage of joystick travel ignored around the neutral position", Saved = true, Min = 0.0f, Max = 50.0f )]
	public static float ControllerJoystickDeadzone { get; internal set; } = 12.5f;

	[ConVar( "controller_analog_speed", ConVarFlags.Protected, Help = "How fast the left joystick moves, for stuff like the virtual cursor in menus", Saved = true, Min = 0.1f, Max = 360.0f )]
	internal static float ControllerAnalogSpeed { get; set; } = 2.0f;

	[ConVar( "mouse_pitch_inverted", ConVarFlags.Protected, Help = "Whether the mouse's pitch should be inverted or not", Saved = true, Min = 0, Max = 1 )]
	public static bool InvertMousePitch { get; internal set; } = false;

	[ConVar( "mouse_yaw_inverted", ConVarFlags.Protected, Help = "Whether the mouse's yaw should be inverted or not", Saved = true, Min = 0, Max = 1 )]
	public static bool InvertMouseYaw { get; internal set; } = false;

	/// <summary>
	/// Voice chat activation mode.
	/// </summary>
	[ConVar( "voip_mode", ConVarFlags.Protected, Help = "Voice chat mode: PushToTalk, OpenMicrophone, or Disabled", Saved = true )]
	public static VoiceMode VoiceMode { get; internal set; } = VoiceMode.PushToTalk;

	/// <summary>
	/// Keep the recording device open between push-to-talk presses, in case opening it causes a delay.
	/// </summary>
	[ConVar( "voip_streamlined", ConVarFlags.Protected, Help = "Keep the recording device open between push-to-talk presses, in case opening it causes a delay", Saved = true )]
	public static bool StreamlinedPushToTalk { get; internal set; }

	/// <summary>
	/// Name of the microphone used for voice chat, empty for the system default.
	/// </summary>
	[ConVar( "voip_device", ConVarFlags.Protected, Help = "Microphone used for voice chat, empty for the system default", Saved = true )]
	public static string VoiceDevice { get; internal set; } = "";

	/// <summary>
	/// Voice is only sent while the microphone is louder than this, in decibels.
	/// </summary>
	[ConVar( "voip_threshold", ConVarFlags.Protected, Help = "Voice is only sent while the microphone is louder than this, in decibels", Min = -96.0f, Max = 0.0f, Saved = true )]
	public static float VoiceThreshold { get; internal set; } = -50.0f;

	/// <summary>
	/// Automatically adjust the microphone's volume so voice is sent at a consistent level.
	/// </summary>
	[ConVar( "voip_agc", ConVarFlags.Protected, Help = "Automatically adjust the microphone's volume so voice is sent at a consistent level", Saved = true )]
	public static bool VoiceAutomaticGainControl { get; internal set; } = true;

	/// <summary>
	/// Show the microphone indicator while voice is being sent. Only the player can change this, games and servers can't.
	/// </summary>
	[ConVar( "voip_show_indicator", ConVarFlags.Protected, Help = "Show the microphone indicator while talking", Saved = true )]
	public static bool ShowVoiceIndicator { get; internal set; } = true;

	public static bool ChatEnabled
	{
		get => ChatEnabledSetting;
		[Obsolete( "Only the player can change this, in the settings menu" )]
		set { }
	}

	[ConVar( "platform.chat.enabled", ConVarFlags.Protected | ConVarFlags.Saved, Help = "Do we want to enable the platform level chat system? The game also has to have it enabled." )]
	internal static bool ChatEnabledSetting { get; set; } = true;

	/// <summary>
	/// Disabling this will turn off the UI, recording, and playback.
	/// </summary>
	public static bool VoiceEnabled
	{
		get => VoiceEnabledSetting;
		[Obsolete( "Only the player can change this, in the settings menu" )]
		set { }
	}

	[ConVar( "platform.voice.enabled", ConVarFlags.Protected | ConVarFlags.Saved, Help = "Do we want to enable the platform level voice chat system? The game also has to have it enabled." )]
	internal static bool VoiceEnabledSetting { get; set; } = true;

	/// <summary>
	/// When enabled, player usernames/avatars are replaced with anonymous versions.
	/// </summary>
	public static bool StreamerMode
	{
		get => StreamerModeSetting;
		[Obsolete( "Only the player can change this, in the settings menu" )]
		set { }
	}

	[ConVar( "streamer_mode", ConVarFlags.Protected | ConVarFlags.Saved, Help = "Hide usernames and profile pictures in the built-in chat behind anonymous aliases." )]
	internal static bool StreamerModeSetting { get; set; } = false;

	/// <summary>
	/// Whether the user wants to see subtitles for sounds that have them. Off by
	/// default - turned on in the audio settings.
	/// </summary>
	[ConVar( "snd_subtitles", ConVarFlags.Saved, Help = "Show subtitles for sounds that have them" )]
	public static bool Subtitles { get; set; } = false;
}

/// <summary>
/// Voice chat activation mode preference.
/// </summary>
public enum VoiceMode
{
	PushToTalk,
	OpenMicrophone,
	Disabled
}
