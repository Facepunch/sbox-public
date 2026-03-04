using System.Text.Json.Serialization;

namespace Sandbox.Clutter;

/// <summary>
/// A weighted collection of Prefabs and Models for random selection during clutter placement.
/// </summary>
[AssetType( Name = "Clutter Definition", Extension = "clutter", Category = "World" )]
public class ClutterDefinition : GameResource
{
	/// <summary>
	/// Tile size options for streaming mode.
	/// </summary>
	public enum TileSizeOption
	{
		[Title( "256" )] Size256 = 256,
		[Title( "512" )] Size512 = 512,
		[Title( "1024" )] Size1024 = 1024,
		[Title( "2048" )] Size2048 = 2048,
		[Title( "4096" )] Size4096 = 4096
	}

	/// <summary>
	/// List of weighted entries
	/// </summary>
	[Property]
	[Editor( "ClutterEntriesGrid" )]
	public List<ClutterEntry> Entries { get; set; } = [];

	public bool IsEmpty => Entries.Count == 0;

	/// <summary>
	/// Size of each tile in world units for infinite streaming mode.
	/// </summary>
	[Property]
	[Title( "Tile Size" )]
	public TileSizeOption TileSizeEnum { get; set; } = TileSizeOption.Size512;

	/// <summary>
	/// Gets the tile size as a float value.
	/// </summary>
	[Hide, JsonIgnore]
	public float TileSize => (float)TileSizeEnum;

	/// <summary>
	/// Number of tiles to generate around the camera in each direction.
	/// Higher values = more visible range but more memory usage.
	/// </summary>
	[Property, Range( 1, 10 )]
	public int TileRadius { get; set; } = 4;

	[Property]
	public AnyOfType<Scatterer> Scatterer { get; set; } = new SimpleScatterer();

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add( TileSize );
		hash.Add( TileRadius );
		hash.Add( Entries.Count );

		foreach ( var entry in Entries )
		{
			if ( entry != null )
			{
				hash.Add( entry.Weight );
				hash.Add( entry.Model?.GetHashCode() ?? 0 );
				hash.Add( entry.Prefab?.GetHashCode() ?? 0 );
			}
		}

		hash.Add( Scatterer.Value?.GetHashCode() ?? 0 );
		return hash.ToHashCode();
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "forest", width, height );
	}
}
