using System.Text.Json.Serialization;

namespace Sandbox.Services;

/// <summary>
/// Server-computed metadata for a package version, extracted from its uploaded files.
/// This is a shared DTO - it's persisted as JSON on the website and also consumed by the
/// game engine. Polymorphic on package type via the "$type" discriminator; add a
/// <see cref="JsonDerivedTypeAttribute"/> when introducing a new type.
///
/// IgnoreUnrecognizedTypeDiscriminators + a concrete (non-abstract) base means an unknown or
/// missing "$type" falls back to a plain BaseMetaData on read instead of throwing - so an older
/// client can read metadata produced by a newer server.
/// </summary>
[JsonPolymorphic( TypeDiscriminatorPropertyName = "$type", IgnoreUnrecognizedTypeDiscriminators = true )]
[JsonDerivedType( typeof( ModelMetaData ), "model" )]
[JsonDerivedType( typeof( MaterialMetaData ), "material" )]
[JsonDerivedType( typeof( ClothingMetaData ), "clothing" )]
public class BaseMetaData
{
	/// <summary>
	/// Folds this metadata type's derived facet tags into <paramref name="tags"/> in place: each
	/// owned facet is set when the metadata warrants it and cleared otherwise (see
	/// <see cref="SetTag"/>). Stable, lowercase facet keys (e.g. "hascollision", "hasbones"); the
	/// search indexer treats these as internal tags, so a package type can light up a filter just
	/// by defining a tag with the matching name - no metadata-specific code in the search system.
	/// Default is a no-op.
	/// </summary>
	public virtual void GetAutoTags( HashSet<string> tags )
	{
	}

	/// <summary>
	/// Adds <paramref name="tag"/> to the set when <paramref name="present"/>, removes it otherwise.
	/// The remove-on-false branch is what keeps the metadata authoritative: a user can't make a
	/// derived facet (e.g. "hascollision") stick by adding it to their tags manually, and a
	/// re-index can't leave a stale one behind. For overrides to call from <see cref="GetAutoTags"/>.
	/// </summary>
	protected static void SetTag( HashSet<string> tags, string tag, bool present )
	{
		if ( present ) tags.Add( tag );
		else tags.Remove( tag );
	}

	/// <summary>
	/// Mutates a package's category id list in place for the search index: removes the
	/// bucket categories this metadata type manages (so stale ones don't linger) and adds
	/// the ones the current metadata maps to. Raw category ids - the concrete type owns the
	/// id constants. Default is a no-op.
	/// </summary>
	public virtual void UpdateCategories( List<int> categories )
	{
	}
}
