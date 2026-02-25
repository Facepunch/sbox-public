using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Holds a collection of clothing items. Won't let you add items that aren't compatible.
/// </summary>
public partial class ClothingContainer
{
	[Expose]
	public class ClothingEntry
	{
		public ClothingEntry()
		{
		}

		public ClothingEntry( Clothing clothing )
		{
			Clothing = clothing;
			ItemDefinitionId = clothing?.SteamItemDefinitionId ?? 0;
		}

		/// <summary>
		/// A direct reference to the clothing item
		/// </summary>
		[KeyProperty]
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public Clothing Clothing { get; set; }

		/// <summary>
		/// If this is a Steam Inventory Item then this is the item definition id. This usually means
		/// we'll look up the clothing item from the workshop.
		/// </summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		[JsonPropertyName( "item" )]
		public int ItemDefinitionId { get; set; }

		/// <summary>
		/// Used to select a tint for the item. The gradients are defined in the item.
		/// </summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public float? Tint { get; set; }

		/// <summary>
		/// If this item is manually placed, this is the bone we're attached to
		/// </summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		[JsonPropertyName( "bone" )]
		public string Bone { get; set; }

		/// <summary>
		/// If this item is manually placed, this is the offset relative to the bone
		/// </summary>
		[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		[JsonPropertyName( "tx" )]
		public Transform? Transform { get; set; }
	}

	/// <summary>
	/// A user set name for this setup
	/// </summary>
	public string DisplayName { get; set; } = null;

	/// <summary>
	/// The avatar's height. Default is 0.5f.
	/// </summary>
	public float Height { get; set; } = 0.5f;

	/// <summary>
	/// The avatar's age. Default is 0.0f. We'll pick a skin based on this.
	/// </summary>
	public float Age { get; set; } = 0.5f;

	/// <summary>
	/// For the citizen the skin color is dynamic, based on a gradient. This is 0-1.
	/// </summary>
	public float Tint { get; set; } = 0.5f;

	/// <summary>
	/// If true, this avatar prefers to use a human model when possible
	/// </summary>
	public bool PrefersHuman { get; set; } = false;

	/// <summary>
	/// A list of clothing items the avatar is wearing
	/// </summary>
	public List<ClothingEntry> Clothing = new();

	/// <summary>
	/// Restrict things like Height to their sensible limits
	/// </summary>
	public void Normalize()
	{
		Height = Height.Clamp( 0, 1 );
		Age = Age.Clamp( 0, 1 );
		Tint = Tint.Clamp( 0, 1 );
		DisplayName = DisplayName?.Truncate( 32 );
	}

	/// <summary>
	/// Add a clothing item if we don't already contain it, else remove it
	/// </summary>
	public void Toggle( Clothing clothing )
	{
		if ( Has( clothing ) ) Remove( clothing );
		else Add( clothing );
	}

	/// <summary>
	/// Add clothing item
	/// </summary>
	public ClothingEntry Add( Clothing clothing )
	{
		Clothing.RemoveAll( x => (x.Clothing?.CanBeWornWith( clothing ) ?? true) == false );

		var entry = new ClothingEntry( clothing );
		Add( entry );
		return entry;
	}

	void FixClothing( ClothingEntry entry )
	{
		if ( entry.Clothing is not null && entry.Clothing.IsPlaceholder() )
		{
			entry.ItemDefinitionId = entry.Clothing.SteamItemDefinitionId ?? 0;
		}

		// it's fine, probably
		if ( entry.Clothing != null ) return;

		// nothing we can do, we can't look this up
		if ( entry.ItemDefinitionId == 0 ) return;

		var definition = Services.Inventory.FindDefinition( entry.ItemDefinitionId );

		// no definition? This isn't a proper clothing?
		if ( definition == null )
		{
			Log.Warning( $"Def not found {entry.ItemDefinitionId}" );
			return;
		}

		// maybe it's a locally stored inventory item, and already loaded?
		entry.Clothing = ResourceLibrary.GetAll<Clothing>().FirstOrDefault( x => x.SteamItemDefinitionId == entry.ItemDefinitionId );
		if ( entry.Clothing != null ) return;

		// maybe it's remote, but we've downloaded it
		entry.Clothing = ResourceLibrary.GetAll<Clothing>().FirstOrDefault( x => x.ResourcePath == definition.Asset );
		if ( entry.Clothing != null ) return;

		Log.Trace( $"Couldn't fix clothing item def {entry.ItemDefinitionId}" );
	}

	/// <summary>
	/// Add clothing item
	/// </summary>
	public void Add( ClothingEntry clothing )
	{
		FixClothing( clothing );

		Clothing.RemoveAll( x => (x.Clothing?.CanBeWornWith( clothing.Clothing ) ?? true) == false );
		Clothing.Add( clothing );
	}

	/// <summary>
	/// Add clothing items
	/// </summary>
	public void AddRange( IEnumerable<ClothingEntry> clothing )
	{
		if ( clothing is null ) return;

		foreach ( var item in clothing )
		{
			Add( item );
		}
	}

	/// <summary>
	/// Find a clothing entry matching this clothing item
	/// </summary>
	public ClothingEntry FindEntry( Clothing clothing )
	{
		return Clothing.Where( x => x.Clothing == clothing ).FirstOrDefault();
	}

	/// <summary>
	/// Remove clothing item
	/// </summary>
	private void Remove( Clothing clothing )
	{
		Clothing.RemoveAll( x => x.Clothing == clothing || (x.ItemDefinitionId != 0 && x.ItemDefinitionId == clothing.SteamItemDefinitionId) );
	}

	/// <summary>
	/// Returns true if we have this clothing item
	/// </summary>
	public bool Has( Clothing clothing ) => clothing != null && Clothing.Any( x => x.Clothing == clothing || (x.ItemDefinitionId != 0 && x.ItemDefinitionId == clothing.SteamItemDefinitionId) );

	/// <summary>
	/// Return a list of bodygroups and what their value should be
	/// </summary>
	public IEnumerable<(string name, int value)> GetBodyGroups() => GetBodyGroups( Clothing.Select( x => x.Clothing ) );

	/// <summary>
	/// Return a list of bodygroups and what their value should be
	/// </summary>
	public IEnumerable<(string name, int value)> GetBodyGroups( IEnumerable<Clothing> items )
	{
		var mask = items.Where( x => x.IsValid() ).Select( x => x.HideBody ).DefaultIfEmpty().Aggregate( ( a, b ) => a | b );

		yield return ("head", (mask & Sandbox.Clothing.BodyGroups.Head) != 0 ? 1 : 0);
		yield return ("Chest", (mask & Sandbox.Clothing.BodyGroups.Chest) != 0 ? 1 : 0);
		yield return ("Legs", (mask & Sandbox.Clothing.BodyGroups.Legs) != 0 ? 1 : 0);
		yield return ("Hands", (mask & Sandbox.Clothing.BodyGroups.Hands) != 0 ? 1 : 0);
		yield return ("Feet", (mask & Sandbox.Clothing.BodyGroups.Feet) != 0 ? 1 : 0);
	}

	IEnumerable<Entry> GetSerializedEntities()
	{
		foreach ( var c in Clothing.OrderBy( x => x.Clothing?.ResourcePath ).ThenBy( x => x.ItemDefinitionId ) )
		{
			yield return Entry.From( c );
		}
	}


	/// <summary>
	/// Serialize to Json
	/// </summary>
	public string Serialize()
	{
		var o = new
		{
			Items = GetSerializedEntities(),
			Height,
			DisplayName,
			Age,
			Tint,
			PrefersHuman,
		};

		var options = new JsonSerializerOptions( JsonSerializerOptions.Default ) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
		return System.Text.Json.JsonSerializer.Serialize( o, options );
	}

	/// <summary>
	/// Deserialize from Json
	/// </summary>
	public void Deserialize( string json )
	{
		Clothing.Clear();

		if ( string.IsNullOrWhiteSpace( json ) )
			return;

		try
		{
			var js = Json.ParseToJsonNode( json );

			// old format
			if ( js is JsonArray jsa )
			{
				ParseEntries( jsa );
			}
			else if ( js is JsonObject jso )
			{
				ParseEntries( jso["Items"] as JsonArray );
				Height = (float)(jso["Height"] ?? 1.0f);
				DisplayName = (string)(jso["DisplayName"]);
				Age = (float)(jso["Age"] ?? 0.0f);
				Tint = (float)(jso["Tint"] ?? 0.0f);
				PrefersHuman = (bool)(jso["PrefersHuman"] ?? false);
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception when deserailizing clothing ({e.Message})" );
		}
	}

	void ParseEntries( JsonArray array )
	{
		if ( array is null )
			return;

		var entries = System.Text.Json.JsonSerializer.Deserialize<Entry[]>( array );

		foreach ( var entry in entries )
		{
			var add = new ClothingEntry();

			if ( entry.ItemId != default )
			{
				add.ItemDefinitionId = entry.ItemId;
			}
			else
			{
				// Try new path-based format first, then fall back to legacy int id for old saved avatars
#pragma warning disable CS0618, CS0612 // Type or member is obsolete
				add.Clothing = !string.IsNullOrEmpty( entry.Path )
					? Game.Resources.Get<Clothing>( entry.Path )
					: Game.Resources.Get<Clothing>( entry.LegacyId );
#pragma warning restore CS0618, CS0612 // Type or member is obsolete
				if ( add.Clothing == null ) continue;
			}

			add.Tint = entry.Tint;
			Add( add );
		}
	}

	/// <summary>
	/// Used for serialization
	/// </summary>
	internal class Entry
	{
		/// <summary>
		/// The resource path of this item. This means it's on disk somewhere.
		/// </summary>
		[JsonPropertyName( "p" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		internal string Path { get; set; }

		/// <summary>
		/// Legacy integer resource ID from before path-based serialization.
		/// Kept for backwards compatibility when reading old saved avatars.
		/// </summary>
		[JsonPropertyName( "id" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		[Obsolete]
		internal int LegacyId { get; set; }

		/// <summary>
		/// The Steam Inventory Item Definition Id. This means we should look up the item from the workshop.
		/// </summary>
		[JsonPropertyName( "iid" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
		public int ItemId { get; set; }

		/// <summary>
		/// Tint variable used to evaluate the model tint color gradient
		/// </summary>
		[JsonPropertyName( "t" )]
		public float? Tint { get; set; }

		internal static Entry From( ClothingEntry c )
		{
			var entry = new Entry { Path = c.Clothing?.ResourcePath, Tint = c.Tint };

			// If we have a Steam item id, store that instead of the path
			if ( c.Clothing != null && c.Clothing.SteamItemDefinitionId.HasValue )
			{
				entry.Path = default;
				entry.ItemId = c.Clothing.SteamItemDefinitionId.Value;
			}

			if ( c.Clothing != null && c.ItemDefinitionId != 0 )
			{
				entry.Path = default;
				entry.ItemId = c.ItemDefinitionId;
			}

			return entry;
		}

		// in the future we could allow some
		// configuration (tint etc) of items
		// which is why this is a struct instead
		// of serializing an array of ints
	}

	/// <summary>
	/// Create the container from json definitions
	/// </summary>
	public static ClothingContainer CreateFromJson( string json )
	{
		var cc = new ClothingContainer();
		cc.Deserialize( json );
		return cc;
	}

	/// <summary>
	/// Create the container from the local user's setup
	/// </summary>
	public static ClothingContainer CreateFromLocalUser()
	{
		return CreateFromJson( Avatar.AvatarJson );
	}

	internal async Task Store( bool active, int slot )
	{
		var json = Serialize();

		try
		{
			if ( active )
			{
				Avatar.AvatarJson = json;
				await Backend.Storage.Set( (long)Utility.Steam.SteamId, "facepunch.avatar", "avatar.active", "0", json );
			}

			await Backend.Storage.Set( (long)Utility.Steam.SteamId, "facepunch.avatar", "avatars", $"{slot}", json );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Error saving avatar - {e.Message}" );
		}
	}
}
