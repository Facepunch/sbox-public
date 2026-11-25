using Sandbox.Services;
using Steamworks;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Sandbox;

internal static partial class Workshop
{
	/// <summary>
	/// Filename for the server workshop item list
	/// </summary>
	internal const string ServerItemsFile = "workshop.json";

	/// <summary>
	/// A Workshop collection a dedicated server uses.
	/// </summary>
	[ConVar( "host_workshop_collection", ConVarFlags.Protected, Help = "Workshop collection ID the dedicated server uses" )]
	internal static string HostWorkshopCollection { get; set; }

	/// <summary>
	/// How deep collections can link to other collections before the rest are ignored
	/// </summary>
	const int MaxCollectionDepth = 8;

	/// <summary>
	/// What a dedicated server has in place of subscriptions: the items in <see cref="HostWorkshopCollection"/>
	/// and in the <see cref="ServerItemsFile"/> config file, with collections expanded, in that order.
	/// </summary>
	internal static async Task<Item[]> GetServerItemsAsync( CancellationToken token = default )
	{
		var ids = new List<ulong>();

		if ( !string.IsNullOrWhiteSpace( HostWorkshopCollection ) )
		{
			if ( TryParseItemId( HostWorkshopCollection, out var collection ) )
				ids.Add( collection );
			else
				Log.Warning( $"host_workshop_collection: '{HostWorkshopCollection}' isn't a Workshop ID" );
		}

		ids.AddRange( ReadServerItemsFile( EngineFileSystem.Config ) );

		if ( ids.Count == 0 ) return [];

		// The game server answers Workshop queries once it's logged on. Without that, they'd go nowhere.
		if ( !await Network.DedicatedServer.LogOnToSteamAsync() )
		{
			Log.Warning( "Couldn't log on to Steam, so the server has no Workshop items" );
			return [];
		}

		return await ExpandCollectionsAsync( ids, x => FetchAsync( x, token ), token );
	}

	/// <summary>
	/// The items <paramref name="ids"/> name, with collections (and collections in them) replaced by what's in them.
	/// Each item appears once, in the order it's first named.
	/// </summary>
	internal static async Task<Item[]> ExpandCollectionsAsync( IReadOnlyList<ulong> ids, Func<IReadOnlyCollection<ulong>, Task<Item[]>> fetch, CancellationToken token = default )
	{
		var seen = new HashSet<ulong>();
		var items = new List<Item>();

		async Task VisitAsync( IReadOnlyList<ulong> level, int depth )
		{
			// Ask about the whole level at once, then go through it in order
			var unseen = level.Where( x => x != 0 && !seen.Contains( x ) ).Distinct().ToArray();
			if ( unseen.Length == 0 ) return;

			token.ThrowIfCancellationRequested();

			var found = new Dictionary<ulong, Item>();
			foreach ( var item in await fetch( unseen ) ?? [] )
			{
				if ( item is not null ) found.TryAdd( item.Id, item );
			}

			foreach ( var id in unseen )
			{
				if ( !seen.Add( id ) ) continue;

				if ( !found.TryGetValue( id, out var item ) )
				{
					Log.Warning( $"Workshop item {id} doesn't exist, or the Workshop couldn't be asked about it" );
					continue;
				}

				if ( item.FileType != WorkshopFileType.Collection )
				{
					items.Add( item );
					continue;
				}

				if ( depth >= MaxCollectionDepth )
				{
					Log.Warning( $"Skipping Workshop collection {item}: collections are nested too deeply" );
					continue;
				}

				await VisitAsync( item.Children ?? [], depth + 1 );
			}
		}

		await VisitAsync( ids, 0 );
		return items.ToArray();
	}

	/// <summary>
	/// The Workshop IDs listed in the server's <see cref="ServerItemsFile"/>, if it has one
	/// </summary>
	internal static IEnumerable<ulong> ReadServerItemsFile( BaseFileSystem config )
	{
		try
		{
			if ( config?.FileExists( ServerItemsFile ) != true ) return [];

			return ParseServerItems( config.ReadAllText( ServerItemsFile ) );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Couldn't read config/{ServerItemsFile}: {e.Message}" );
			return [];
		}
	}

	static readonly JsonDocumentOptions ServerItemsJson = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

	/// <summary>
	/// Read a Workshop item list: a JSON array of item and collection IDs, e.g. <c>[ 3312345678, 3398765432 ]</c>.
	/// IDs may also be written as strings. Entries that aren't IDs are skipped with a warning.
	/// </summary>
	internal static List<ulong> ParseServerItems( string text )
	{
		var ids = new List<ulong>();
		if ( string.IsNullOrWhiteSpace( text ) ) return ids;

		try
		{
			using var doc = JsonDocument.Parse( text, ServerItemsJson );
			if ( doc.RootElement.ValueKind != JsonValueKind.Array )
			{
				Log.Warning( $"config/{ServerItemsFile}: expected a JSON array of Workshop IDs" );
				return ids;
			}

			foreach ( var element in doc.RootElement.EnumerateArray() )
			{
				ulong id = 0;
				var valid = element.ValueKind switch
				{
					JsonValueKind.Number => element.TryGetUInt64( out id ) && id != 0,
					JsonValueKind.String => TryParseItemId( element.GetString(), out id ),
					_ => false
				};

				if ( valid )
					ids.Add( id );
				else
					Log.Warning( $"config/{ServerItemsFile}: {element.GetRawText()} isn't a Workshop ID" );
			}
		}
		catch ( JsonException e )
		{
			Log.Warning( $"config/{ServerItemsFile} isn't valid JSON: {e.Message}" );
		}

		return ids;
	}

	/// <summary>
	/// A Workshop ID - a non-zero number
	/// </summary>
	internal static bool TryParseItemId( string value, out ulong id )
	{
		return ulong.TryParse( value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id ) && id != 0;
	}
}
