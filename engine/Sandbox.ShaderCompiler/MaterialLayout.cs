namespace Sandbox.ShaderCompiler;

/// <summary>
/// A material's record: the fields of its <c>[Material]</c> struct in declaration order, each four-byte aligned with no
/// padding (the scalar layout <c>ByteAddressBuffer.Load&lt;T&gt;</c> reads), textures as a <c>uint</c> bindless slot.
/// This is the layout gpudriven's material table holds per material.
/// </summary>
public sealed class MaterialLayout
{
	/// <summary>The struct's name.</summary>
	public string Type { get; init; }

	/// <summary>Bytes of one record.</summary>
	public int Size { get; init; }

	/// <summary>Its fields.</summary>
	public IReadOnlyList<MaterialField> Fields { get; init; }

	/// <summary>A field by name, or null.</summary>
	public MaterialField Field( string name ) => Fields.FirstOrDefault( f => f.Name == name );

	/// <summary>The render state every material starts from: the struct's attributes (<c>[Blend( "Alpha" )]</c>).</summary>
	public SurfaceRenderDefaults RenderState { get; init; } = SurfaceRenderDefaults.None;

	/// <summary>The render state's defaults and <c>[RenderState]</c> fields, for resolving it per material.</summary>
	public SurfaceStateLayout States() => new( RenderState, Fields.SelectMany( f => (f.RenderStates ?? []).Select( r => new SurfaceStateField( r, f.Offset, f.Kind == MaterialFieldKind.Float ) ) ).ToArray() );

	/// <summary>
	/// Specialization constant id of the flag that says a pipeline is specialised for one material's features
	/// (<see cref="Features"/>); each feature's value is constant <see cref="FirstFeatureId"/> plus its index.
	/// </summary>
	public const int SpecializedId = 15;

	/// <summary>Specialization constant id of the first feature's value.</summary>
	public const int FirstFeatureId = 16;

	/// <summary>Features a pipeline can be specialised for, at most: ids up to the <c>SpecializedConstant()</c> globals'.</summary>
	public const int MaxFeatures = 48;

	/// <summary>
	/// The fields a pipeline can be specialised for (<see cref="MaterialField.IsFeature"/>), in order, at most
	/// <see cref="MaxFeatures"/>: pixel stages read them as specialization constants when the pipeline says so.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public IReadOnlyList<MaterialField> Features => features ??= Fields.Where( f => f.IsFeature ).Take( MaxFeatures ).ToArray();

	MaterialField[] features;

	/// <summary>Lay out <paramref name="fields"/> (offsets ignored) in order, with the struct's render state.</summary>
	public static MaterialLayout Create( string type, IEnumerable<MaterialField> fields, SurfaceRenderDefaults renderState = null )
	{
		var laidOut = new List<MaterialField>();
		var offset = 0;
		foreach ( var field in fields )
		{
			laidOut.Add( field with { Offset = offset } );
			offset += field.Size;
		}

		return new MaterialLayout { Type = type, Size = offset, Fields = laidOut, RenderState = renderState ?? SurfaceRenderDefaults.None };
	}

	/// <summary>A record with every field at its default (textures 0, which the engine's shaders read as none).</summary>
	public byte[] DefaultRecord()
	{
		var record = new byte[Size];
		foreach ( var field in Fields )
		{
			if ( field.Kind == MaterialFieldKind.Texture2D || field.Default is null ) continue;
			for ( int i = 0; i < field.Components && i < field.Default.Length; i++ ) field.Write( record, i, field.Default[i] );
		}

		return record;
	}
}

/// <summary>What a material field holds.</summary>
public enum MaterialFieldKind
{
	/// <summary>Floats.</summary>
	Float,
	/// <summary>Signed integers.</summary>
	Int,
	/// <summary>Unsigned integers.</summary>
	UInt,
	/// <summary>A bool, 32 bits.</summary>
	Bool,
	/// <summary>A 2D texture (written <c>Texture2D</c>): its bindless slot.</summary>
	Texture2D,
}

/// <summary>
/// A material field: its name, kind and component count, byte offset in the record, and what the author's attributes
/// say (<c>[Group]</c>, <c>[Range]</c>, <c>[Default]</c>, <c>[Srgb]</c>).
/// </summary>
public sealed record MaterialField( string Name, MaterialFieldKind Kind, int Components, int Offset, float[] Default, string Group, float? Min, float? Max, bool Srgb )
{
	/// <summary>The texture <c>Opacity</c> comes from (<c>[Opacity]</c>), whose size alpha to coverage compensates mips by.</summary>
	public bool Opacity { get; init; }

	/// <summary>
	/// The <c>.shader</c> value a material converted from a <c>.vmat</c> takes this field from (<c>[Source( "F_MULTIBLEND" )]</c>:
	/// a feature, variable or <c>ShaderVersion</c>); null to look it up by name.
	/// </summary>
	public string Source { get; init; }

	/// <summary>
	/// The render states a material changes with this field (<c>[RenderState( "Blend: Alpha" )]</c>); null for none. Applied
	/// in field order over the struct's defaults.
	/// </summary>
	public IReadOnlyList<SurfaceStateRule> RenderStates { get; init; }

	/// <summary>
	/// The <c>.shader</c>s a <c>[Source( "Shader: complex simple generic" )]</c> field tells apart: a converted material
	/// takes the index of its own shader in the list. Null for other fields.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public IReadOnlyList<string> ShaderNames => Source is { } s && s.StartsWith( "Shader:", StringComparison.Ordinal )
		? s["Shader:".Length..].Split( ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
		: null;

	/// <summary>
	/// A feature: a bool or integer whose <c>[Source]</c> is a <c>.shader</c> feature (<c>F_X</c>) or the shader list, and
	/// whose range lets it vary. Fixed per material in practice, so a pipeline can be specialised for its value.
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public bool IsFeature => Components == 1 && Kind is MaterialFieldKind.Bool or MaterialFieldKind.Int or MaterialFieldKind.UInt
		&& Source is { } source && (source.StartsWith( "F_", StringComparison.Ordinal ) || source.StartsWith( "Shader:", StringComparison.Ordinal ))
		&& !(Min is { } min && Max is { } max && min == max);

	/// <summary>Bytes it takes.</summary>
	public int Size => Kind == MaterialFieldKind.Texture2D ? 4 : 4 * Components;

	/// <summary>Write one component of a value into <paramref name="record"/>.</summary>
	public void Write( Span<byte> record, int component, float value )
	{
		var at = record[(Offset + component * 4)..];
		switch ( Kind )
		{
			case MaterialFieldKind.Float:
				System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( at, value );
				break;
			case MaterialFieldKind.Int:
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( at, (int)MathF.Round( value ) );
				break;
			case MaterialFieldKind.Bool:
				System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( at, value != 0 ? 1u : 0u );
				break;
			default:
				System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( at, (uint)MathF.Max( 0, MathF.Round( value ) ) );
				break;
		}
	}
}
