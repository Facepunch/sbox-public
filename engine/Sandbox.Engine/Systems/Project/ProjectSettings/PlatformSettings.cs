namespace Sandbox;

/// <summary>
/// Platform-level settings for a game project. These control engine-provided
/// features like text chat that exist outside of any specific scene.
/// </summary>
[Expose]
public class PlatformSettings : ConfigData
{
	/// <summary>
	/// Whether the built-in text chat system is enabled for this game.
	/// </summary>
	[Group( "Chat Settings" ), Title( "Enabled" )]
	public bool ChatEnabled
	{
		get
		{
			if ( Application.IsEditor )
				return field;

			// This will be false for published games that didn't include Platform.config 
			if ( !LoadedFromDisk )
				return false;

			return field;
		}
		set;
	} = true;

	/// <summary>
	/// Show the default chat UI overlay. When false, messages are still processed
	/// and events still fire, but the built-in overlay is hidden. Use this when
	/// implementing a custom chat UI.
	/// </summary>
	[Group( "Chat Settings" ), Title( "Show UI" )]
	public bool ChatShowUI { get; set; } = true;

	/// <summary>
	/// Maximum length of a single chat message in characters.
	/// </summary>
	[Group( "Chat Settings" )]
	[Range( 32, 256 ), Title( "Chat Max Length" )]
	public int ChatMaxMessageLength { get; set; } = 255;
}
