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
	const float TitleHeight = 72f;

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

	public static SpawnlistData Create( string name )
	{
		return Create( name, out _ );
	}

	public static SpawnlistData Create( string name, out Storage.Entry entry )
	{
		var data = new SpawnlistData { Name = name };
		entry = Storage.CreateEntry( "spawnlist" );
		entry.SetMeta( "name", name );
		Save( entry, data );
		SpawnlistCreated?.Invoke();
		return data;
	}

	/// <summary>
	/// Asks for a name in a window over the menu, then makes the spawnlist.
	/// </summary>
	/// <param name="initialItem">An item the new list starts with.</param>
	/// <param name="name">What the name box starts with.</param>
	public static void PromptCreate( Panel source, Action onCreated = null, Action<Storage.Entry> onEntryCreated = null, SpawnlistItem initialItem = null, string name = null )
	{
		UIModal.Prompt( source, "#spawnmenu.spawnlist.create_title", name, newName =>
		{
			Create( newName, out var entry );
			if ( initialItem is not null )
				AddItem( entry, initialItem );

			Notices.AddNotice( "list_alt", Color.Cyan, Game.Language.GetPhrase( "spawnmenu.spawnlist.created_notice", new() { { "name", newName } } ) );

			onCreated?.Invoke();
			onEntryCreated?.Invoke( entry );
		}, "#spawnmenu.spawnlist.name_placeholder", "#spawnmenu.spawnlist.create_button", "add" );
	}

	/// <summary>
	/// "1 item" or "N items", in the player's language.
	/// </summary>
	public static string ItemCountText( int count )
		=> Game.Language.GetPhrase( count == 1 ? "ui.spawnlist.one_item" : "ui.spawnlist.items", new() { { "count", count } } );

	public static void Save( Storage.Entry entry, SpawnlistData data )
	{
		Normalize( data, true );
		var contentItems = data.Items.Where( item => item.IsContent ).ToList();

		entry.Files.WriteJson( "/spawnlist.json", data );
		entry.SetMeta( "name", data.Name );
		entry.SetMeta( "item_count", contentItems.Count );
		entry.SetMeta( "metadata_version", 2 );
		entry.SetMeta( "preview_items", Json.Serialize( contentItems.Take( 6 ).ToList() ) );

		var contentTypes = contentItems
			.Select( item => SpawnlistItem.ParseIdent( item.Ident ).Type ?? "other" )
			.GroupBy( type => type, StringComparer.OrdinalIgnoreCase )
			.ToDictionary( group => group.Key.ToLowerInvariant(), group => group.Count() );
		entry.SetMeta( "content_types", Json.Serialize( contentTypes ) );
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

	private static bool Normalize( SpawnlistData data, bool assignIds )
	{
		var changed = data.Items is null || data.Name is null;
		data.Name ??= Game.Language.GetPhrase( "spawnmenu.spawnlist.untitled" );
		data.Items ??= new();
		if ( !assignIds ) return changed;

		foreach ( var item in data.Items )
		{
			if ( item.Id != Guid.Empty ) continue;
			item.Id = Guid.NewGuid();
			changed = true;
		}

		return changed;
	}

	public static IEnumerable<Storage.Entry> GetAll()
	{
		return Storage.GetAll( "spawnlist" ).OrderByDescending( x => x.Created );
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

	public static void Publish( Storage.Entry entry )
	{
		// Refresh the public metadata manifest before every publish/sync so older
		// local spawnlists gain content previews without needing to be edited first.
		Save( entry, Load( entry ) );

		var options = new Modals.WorkshopPublishOptions
		{
			Title = entry.GetMeta<string>( "name" ) ?? Game.Language.GetPhrase( "spawnmenu.spawnlist.untitled" ),
			Description = entry.GetMeta<string>( "description", "" )
		};
		entry.Publish( options );
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

	public static void PopulateContextMenu( Sandbox.UI.Menu menu, Panel sourcePanel, SpawnlistItem item, Storage.Entry skipEntry = null )
	{
		var entries = GetAll()
			.Where( e => skipEntry is null || e.Id != skipEntry.Id )
			.ToList();

		( string Type, string Path, string Source ) = SpawnlistItem.ParseIdent( item.Ident );

		if ( Type != "dupe" )
		{
			menu.AddOption( "#spawnmenu.spawn_with_toolgun", "my_location", async () =>
			{
				// Work out what we can locally first, the host might not have this content (e.g. mounts)
				var metadata = await ISpawner.ResolveMetadata( Type, Path );
				Toolgun.SetSpawner( Type, Path, metadata );
			} );
		}

		menu.AddOption( "#spawnmenu.copy", "content_copy", () => Clipboard.SetText( item.Ident ) );

		menu.AddSeparator();

		if ( entries.Count > 0 )
		{
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

			menu.AddSeparator();
		}

		var (type, path, source) = SpawnlistItem.ParseIdent( item.Ident );

		menu.AddOption( "#spawnmenu.spawnlist.create_new_option", "add", () =>
		{
			var name = (type == "dupe" ? item.Title : LocalizedText.Resolve( item.Title ))
				?? Game.Language.GetPhrase( "spawnmenu.spawnlist.new_button" );

			// open the list that was made
			Action<Storage.Entry> opened = null;
			var spawnMenu = sourcePanel.Ancestors.OfType<SpawnMenu>().FirstOrDefault();
			if ( spawnMenu is not null )
				opened = spawnMenu.OpenSpawnlist;

			PromptCreate( sourcePanel, onEntryCreated: opened, initialItem: item, name: name );
		} );

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
