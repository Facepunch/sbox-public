using Sandbox.Localization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// A compiled .lang resource containing translations keyed by token, then language code.
/// </summary>
[AssetType( Name = "Language", Extension = "lang", Category = "Localization", Flags = AssetTypeFlags.NoEmbedding )]
public sealed class LanguageResource : GameResource
{
	/// <summary>
	/// Optional prefix shared by all tokens in this file, such as "weapon.flashlight".
	/// </summary>
	[JsonPropertyName( "@prefix" )]
	public string Prefix { get; set; }

	/// <summary>
	/// Each token's translations and optional @context note for translators.
	/// These entries are stored directly at the root of the .lang file.
	/// </summary>
	[JsonIgnore]
	public Dictionary<string, Dictionary<string, string>> Tokens { get; set; } = new();

	/// <inheritdoc />
	protected override void OnJsonDeserialize( JsonObject node )
	{
		var tokens = new Dictionary<string, Dictionary<string, string>>();
		foreach ( var (token, value) in node )
		{
			if ( token.Equals( "@prefix", StringComparison.OrdinalIgnoreCase ) || token.StartsWith( "__", StringComparison.Ordinal ) )
				continue;

			var translations = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
			foreach ( var (language, text) in value.AsObject() )
			{
				translations[language] = text?.GetValue<string>() ?? throw new JsonException( $"Expected text for '{token}' in '{language}'." );
			}

			tokens[token] = translations;
		}

		// A reload can remove the prefix as well as individual tokens.
		Prefix = null;
		Tokens = tokens;
	}

	/// <inheritdoc />
	protected override void OnJsonSerialize( JsonObject node )
	{
		if ( string.IsNullOrEmpty( Prefix ) )
			node.Remove( "@prefix" );

		foreach ( var (token, translations) in Tokens )
		{
			node[token] = Json.ToNode( translations );
		}
	}

	internal void AddPhrases( string language, PhraseCollection catalog )
	{
		if ( language.Equals( "@context", StringComparison.OrdinalIgnoreCase ) )
			return;

		var prefix = Prefix?.Trim().TrimEnd( '.' );
		foreach ( var (token, translations) in Tokens )
		{
			if ( translations.TryGetValue( language, out var text ) )
			{
				catalog.Set( string.IsNullOrEmpty( prefix ) ? token : $"{prefix}.{token}", text );
			}
		}
	}

	/// <inheritdoc />
	protected override void PostLoad() => Game.Language?.Refresh();

	/// <inheritdoc />
	protected override void PostReload() => Game.Language?.Refresh();

	/// <inheritdoc />
	protected override void OnDestroy() => Game.Language?.Refresh();

	/// <inheritdoc />
	protected override Bitmap CreateAssetTypeIcon( int width, int height ) => CreateSimpleAssetTypeIcon( "translate", width, height );
}
