namespace Sandbox;

using Sandbox.UI;
using System.Text.Json.Serialization;

public enum SpawnlistItemKind
{
	Item,
	Divider,
	Title
}

/// <summary>
/// A spawnlist item -- lots of cleanup needed, docs, etc
/// </summary>
public class SpawnlistItem : IMixedVirtualGridFullRow
{
	[JsonPropertyName( "id" )]
	public Guid Id { get; set; }

	[JsonPropertyName( "kind" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public SpawnlistItemKind Kind { get; set; }

	[JsonPropertyName( "ident" )]
	public string Ident { get; set; }

	[JsonPropertyName( "title" )]
	public string Title { get; set; }

	[JsonPropertyName( "icon" )]
	public string Icon { get; set; }

	[JsonIgnore]
	public bool IsContent => Kind == SpawnlistItemKind.Item;

	[JsonIgnore]
	public bool IsFullRow => !IsContent;

	[JsonIgnore]
	public float Height => Kind == SpawnlistItemKind.Title ? TitleHeight : DividerHeight;
	const float DividerHeight = 42f;
	const float TitleHeight = 48f;

	public static string MakeIdent( string type, string path, string source = "local" )
	{
		// TODO: hate this special case
		if ( type == "dupe" )
			return $"dupe.{source}:{path}";

		return $"{type}:{path}";
	}

	public static (string Type, string Path, string Source) ParseIdent( string ident )
	{
		if ( string.IsNullOrEmpty( ident ) )
			return (null, null, "local");

		var colonIndex = ident.IndexOf( ':' );
		if ( colonIndex < 0 )
			return (ident, ident, "local");

		var prefix = ident[..colonIndex];
		var data = ident[(colonIndex + 1)..];

		// TODO: hate this special case
		if ( prefix.StartsWith( "dupe." ) )
		{
			var source = prefix["dupe.".Length..];
			return ("dupe", data, source);
		}

		return (prefix, data, "local");
	}
}

public class SpawnlistData
{
	/// <summary>
	/// Raised whenever a new spawnlist is created, so UI can refresh without needing a panel ancestor walk.
	/// </summary>
	public static event Action SpawnlistCreated;

	/// <summary>
	/// The literal name chosen by the author. Only the initial fallback is localized.
	/// </summary>
	[JsonPropertyName( "name" )]
	public string Name { get; set; } = Game.Language.GetPhrase( "spawnmenu.spawnlist.untitled" );

	[JsonPropertyName( "description" )]
	public string Description { get; set; } = "";

	[JsonPropertyName( "items" )]
	public List<SpawnlistItem> Items { get; set; } = new();

	/// <summary>
	/// Creates a local list and optionally adds its first item before notifying the menus.
	/// </summary>
	public static SpawnlistData Create( string name, out Storage.Entry entry, SpawnlistItem initialItem = null )
	{
		var data = new SpawnlistData { Name = name };
		if ( initialItem is not null )
			data.Items.Add( initialItem );

		entry = Storage.CreateEntry( "spawnlist" );
		Save( entry, data );
		SpawnlistCreated?.Invoke();
		return data;
	}

	/// <summary>
	/// "1 item" or "N items", in the player's language.
	/// </summary>
	public static string ItemCountText( int count )
		=> Game.Language.GetPhrase( count == 1 ? "ui.spawnlist.one_item" : "ui.spawnlist.items", new() { { "count", count } } );

	public static void Save( Storage.Entry entry, SpawnlistData data )
	{
		Normalize( data, true );

		entry.Files.WriteJson( "/spawnlist.json", data );
		entry.SetMeta( "name", data.Name );
	}

	public static SpawnlistData Load( Storage.Entry entry )
	{
		if ( !entry.Files.FileExists( "/spawnlist.json" ) )
			return new SpawnlistData { Name = entry.GetMeta<string>( "name" ) ?? Game.Language.GetPhrase( "spawnmenu.spawnlist.untitled" ) };

		var data = entry.Files.ReadJson<SpawnlistData>( "/spawnlist.json" )
			?? new SpawnlistData();
		Normalize( data, !entry.Files.IsReadOnly );
		return data;
	}

	private static void Normalize( SpawnlistData data, bool assignIds )
	{
		data.Name ??= Game.Language.GetPhrase( "spawnmenu.spawnlist.untitled" );
		data.Items ??= new();
		if ( !assignIds ) return;

		foreach ( var item in data.Items )
		{
			if ( item.Id != Guid.Empty ) continue;
			item.Id = Guid.NewGuid();
		}
	}

	/// <summary>
	/// The player's writable local spawnlists, newest first.
	/// </summary>
	public static IEnumerable<Storage.Entry> GetAll()
	{
		return Storage.GetAll( "spawnlist" ).Where( x => !x.Files.IsReadOnly ).OrderByDescending( x => x.Created );
	}

	public static void Rename( Storage.Entry entry, string newName )
	{
		var data = Load( entry );
		data.Name = newName;
		Save( entry, data );
	}

	public static void Delete( Storage.Entry entry )
	{
		entry.Delete();
	}

	public static void AddItem( Storage.Entry entry, SpawnlistItem item )
	{
		var data = Load( entry );
		if ( item.Id == Guid.Empty ) item.Id = Guid.NewGuid();
		data.Items.Add( item );
		Save( entry, data );
	}

	public static Guid AddMarker( Storage.Entry entry, SpawnlistItemKind kind, string title = null )
	{
		if ( kind == SpawnlistItemKind.Item ) throw new ArgumentOutOfRangeException( nameof( kind ) );

		var data = Load( entry );
		var item = new SpawnlistItem { Id = Guid.NewGuid(), Kind = kind, Title = title };
		data.Items.Add( item );
		Save( entry, data );
		return item.Id;
	}

	public static void MoveItem( Storage.Entry entry, Guid id, int destinationIndex )
	{
		var data = Load( entry );
		var sourceIndex = data.Items.FindIndex( item => item.Id == id );
		if ( sourceIndex < 0 ) return;

		var item = data.Items[sourceIndex];
		data.Items.RemoveAt( sourceIndex );
		if ( sourceIndex < destinationIndex ) destinationIndex--;
		destinationIndex = destinationIndex.Clamp( 0, data.Items.Count );
		data.Items.Insert( destinationIndex, item );
		Save( entry, data );
	}

	public static void RenameItem( Storage.Entry entry, Guid id, string title )
	{
		var data = Load( entry );
		var item = data.Items.FirstOrDefault( item => item.Id == id );
		if ( item is null ) return;

		item.Title = title?.Trim() ?? "";
		Save( entry, data );
	}

	public static void RemoveItem( Storage.Entry entry, Guid id )
	{
		var data = Load( entry );
		if ( data.Items.RemoveAll( item => item.Id == id ) > 0 )
			Save( entry, data );
	}

	public static void RemoveItem( Storage.Entry entry, int index )
	{
		var data = Load( entry );
		if ( index >= 0 && index < data.Items.Count )
		{
			data.Items.RemoveAt( index );
			Save( entry, data );
		}
	}

	/// <summary>
	/// Adds item actions and spawnlist destinations to a context menu.
	/// </summary>
	public static void PopulateContextMenu( Sandbox.UI.Menu menu, Panel sourcePanel, SpawnlistItem item, Storage.Entry skipEntry = null, bool allowCreateSpawnlist = true )
	{
		var entries = GetAll()
			.Where( e => skipEntry is null || e.Id != skipEntry.Id )
			.ToList();

		var (type, path, source) = SpawnlistItem.ParseIdent( item.Ident );

		if ( type != "dupe" )
		{
			menu.AddOption( "#spawnmenu.spawn_with_toolgun", "my_location", async () =>
			{
				// Work out what we can locally first, the host might not have this content (e.g. mounts)
				var metadata = await ISpawner.ResolveMetadata( type, path );
				Toolgun.SetSpawner( type, path, metadata );
			} );
		}

		menu.AddOption( "#spawnmenu.copy", "content_copy", () => Clipboard.SetText( item.Ident ) );

		if ( entries.Count > 0 )
		{
			menu.AddSeparator();
			var sub = menu.AddMenu( "#spawnmenu.spawnlist.add_to_submenu", "playlist_add" );
			foreach ( var entry in entries )
			{
				var data = Load( entry );
				var capturedEntry = entry;
				var option = sub.AddOption( null, "collections_bookmark", () => AddItem( capturedEntry, item ) );

				// List names are authored content, even when they begin with a phrase prefix.
				foreach ( var label in option.Children.OfType<Label>() )
				{
					label.Tokenize = false;
				}

				option.Text = data.Name;
			}

		}

		if ( allowCreateSpawnlist )
		{
			menu.AddSeparator();
			menu.AddOption( "#spawnmenu.spawnlist.create_new_option", "add", () =>
			{
				var name = (type == "dupe" ? item.Title : LocalizedText.Resolve( item.Title ))
					?? Game.Language.GetPhrase( "spawnmenu.spawnlist.new_button" );

				Create( name, out var entry, item );
				sourcePanel.Ancestors.OfType<SpawnMenu>().FirstOrDefault()?.OpenSpawnlist( entry );
			} );
		}

		var spawner = ISpawner.Create( type, path, source );
		var fullIdent = spawner?.FullIdent;

		if ( !string.IsNullOrEmpty( fullIdent ) )
		{
			menu.AddSeparator();

			menu.AddOption( "#ui.open_workshop", "public", () =>
			{
				Game.Overlay.ShowPackageModal( fullIdent );
			} );
		}
	}
}
