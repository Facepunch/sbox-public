namespace Sandbox.Localization;

/// <summary>
/// Holds a bunch of localized phrases
/// </summary>
public class PhraseCollection
{
	internal Dictionary<string, Phrase> Phrases { get; } = new Dictionary<string, Phrase>( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Add a phrase to the language
	/// </summary>
	public void Set( string key, string value )
	{
		Phrases[key] = new Phrase( value );
	}

	/// <summary>
	/// Get a simple phrase from the language
	/// </summary>
	public string GetPhrase( string phrase, Dictionary<string, object> data = null )
		=> Resolve( phrase, this, null, data );

	internal static string Resolve( string key, PhraseCollection primary, PhraseCollection fallback, Dictionary<string, object> data = null )
	{
		var phrase = primary?.Phrases.GetValueOrDefault( key ) ?? fallback?.Phrases.GetValueOrDefault( key );
		return phrase?.Render( data ) ?? key;
	}
}
