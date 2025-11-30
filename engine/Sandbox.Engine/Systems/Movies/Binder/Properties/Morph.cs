using static Sandbox.SkinnedModelRenderer;

namespace Sandbox.MovieMaker.Properties;

#nullable enable

/// <summary>
/// Reads / writes a morph parameter on a <see cref="SkinnedModelRenderer"/>.
/// </summary>
file sealed record MorphProperty( ITrackProperty<MorphAccessor?> Parent, string Name ) : ITrackProperty<float>
{
	public bool IsBound => Parent.Value?.Names.Contains( Name ) ?? false; // TODO: cache?

	public float Value
	{
		get => Parent.Value?.Get( Name ) ?? default;
		set => Parent.Value?.Set( Name, value );
	}

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class MorphPropertyFactory : ITrackPropertyFactory<ITrackProperty<MorphAccessor?>, float>
{
	/// <summary>
	/// Any property inside a <see cref="MorphAccessor"/> is a morph.
	/// </summary>
	public bool PropertyExists( ITrackProperty<MorphAccessor?> parent, string name ) => true;

	public ITrackProperty<float> CreateProperty( ITrackProperty<MorphAccessor?> parent, string name ) => new MorphProperty( parent, name );
	public IEnumerable<string> GetPropertyNames( ITrackProperty<MorphAccessor?> parent )
	{
		return parent is { IsBound: true, Value: { } accessor }
			? accessor.Names
			: Enumerable.Empty<string>();
	}

	public string GetCategoryName( ITrackProperty<MorphAccessor?> parent, string name ) => "Morphs";
}
