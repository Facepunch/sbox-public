/// <summary>
/// Resolves phrase references at the point of display, including data received from the host.
/// Plain text such as workshop titles and player names is kept as supplied.
/// </summary>
internal static class LocalizedText
{
	/// <summary>
	/// Resolves a hash-prefixed phrase and its named parameters using the local player's language.
	/// Parameter values can themselves be hash-prefixed phrase references.
	/// </summary>
	public static string Resolve( string text, Dictionary<string, string> tokens = null )
	{
		if ( string.IsNullOrEmpty( text ) || !text.StartsWith( '#' ) )
		{
			return text;
		}

		if ( text.StartsWith( "##", StringComparison.Ordinal ) )
		{
			return text[1..];
		}

		Dictionary<string, object> data = null;

		if ( tokens is not null )
		{
			data = new();

			foreach ( var token in tokens )
			{
				data[token.Key] = Resolve( token.Value );
			}
		}

		return Game.Language.GetPhrase( text[1..], data );
	}

	/// <summary>
	/// Escapes user-supplied parameter text so a leading hash cannot become a phrase reference.
	/// </summary>
	public static string Literal( string text )
	{
		return text?.StartsWith( '#' ) == true ? $"#{text}" : text;
	}

	/// <summary>
	/// Localizes an input title inside the game while keeping the input configuration readable
	/// by the engine's settings menu, which does not load this game's phrase catalogs.
	/// </summary>
	public static string InputTitle( InputAction action )
	{
		return ResolveInput( "action", action.Name, string.IsNullOrEmpty( action.Title ) ? action.Name : action.Title );
	}

	/// <summary>
	/// Localizes the display name of an input group without changing its identifier.
	/// </summary>
	public static string InputGroup( string group )
	{
		return ResolveInput( "group", group, group );
	}

	/// <summary>
	/// Returns a phrase reference for built-in resource types while preserving custom type names.
	/// Editor asset metadata remains readable outside the game's localization context.
	/// </summary>
	public static string ResourceTypeName( AssetTypeAttribute assetType, string fallback = null )
	{
		return ResourceTypeName( assetType?.Extension, assetType?.Name ?? fallback ?? "#ui.resource.title" );
	}

	/// <summary>
	/// The same by extension, for types without an asset type like materials.
	/// </summary>
	public static string ResourceTypeName( string extension, string fallback )
	{
		return extension?.TrimStart( '.' ).ToLowerInvariant() switch
		{
			"material" or "vmat" => "#ui.resource.type.material",
			"bdef" => "#tool.name.balloon",
			"btndef" => "#tool.name.button",
			"decal" => "#tool.name.decal",
			"hdef" => "#tool.name.hoverball",
			"ldef" => "#tool.setting.line",
			"sndef" => "#tool.setting.group.sound",
			"surface" => "#ui.resource.type.surface",
			"sent" => "#entity.property.entity",
			"semit" => "#tool.setting.effect",
			"smemit" => "#entity.name.emitter",
			"spp" => "#tool.setting.effect",
			"tdef" => "#tool.name.thruster",
			"wdef" => "#tool.name.wheel",
			_ => fallback
		};
	}

	private static string ResolveInput( string kind, string name, string fallback )
	{
		if ( string.IsNullOrEmpty( name ) ) return fallback;

		var key = $"input.{kind}.{name.ToLowerInvariant()}";
		var translated = Game.Language.GetPhrase( key );
		return translated == key ? Resolve( fallback ) : translated;
	}
}
