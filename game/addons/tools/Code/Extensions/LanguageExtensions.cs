namespace Editor;

public static class LanguageExtensions
{
	/// <summary>
	/// Resolves a localization token to the phrase for the current language if the string starts with a "#" character.
	/// Otherwise, returns the string as-is.
	/// </summary>
	public static string AutoLocalize( this string value )
	{
		if ( value is not null && value.Length > 1 && value[0] == '#' )
			return Language.GetPhrase( value[1..] );

		return value;
	}
}
