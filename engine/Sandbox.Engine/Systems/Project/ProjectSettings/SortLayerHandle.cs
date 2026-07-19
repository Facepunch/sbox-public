using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// A reference to one of the project's sprite sort layers. Stores the layer's id rather than its
/// name or position, so renaming or reordering layers never changes what a sprite points at.
/// </summary>
public struct SortLayerHandle : IEquatable<SortLayerHandle>
{
	/// <summary>
	/// Id of the referenced layer. Empty means the default layer.
	/// </summary>
	public Guid Id { get; set; }

	/// <summary>
	/// Initializes a handle pointing at the layer with the given id.
	/// </summary>
	public SortLayerHandle( Guid id ) => Id = id;

	/// <summary>
	/// Initializes a handle pointing at a layer.
	/// </summary>
	public SortLayerHandle( SortingSettings.SortLayer layer ) => Id = layer?.Id ?? Guid.Empty;

	/// <summary>
	/// The layer this handle refers to, falling back to the default layer when the referenced
	/// layer has been deleted.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly SortingSettings.SortLayer Layer
	{
		get
		{
			var settings = ProjectSettings.Sorting;
			return settings.GetLayer( Id ) ?? settings.DefaultLayer;
		}
	}

	/// <summary>
	/// Position of this layer in the draw order. Higher draws in front.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly int Index => ProjectSettings.Sorting.GetLayerIndex( Id );

	/// <summary>
	/// Display name of this layer.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly string Name => Layer.Name;

	/// <summary>
	/// Finds a layer by name. Returns a handle to the default layer if there is no match.
	/// </summary>
	public static SortLayerHandle FromName( string name )
	{
		return new SortLayerHandle( ProjectSettings.Sorting.GetLayer( name ) );
	}

	/// <inheritdoc />
	public readonly bool Equals( SortLayerHandle other ) => Id == other.Id;

	/// <inheritdoc />
	public readonly override bool Equals( object obj ) => obj is SortLayerHandle other && Equals( other );

	/// <inheritdoc />
	public readonly override int GetHashCode() => Id.GetHashCode();

	/// <inheritdoc />
	public readonly override string ToString() => Name;

	public static bool operator ==( SortLayerHandle a, SortLayerHandle b ) => a.Equals( b );
	public static bool operator !=( SortLayerHandle a, SortLayerHandle b ) => !a.Equals( b );
}
