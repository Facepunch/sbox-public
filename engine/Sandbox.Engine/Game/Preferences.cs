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

	[ConVar( "controller_analog_speed", ConVarFlags.Protected, Help = "How fast the left joystick moves, for stuff like the virtual cursor in menus", Saved = true, Min = 0.1f, Max = 360.0f )]
	internal static float ControllerAnalogSpeed { get; set; } = 2.0f;

	[ConVar( "mouse_pitch_inverted", ConVarFlags.Protected, Help = "Whether the mouse's pitch should be inverted or not", Saved = true, Min = 0, Max = 1 )]
	public static bool InvertMousePitch { get; internal set; } = false;

	[ConVar( "mouse_yaw_inverted", ConVarFlags.Protected, Help = "Whether the mouse's yaw should be inverted or not", Saved = true, Min = 0, Max = 1 )]
	public static bool InvertMouseYaw { get; internal set; } = false;

}
