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
	public string GetPhrase( string phrase, Dictionary<string, object> data = null, bool returnSegment = false )
	{
		if ( !Phrases.TryGetValue( phrase, out var result ) )
		{
			if ( returnSegment )
			{
				var lastDotIndex = phrase.LastIndexOf( '.' );
				return lastDotIndex > 0 ? phrase[(lastDotIndex + 1)..] : phrase;
			}
			return phrase;
		}

		return result.Render( data );
	}
}
