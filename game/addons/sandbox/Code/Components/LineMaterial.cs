/// <summary>
/// Puts a workshop material on the <see cref="LineRenderer"/>s of this object. It has to run on every client:
/// a runtime material can't be networked, a client may not have the package mounted, and workshop materials
/// are made for models, which a line can't draw - so each client fetches the package and copies its textures
/// into a line material of its own.
/// </summary>
[Title( "Line Material" ), Icon( "texture" ), Hide]
public sealed class LineMaterial : Component
{
	/// <summary>
	/// A line.shader material with only default textures besides its color, copied as the base for conversions.
	/// </summary>
	const string LineTemplate = "entities/trails/line_thickbeam.vmat";

	/// <summary>
	/// The texture slots line.shader has, copied across from the workshop material when it has them.
	/// </summary>
	static readonly string[] TextureSlots = ["g_tColor", "g_tNormal", "g_tRoughness", "g_tMetalness", "g_tAmbientOcclusion"];

	// converted once per package, every rope using it shares the result
	static readonly Dictionary<string, Task<Material>> _converted = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The workshop package ident of the material.
	/// </summary>
	[Property]
	public string Ident { get; set; }

	protected override void OnStart() => _ = Apply();

	async Task Apply()
	{
		if ( string.IsNullOrWhiteSpace( Ident ) ) return;

		Material material = null;
		try
		{
			material = await GetLineMaterial( Ident );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't load rope material '{Ident}': {e}" );
		}

		if ( material is null || !this.IsValid() ) return;

		foreach ( var line in GetComponents<LineRenderer>( true ) )
			line.Texturing = line.Texturing with { Material = material };
	}

	/// <summary>
	/// The workshop material as a line material, fetched and converted once and shared after that.
	/// </summary>
	public static Task<Material> LoadAsync( string ident ) => GetLineMaterial( ident );

	static Task<Material> GetLineMaterial( string ident )
	{
		if ( !_converted.TryGetValue( ident, out var task ) || task.IsFaulted )
			_converted[ident] = task = Convert( ident );

		return task;
	}

	static async Task<Material> Convert( string ident )
	{
		var source = await Cloud.Load<Material>( ident );
		if ( source is null ) return null;

		// already made for lines
		if ( source.ShaderName?.Contains( "line", StringComparison.OrdinalIgnoreCase ) == true ) return source;

		// some materials keep their color somewhere other than g_tColor, the first texture is the best guess then
		var color = source.GetTexture( "g_tColor" ) ?? source.FirstTexture;
		if ( color is null )
		{
			Log.Warning( $"Rope material '{ident}' ({source.ShaderName}) has no texture a line can use" );
			return null;
		}

		// a copy of a line material we ship, so the shader is right and any slot the source lacks keeps a neutral default
		var line = Material.Load( LineTemplate )?.CreateCopy();
		if ( line is null )
		{
			Log.Warning( $"Couldn't load the line material template '{LineTemplate}'" );
			return null;
		}

		line.Set( "g_tColor", color );
		foreach ( var slot in TextureSlots )
		{
			if ( slot == "g_tColor" ) continue;
			if ( source.GetTexture( slot ) is { } texture )
				line.Set( slot, texture );
		}

		return line;
	}

	public static bool IsPackageIdent( string path ) => !string.IsNullOrEmpty( path ) && !path.Contains( '/' ) && Package.TryParseIdentParts( path, out _ );
}
