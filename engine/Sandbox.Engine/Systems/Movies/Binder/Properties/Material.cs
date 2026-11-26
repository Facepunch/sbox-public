namespace Sandbox.MovieMaker.Properties;

#nullable enable

/// <summary>
/// Pseudo-property inside any <see cref="Material"/> property, for the material's shader. Materials made at runtime
/// can't be saved as a reference, so this is recorded instead. Writing a different shader replaces the material with
/// <see cref="Material.FromShader(Shader)"/>.
/// </summary>
internal sealed record MaterialShaderProperty( ITrackProperty<Material?> Parent ) : ITrackProperty<Shader?>
{
	public const string PropertyName = "Shader";

	public string Name => PropertyName;

	public Shader? Value
	{
		get => Parent.Value?.Shader;
		set
		{
			if ( Parent.Value?.Shader == value ) return;

			Parent.Value = Material.FromShader( value );
		}
	}

	bool ITrackProperty.CanWrite => Parent.CanWrite;

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class MaterialShaderPropertyFactory : ITrackPropertyFactory<ITrackProperty<Material?>, Shader?>
{
	public string BaseCategoryName => "Members";

	public IEnumerable<string> GetPropertyNames( ITrackProperty<Material?> parent ) =>
		[MaterialShaderProperty.PropertyName];

	public bool PropertyExists( ITrackProperty<Material?> parent, string name ) =>
		name == MaterialShaderProperty.PropertyName;

	public ITrackProperty<Shader?> CreateProperty( ITrackProperty<Material?> parent, string name ) =>
		new MaterialShaderProperty( parent );
}
