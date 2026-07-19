using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Project wide draw order settings for sprites. Sort layers are an ordered list - a sprite in a
/// later layer always draws in front of a sprite in an earlier one, whatever their positions.
/// </summary>
[Expose]
public class SortingSettings : ConfigData
{
	/// <summary>
	/// Name given to the layer that every sprite starts in.
	/// </summary>
	public const string DefaultLayerName = "Default";

	/// <summary>
	/// A named position in the draw order. Sprites reference layers by <see cref="Id"/>, so a layer
	/// can be freely renamed or moved without silently re-sorting anything that points at it.
	/// </summary>
	public class SortLayer
	{
		/// <summary>
		/// Display name, expected to be unique within the list.
		/// </summary>
		[KeyProperty]
		public string Name { get; set; } = "New Layer";

		/// <summary>
		/// Stable identity, preserved across reordering and renaming.
		/// </summary>
		[Hide]
		public Guid Id { get; set; } = Guid.NewGuid();
	}

	/// <summary>
	/// Every sort layer, back to front. The first entry draws first and so appears behind the rest.
	/// </summary>
	public List<SortLayer> Layers { get; set; }

	private Dictionary<Guid, int> _indexById;

	public SortingSettings()
	{
		OnValidate();
	}

	/// <summary>
	/// The layer that sprites fall back to when they have no layer assigned, or when the layer they
	/// reference no longer exists. Always the first layer in the list.
	/// </summary>
	[JsonIgnore, Hide]
	public SortLayer DefaultLayer => Layers[0];

	/// <summary>
	/// Finds the position of a layer in the draw order. Returns 0 - the default layer - if the id is
	/// empty or refers to a layer that has since been deleted.
	/// </summary>
	public int GetLayerIndex( Guid id )
	{
		if ( id == Guid.Empty ) return 0;

		return _indexById.TryGetValue( id, out var index ) ? index : 0;
	}

	/// <summary>
	/// Finds a layer by its id, or null if no such layer exists.
	/// </summary>
	public SortLayer GetLayer( Guid id )
	{
		if ( id == Guid.Empty ) return null;

		return _indexById.TryGetValue( id, out var index ) ? Layers[index] : null;
	}

	/// <summary>
	/// Finds a layer by name, case insensitively, or null if no such layer exists.
	/// </summary>
	public SortLayer GetLayer( string name )
	{
		foreach ( var layer in Layers )
		{
			if ( string.Equals( layer.Name, name, StringComparison.OrdinalIgnoreCase ) )
				return layer;
		}

		return null;
	}

	/// <summary>
	/// Adds a layer at the front of the draw order, so it draws on top of everything already there.
	/// </summary>
	public SortLayer AddLayer( string name )
	{
		var layer = new SortLayer { Name = name };

		Layers.Add( layer );
		Refresh();

		return layer;
	}

	/// <summary>
	/// Removes a layer. Sprites still pointing at it resolve back to <see cref="DefaultLayer"/> on
	/// their own, so there is nothing to fix up afterwards.
	///
	/// Refuses to remove the last layer, since everything falls back to it.
	/// </summary>
	public bool RemoveLayer( SortLayer layer )
	{
		if ( Layers.Count <= 1 ) return false;
		if ( !Layers.Remove( layer ) ) return false;

		Refresh();
		return true;
	}

	/// <summary>
	/// Moves a layer to a new position in the draw order.
	/// </summary>
	public void MoveLayer( int fromIndex, int toIndex )
	{
		if ( fromIndex < 0 || fromIndex >= Layers.Count ) return;
		if ( toIndex < 0 || toIndex >= Layers.Count ) return;
		if ( fromIndex == toIndex ) return;

		var layer = Layers[fromIndex];

		Layers.RemoveAt( fromIndex );
		Layers.Insert( toIndex, layer );

		Refresh();
	}

	/// <summary>
	/// Rebuilds the id lookup after the layer list has been changed.
	///
	/// Every lookup from a layer id to its position in the draw order goes through that map, so
	/// leaving it stale does not throw - it quietly resolves layers to the wrong position, or to
	/// the default. Prefer <see cref="AddLayer"/>, <see cref="RemoveLayer"/> and
	/// <see cref="MoveLayer"/>, which cannot forget to call this.
	/// </summary>
	public void Refresh() => OnValidate();

	protected override void OnValidate()
	{
		Layers ??= [new SortLayer { Name = DefaultLayerName }];

		// A project with no layers at all has nothing to fall back to, so keep one alive.
		if ( Layers.Count == 0 )
		{
			Layers.Add( new SortLayer { Name = DefaultLayerName } );
		}

		_indexById = new Dictionary<Guid, int>( Layers.Count );

		for ( int i = 0; i < Layers.Count; i++ )
		{
			var layer = Layers[i];

			// Layers authored by hand, or duplicated in the editor, can arrive without an id.
			if ( layer.Id == Guid.Empty )
			{
				layer.Id = Guid.NewGuid();
			}

			// First one wins, so a duplicated id can never make a layer unreachable.
			_indexById.TryAdd( layer.Id, i );
		}
	}

	public override int GetHashCode()
	{
		HashCode hc = default;

		foreach ( var layer in Layers )
		{
			hc.Add( layer.Id );
			hc.Add( layer.Name );
		}

		return hc.ToHashCode();
	}
}
