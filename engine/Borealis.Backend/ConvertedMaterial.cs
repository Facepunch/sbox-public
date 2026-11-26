using Borealis.Source2.Materials;
using Borealis.Source2.Shaders;
using Sandbox.ShaderCompiler;
using System.IO;
using System.Linq;
using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// A <c>.vmat</c> material of a <c>.shader</c> that a surface stands in for (<see cref="SurfaceShaders.Convert"/>): the
/// surface's record filled from the material's values, field by field. A field takes the value of its <c>[Source]</c>
/// (a feature, a variable or <c>ShaderVersion</c>), else of its name with vfx's prefixes; textures load from the
/// material's texture of that name. Its render state is the surface's, changed by the material's values of the surface's
/// <c>[RenderState]</c> fields (<c>F_TRANSLUCENT</c> blending, <c>F_ALPHA_TEST</c> alpha-testing), as any material's.
/// </summary>
internal sealed class ConvertedMaterial : SurfaceMaterial
{
	static readonly string[] Prefixes = ["g_t", "g_fl", "g_f", "g_v", "g_n", "g_b", ""];

	ConvertedMaterial( string shader, Sandbox.ShaderCompiler.MaterialLayout layout ) : base( shader, layout.Size, MaterialClassWriter.Hash( layout ), layout.States() )
	{
		this.layout = layout;

		// The words of fields its render state or pipelines' features read
		stateWords = new bool[(layout.Size + 3) / 4];
		for ( int f = 0; f < layout.Fields.Count; f++ )
		{
			var field = layout.Fields[f];
			if ( !field.IsFeature && field.RenderStates is not { Count: > 0 } ) continue;
			for ( int at = field.Offset / 4; at < (field.Offset + field.Size + 3) / 4 && at < stateWords.Length; at++ ) stateWords[at] = true;
		}
	}

	readonly Sandbox.ShaderCompiler.MaterialLayout layout;
	readonly bool[] stateWords;

	private protected override bool AffectsState( int offset ) => offset < 0 || offset / 4 >= stateWords.Length || stateWords[offset / 4];

	/// <summary>
	/// Set the fields a shader variable fills (<c>g_vColorTint</c>: a field whose <c>[Source]</c> it is, or named for it with
	/// vfx's prefixes), as code setting it on the material does. False when the surface has no such field.
	/// </summary>
	public bool Set( string variable, ReadOnlySpan<float> value )
	{
		var found = false;
		for ( int f = 0; f < layout.Fields.Count; f++ )
		{
			var field = layout.Fields[f];
			if ( field.Kind == MaterialFieldKind.Texture2D || !Fills( field, variable ) ) continue;
			found = true;
			for ( int i = 0; i < field.Components && i < value.Length; i++ )
			{
				if ( field.Kind == MaterialFieldKind.Float ) SetFloat( field.Offset + i * 4, value[i] );
				else SetInt( field.Offset + i * 4, field.Kind == MaterialFieldKind.Bool ? (value[i] != 0 ? 1 : 0) : (int)MathF.Round( value[i] ) );
			}
		}

		return found;
	}

	/// <summary>Set the texture fields a shader variable (<c>g_tColor</c>) fills, as <see cref="Set(string, ReadOnlySpan{float})"/> does values.</summary>
	public bool Set( string variable, GpuTexture texture )
	{
		var found = false;
		for ( int f = 0; f < layout.Fields.Count; f++ )
		{
			var field = layout.Fields[f];
			if ( field.Kind != MaterialFieldKind.Texture2D || !Fills( field, variable ) ) continue;
			found = true;
			SetTexture( field.Offset, texture, field.Srgb );
		}

		return found;
	}

	/// <summary>The texture of the first texture field a shader variable (<c>g_tColor</c>) fills, or null.</summary>
	public GpuTexture Texture( string variable )
	{
		for ( int f = 0; f < layout.Fields.Count; f++ )
		{
			var field = layout.Fields[f];
			if ( field.Kind == MaterialFieldKind.Texture2D && Fills( field, variable ) ) return GetTexture( field.Offset );
		}

		return null;
	}

	/// <summary>The value of the field a feature (<c>F_ALPHA_TEST</c>) fills, as the material draws with it; null when the surface has none.</summary>
	internal int? FeatureValue( string feature )
	{
		for ( int f = 0; f < layout.Fields.Count; f++ )
		{
			var field = layout.Fields[f];
			if ( field.Kind != MaterialFieldKind.Texture2D && field.Kind != MaterialFieldKind.Float && string.Equals( field.Source, feature, StringComparison.OrdinalIgnoreCase ) ) return GetInt( field.Offset );
		}

		return null;
	}

	/// <summary>Every texture field nothing has set takes <paramref name="texture"/>: a material made in code, as native makes one.</summary>
	public void FillUnsetTextures( GpuTexture texture )
	{
		for ( int f = 0; f < layout.Fields.Count; f++ )
		{
			var field = layout.Fields[f];
			if ( field.Kind == MaterialFieldKind.Texture2D && GetTexture( field.Offset ) is null ) SetTexture( field.Offset, texture, field.Srgb );
		}
	}

	IReadOnlyDictionary<string, int> featuresSet;

	/// <summary>The surface's feature rules for the material's shader (<see cref="RulesFor"/>), applied to its feature values.</summary>
	FeatureRules rules = FeatureRules.None;

	/// <summary>
	/// Feature values (<c>F_*</c>) set in code over the material's own, as <c>Material.SetFeature</c> does: every feature
	/// code set, so one no longer set goes back to the material's value (its file's, else off). The surface's feature rules
	/// apply to the two together, as native applies a shader's to them, so a feature code sets can turn off one of the
	/// file's (and back on once code stops setting it). The surface's fields whose <c>[Source]</c> is the feature take it;
	/// features it has no field for change nothing.
	/// </summary>
	public void SetFeatures( IReadOnlyDictionary<string, int> features )
	{
		var values = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (name, value) in File?.Ints ?? new Dictionary<string, int>() )
		{
			if ( name.StartsWith( "F_", StringComparison.OrdinalIgnoreCase ) ) values[name] = value;
		}

		foreach ( var (name, value) in features ?? new Dictionary<string, int>() ) values[name] = value;
		rules.ApplyInPlace( values );

		Span<float> one = stackalloc float[1];
		foreach ( var (name, _) in featuresSet ?? new Dictionary<string, int>() )
		{
			if ( values.ContainsKey( name ) ) continue;
			one[0] = 0;
			Set( name, one );
		}

		foreach ( var (name, value) in values )
		{
			one[0] = value;
			Set( name, one );
		}

		featuresSet = features is { Count: > 0 } ? new Dictionary<string, int>( features, StringComparer.OrdinalIgnoreCase ) : null;
	}

	Dictionary<string, bool> moduleAttributes;

	/// <summary>
	/// The bool attributes the surface's module declares (<c>BoolAttribute( overlay, true )</c>) for the material's feature
	/// values, as the material compiler reads them (<see cref="MaterialInputs.Attributes"/>), shared by materials with the
	/// same features.
	/// </summary>
	private protected override bool? ModuleAttribute( string name )
	{
		var declared = Volatile.Read( ref moduleAttributes );
		if ( declared is null ) Volatile.Write( ref moduleAttributes, declared = DeclaredAttributes( Shader, File?.Ints ) );
		return declared.TryGetValue( name, out var value ) ? value : null;
	}

	static readonly Dictionary<string, (PassModule Module, Dictionary<string, bool> Attributes)> declaredAttributes = new( StringComparer.OrdinalIgnoreCase );

	static Dictionary<string, bool> DeclaredAttributes( string path, IReadOnlyDictionary<string, int> ints )
	{
		var features = new SortedDictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (name, value) in ints ?? new Dictionary<string, int>() )
		{
			if ( name.StartsWith( "F_", StringComparison.OrdinalIgnoreCase ) ) features[name] = value;
		}

		var module = ModuleOf( path );
		var key = path + "|" + string.Join( ",", features.Select( f => $"{f.Key}={f.Value}" ) );
		lock ( declaredAttributes )
		{
			if ( declaredAttributes.TryGetValue( key, out var known ) && ReferenceEquals( known.Module, module ) ) return known.Attributes;
		}

		var attributes = new Dictionary<string, bool>( StringComparer.OrdinalIgnoreCase );
		if ( module is not null )
		{
			foreach ( var (name, kind, value) in MaterialInputs.Read( module, features ).Attributes )
			{
				if ( kind == "Bool" && value is [var first, ..] ) attributes.TryAdd( name, first != 0 );
			}
		}

		lock ( declaredAttributes ) declaredAttributes[key] = (module, attributes);
		return attributes;
	}

	static bool Fills( MaterialField field, string variable )
	{
		if ( field.Source is { } source ) return string.Equals( source, variable, StringComparison.OrdinalIgnoreCase );
		foreach ( var prefix in Prefixes )
		{
			if ( variable.Length == prefix.Length + field.Name.Length && variable.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) && variable.EndsWith( field.Name, StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		return field.Kind == MaterialFieldKind.Texture2D && Numbered( field, variable );
	}

	/// <summary>
	/// Whether a texture variable is a numbered variant of a texture field's (<c>g_tColor2</c> for <c>Color</c>): the same
	/// texture made another way (vr_shared_standard_ps_code.fxc's, which vertex_color's materials name), as the material
	/// compiler's inputs take it (<see cref="MaterialInputs"/>).
	/// </summary>
	static bool Numbered( MaterialField field, string variable )
	{
		var stem = "g_t" + field.Name;
		if ( variable.Length <= stem.Length || !variable.StartsWith( stem, StringComparison.OrdinalIgnoreCase ) ) return false;
		for ( int i = stem.Length; i < variable.Length; i++ )
		{
			if ( !char.IsAsciiDigit( variable[i] ) ) return false;
		}

		return true;
	}

	/// <summary>The compiled material it was made from: the textures it names by shader variable, read or not.</summary>
	public MaterialFile File { get; private init; }

	/// <summary>
	/// Whether the surface takes this material: every feature it reads (a field whose <c>[Source]</c> is an <c>F_</c>
	/// feature) is in the field's range. A surface declares the features it doesn't do as fields with <c>Range( 0, 0 )</c>,
	/// so materials that set them stay on their <c>.shader</c>. <paramref name="shader"/> is the material's <c>.shader</c>,
	/// when known: a translucency feature it doesn't read doesn't make the material blend.
	/// </summary>
	public static bool Accepts( CompiledShader compiled, MaterialFile file, ShaderFile shader = null )
	{
		var blends = false;
		foreach ( var field in compiled.Material.Fields )
		{
			if ( field.Source is not { } source || !source.StartsWith( "F_", StringComparison.Ordinal ) ) continue;
			var value = file.Value( source ) is { Length: > 0 } v ? v[0] : 0;
			if ( (field.Min is { } min && value < min) || (field.Max is { } max && value > max) ) return false;
			blends |= source is "F_TRANSLUCENT" or "F_ADDITIVE_BLEND";
		}

		// A translucent material converts only to a surface that reads its translucency features
		return blends || !Blended( file, shader );
	}

	/// <summary>
	/// Whether a material blends (<c>F_TRANSLUCENT</c> or <c>F_ADDITIVE_BLEND</c>), as BackendMaterial.IsTranslucent reads it:
	/// a feature counts only where its shader reads it (<see cref="ShaderFile.FeatureSelectsCode"/>), so a value another
	/// shader left in the material (blendable.shader's <c>F_ADDITIVE_BLEND</c>, which nothing in it reads) doesn't.
	/// </summary>
	static bool Blended( MaterialFile file, ShaderFile shader )
	{
		bool Set( string feature ) => file.Value( feature ) is [> 0, ..] && (shader is null || shader.FeatureSelectsCode( shader.FeatureIndex( feature ) ));
		return Set( "F_TRANSLUCENT" ) || Set( "F_ADDITIVE_BLEND" );
	}

	public static ConvertedMaterial Create( CompiledShader compiled, MaterialFile file, string name, Func<string, GpuTexture> loadTexture )
	{
		var rules = RulesFor( compiled, file.Shader );
		var material = new ConvertedMaterial( compiled.Path, compiled.Material ) { DisplayName = name, File = file, rules = rules };

		// The material's features through the surface's rules, as native applies its shader's before drawing it
		var features = rules.IsEmpty ? null : rules.Apply( file.Ints );

		foreach ( var field in compiled.Material.Fields )
		{
			if ( field.Kind == MaterialFieldKind.Texture2D )
			{
				if ( Lookup( field, file.Textures ) is { } texture && loadTexture( texture ) is { } loaded ) material.SetTexture( field.Offset, loaded, field.Srgb );
				continue;
			}

			var value = field.Source == "ShaderVersion" ? new float[] { file.ShaderVersion } : Value( field, file ) ?? field.Default;
			if ( features is not null && field.Source is { } feature && file.Value( feature ) is not null && features.TryGetValue( feature, out var ruled ) ) value = [ruled];
			if ( value is null ) continue;
			for ( int i = 0; i < field.Components && i < value.Length; i++ )
			{
				if ( field.Kind == MaterialFieldKind.Float ) material.SetFloat( field.Offset + i * 4, value[i] );
				else material.SetInt( field.Offset + i * 4, field.Kind == MaterialFieldKind.Bool ? (value[i] != 0 ? 1 : 0) : (int)MathF.Round( value[i] ) );
			}
		}

		return material;
	}

	/// <summary>
	/// The feature rules of <paramref name="compiled"/>'s module (<see cref="FeatureRules"/>, from its <c>MATERIAL_INPUTS</c>
	/// block) for a material of <paramref name="shader"/>: read with the shader's place in the surface's
	/// <c>[Source( "Shader: ..." )]</c> list (standard's complex, simple, generic), the first when it isn't in it. So a surface
	/// standing in for a deleted <c>.shader</c> carries that shader's rules itself. None when the module declares none.
	/// </summary>
	internal static FeatureRules RulesFor( CompiledShader compiled, string shader )
	{
		var index = compiled.Material.Fields.FirstOrDefault( f => f.ShaderNames is not null ) is { ShaderNames: { } names } ? Math.Max( 0, ShaderIndex( names, shader ) ) : 0;
		return ModuleOf( compiled.Path ) is { } module ? FeatureRules.Read( module, index ) : FeatureRules.None;
	}

	static readonly Dictionary<string, (DateTime Written, PassModule Module)> modules = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// A surface's module (its material inputs generated from its struct, <see cref="SurfaceDeclarations"/>), read again when
	/// its file changes; null when it isn't a surface (or there's no file).
	/// </summary>
	static PassModule ModuleOf( string path )
	{
		if ( string.IsNullOrEmpty( path ) || !System.IO.File.Exists( path ) ) return null;
		var written = System.IO.File.GetLastWriteTimeUtc( path );
		lock ( modules )
		{
			if ( modules.TryGetValue( path, out var known ) && known.Written == written ) return known.Module;
		}

		var text = System.IO.File.ReadAllText( path );
		var module = MaterialInputs.SurfaceText( text ) ? PassModule.FromText( Path.GetFullPath( path ), text, [Path.GetDirectoryName( Path.GetFullPath( path ) )] ) : null;
		lock ( modules ) modules[path] = (written, module);
		return module;
	}

	static float[] Value( MaterialField field, MaterialFile file )
	{
		if ( field.ShaderNames is { } names ) return ShaderIndex( names, file.Shader ) is var index and >= 0 ? [index] : null;
		if ( field.Source is { } source ) return file.Value( source );
		foreach ( var prefix in Prefixes )
		{
			if ( file.Value( prefix + field.Name ) is { } value ) return value;
		}

		return null;
	}

	/// <summary>Where a material's <c>.shader</c> (<c>shaders/generic.shader</c>) is in a <c>[Source( "Shader: ..." )]</c> list; -1 when it isn't.</summary>
	static int ShaderIndex( IReadOnlyList<string> names, string shader )
	{
		if ( shader is null ) return -1;
		var name = Path.GetFileNameWithoutExtension( shader.Replace( '\\', '/' ) );
		for ( int i = 0; i < names.Count; i++ )
		{
			if ( string.Equals( names[i], name, StringComparison.OrdinalIgnoreCase ) ) return i;
		}

		return -1;
	}

	static string Lookup( MaterialField field, IReadOnlyDictionary<string, string> textures )
	{
		if ( field.Source is { } source ) return textures.TryGetValue( source, out var named ) ? named : null;
		foreach ( var prefix in Prefixes )
		{
			if ( textures.TryGetValue( prefix + field.Name, out var texture ) ) return texture;
		}

		// A numbered variant of it (g_tColor2), where the material's shader declared that one
		foreach ( var (variable, texture) in textures )
		{
			if ( Numbered( field, variable ) ) return texture;
		}

		return null;
	}
}
