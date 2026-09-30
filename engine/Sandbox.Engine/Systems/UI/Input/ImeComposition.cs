namespace Sandbox.UI;

/// <summary>
/// Turns the bare composition strings an IME sends into the events panels expect - onimestart
/// the first time text appears, onime for each preview, onimeend when it clears. Committed text
/// never comes through here, it arrives as ordinary typed text.
/// </summary>
internal static class ImeComposition
{
	/// <summary>
	/// Feed the current composition string. Returns whether a composition is now in flight.
	/// </summary>
	public static bool Update( Panel focus, bool composing, string text )
	{
		if ( !string.IsNullOrEmpty( text ) )
		{
			if ( !composing )
				focus?.CreateEvent( "onimestart" );

			focus?.CreateEvent( "onime", text );
			return true;
		}

		if ( composing )
		{
			focus?.CreateEvent( "onime", "" );
			focus?.CreateEvent( "onimeend" );
		}

		return false;
	}
}
