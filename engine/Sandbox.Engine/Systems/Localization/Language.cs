using Sandbox.Engine;

namespace Sandbox;

/// <summary>
/// A container for the current language, allowing access to translated phrases and language information.
/// </summary>
public class LanguageContainer
{
	/// <summary>
	/// The abbreviation for the language the user wants. This is set by the user in the options menu.
	/// </summary>
	public string SelectedCode => Application.LanguageCode;

	/// <summary>
	/// Information about the current selected language. Will default to English if the current language isn't found.
	/// </summary>
	public Sandbox.Localization.LanguageInformation Current { get; internal set; }

	/// <summary>
	/// FileSystem used for localization.
	/// </summary>
	internal BaseFileSystem FileSystem { get; private set; }

	Sandbox.Localization.PhraseCollection lang;
	FileWatch _watcher;

	internal LanguageContainer()
	{
		FileSystem = new AggregateFileSystem();

		_watcher = FileSystem.Watch();
		_watcher.OnChanges += x => Refresh();
	}

	string _previousLanguage;
	Localization.PhraseCollection _previousFallback;
	static Localization.PhraseCollection GameFallback => Game.IsMenu ? GlobalContext.Game.Language?.lang : null;

	internal void Tick()
	{
		var language = (SelectedCode ?? "en").ToLower();
		var fallback = GameFallback;

		if ( _previousLanguage != language )
		{
			_previousLanguage = language;

			lang = new Localization.PhraseCollection();
			AddFromPath( "en" );
			AddFromPath( language );
		}
		else if ( _previousFallback == fallback )
		{
			return;
		}

		// A game catalog reload or unload also invalidates translated menu labels.
		_previousFallback = fallback;
		GlobalContext.Current.UISystem.OnLanguageChanged();
	}

	void AddFromPath( string shortName )
	{
		if ( string.IsNullOrWhiteSpace( shortName ) ) return;
		if ( shortName.Contains( "." ) ) return;
		if ( shortName.Contains( ":" ) ) return;
		if ( shortName.Contains( "/" ) ) return;

		var language = Sandbox.Localization.Languages.Find( shortName );
		if ( language != null ) Current = language;


		foreach ( var file in FileSystem.FindFile( shortName, "*.json" ) )
		{
			AddFile( $"{shortName}/{file}" );
		}

	}

	void AddFile( string path )
	{
		try
		{
			var entries = FileSystem.ReadJson<Dictionary<string, string>>( path );
			if ( entries == null ) return;

			foreach ( var entry in entries )
			{
				lang.Set( entry.Key, entry.Value );
			}

		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't read localization file {path}: {e}" );
		}
	}

	/// <summary>
	/// Called when file(s) have changed and we should reload next tick
	/// </summary>
	internal void Refresh()
	{
		// setting this to null will cause a reload in tick
		_previousLanguage = null;
	}

	/// <summary>
	/// Look up a phrase
	/// </summary>
	/// <param name="textToken">The token used to identify the phrase</param>
	/// <param name="data">Key values of data used by the string. Example: {Variable} -> { "Variable", someVar }</param>
	/// <returns>If found will return the phrase, else will return the token itself</returns>
	public string GetPhrase( string textToken, Dictionary<string, object> data = null )
	{
		if ( lang == null || Language.DisplayKeys )
			return textToken;

		return lang.GetPhrase( textToken, data );
	}

	/// <summary>
	/// Resolves a UI token in the current catalog, with game phrases available to the menu on a miss.
	/// </summary>
	internal static string GetUiPhrase( string token )
		=> Language.DisplayKeys ? token : Localization.PhraseCollection.Resolve( token, Game.Language?.lang, GameFallback );

	internal void Shutdown()
	{
		_watcher?.Dispose();
		_watcher = null;

		FileSystem?.Dispose();
		FileSystem = null;
	}
}

/// <summary>
/// Allows access to translated phrases, allowing the translation of gamemodes etc
/// </summary>
[SkipHotload]
public static class Language
{
	[ConVar( "lang.showkeys", Help = "Show keys/phrases instead of translated text. Useful for debugging localization." )]
	internal static bool DisplayKeys
	{
		get => field;
		set
		{
			field = value;

			// trigger labels etc to update
			GlobalContext.Current.UISystem?.OnLanguageChanged();
		}
	} = false;

	/// <summary>
	/// The abbreviation for the language the user wants. This is set by the user in the options menu.
	/// </summary>
	public static string SelectedCode => Game.Language.SelectedCode;

	/// <summary>
	/// Information about the current selected language. Will default to English if the current language isn't found.
	/// </summary>
	public static Sandbox.Localization.LanguageInformation Current => Game.Language.Current;

	/// <summary>
	/// Look up a phrase
	/// </summary>
	/// <param name="textToken">The token used to identify the phrase</param>
	/// <param name="data">Key values of data used by the string. Example: {Variable} -> { "Variable", someVar }</param>
	/// <returns>If found will return the phrase, else will return the token itself</returns>
	public static string GetPhrase( string textToken, Dictionary<string, object> data = null ) => Game.Language.GetPhrase( textToken, data );

}
