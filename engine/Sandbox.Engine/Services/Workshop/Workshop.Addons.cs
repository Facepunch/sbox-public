using System.Threading;
using System.Text.Json;

namespace Sandbox;

internal static partial class Workshop
{
	static CookieContainer addonSettings;
	static CookieContainer AddonSettings => addonSettings ??= new( "workshop-addons", true );

	/// <summary>
	/// Subscribed addons targeting this game that the user has left enabled.
	/// </summary>
	internal static async Task<Item[]> GetEnabledAddonsAsync( string gameIdent, CancellationToken token = default )
	{
		var result = await new Query().WithSubscribed().WithType( ItemType.Addon )
			.WithTag( $"target_ident:{gameIdent}" ).RunAsync( token );

		var addons = new List<Item>();
		foreach ( var item in result.Items )
		{
			if ( !IsAddonEnabled( gameIdent, item.Id ) ) continue;

			// Validate the item itself; addons have no backend package to look up.
			if ( !IsAddonForGame( item, gameIdent ) )
			{
				Log.Warning( $"Skipping Workshop item {item.Id}: its metadata does not describe an addon for '{gameIdent}'." );
				continue;
			}

			addons.Add( item );
		}

		return addons.ToArray();
	}

	/// <summary>
	/// Verifies the Workshop item's own metadata, without resolving a backend package.
	/// </summary>
	internal static bool IsAddonForGame( Item item, string gameIdent )
	{
		if ( item is null || item.Id == 0 || item.Banned || !item.Tags.Contains( "addon", StringComparer.OrdinalIgnoreCase ) ) return false;

		try
		{
			return IsAddonMetadataForGame( JsonSerializer.Deserialize<WorkshopItemMetaData>( item.Metadata ?? "" ), gameIdent );
		}
		catch ( JsonException )
		{
			return false;
		}
	}

	/// <summary>
	/// Checks that addon metadata targets the current game.
	/// </summary>
	internal static bool IsAddonMetadataForGame( WorkshopItemMetaData? metadata, string gameIdent )
	{
		if ( metadata is not { } value || !string.Equals( value.TypeName, "addon", StringComparison.OrdinalIgnoreCase ) ) return false;

		string parent;
		try
		{
			using var meta = JsonDocument.Parse( value.Meta ?? "{}" );
			if ( meta.RootElement.ValueKind != JsonValueKind.Object
				|| !meta.RootElement.TryGetProperty( "ParentPackage", out var targetProperty )
				|| targetProperty.ValueKind != JsonValueKind.String ) return false;
			parent = targetProperty.GetString();
		}
		catch ( JsonException )
		{
			return false;
		}

		return Package.TryParseIdentParts( parent, out var target )
			&& Package.TryParseIdentParts( gameIdent, out var game )
			&& string.Equals( target.org, game.org, StringComparison.OrdinalIgnoreCase )
			&& string.Equals( target.package, game.package, StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>
	/// Whether a subscribed addon should load when hosting this game. Enabled by default.
	/// </summary>
	internal static bool IsAddonEnabled( string gameIdent, ulong id )
		=> AddonSettings.Get( $"{gameIdent.ToLowerInvariant()}.{id}", true );

	/// <summary>
	/// Remember an addon's enabled state for the next launch without changing its subscription.
	/// </summary>
	internal static void SetAddonEnabled( string gameIdent, ulong id, bool enabled )
	{
		AddonSettings.Set( $"{gameIdent.ToLowerInvariant()}.{id}", enabled );
		AddonSettings.Save();
	}
}
