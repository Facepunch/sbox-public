namespace Sandbox.MovieMaker.Properties;

#nullable enable

/// <summary>
/// Toggles a tag on a <see cref="ITagSet"/>.
/// </summary>
file sealed record TagProperty<T>( ITrackProperty<T?> Parent, string Name ) : ITrackProperty<bool>
	where T : ITagSet
{
	public bool IsBound => Parent is { IsBound: true, Value: not null };

	public bool Value
	{
		get => Parent.Value?.HasLocal( Name ) ?? false;
		set => Parent.Value?.Set( Name, value );
	}

	ITrackTarget ITrackProperty.Parent => Parent;
}

file abstract class TagPropertyFactory<T> : ITrackPropertyFactory<ITrackProperty<T?>, bool>
	where T : ITagSet
{
	/// <summary>
	/// Any property inside a <see cref="ITagSet"/> is a tag.
	/// </summary>
	public bool PropertyExists( ITrackProperty<T?> parent, string name ) => true;

	public ITrackProperty<bool> CreateProperty( ITrackProperty<T?> parent, string name ) =>
		new TagProperty<T>( parent, name );

	public IEnumerable<string> GetPropertyNames( ITrackProperty<T?> parent ) =>
		parent is { IsBound: true, Value: { } tagSet } ? tagSet.LocalTags : [];

	public string BaseCategoryName => "Tags";
}

[Expose]
file sealed class GameTagsPropertyFactory : TagPropertyFactory<GameTags>;

[Expose]
file sealed class TagSetPropertyFactory : TagPropertyFactory<TagSet>;

internal static class TagSetExtensions
{
	extension( ITagSet tagSet )
	{
		public IEnumerable<string> LocalTags =>
			tagSet is GameTags gt ? gt.TryGetAll( false ) : tagSet.TryGetAll();

		public bool HasLocal( string name ) =>
			tagSet is GameTags gt ? gt.Has( name, false ) : tagSet.Has( name );
	}
}
