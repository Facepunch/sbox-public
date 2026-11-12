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

	/// <summary>Lay out <paramref name="fields"/> (offsets ignored) in order.</summary>
	public static MaterialLayout Create( string type, IEnumerable<MaterialField> fields )
	{
		var laidOut = new List<MaterialField>();
		var offset = 0;
		foreach ( var field in fields )
		{
			laidOut.Add( field with { Offset = offset } );
			offset += field.Size;
		}

		return new MaterialLayout { Type = type, Size = offset, Fields = laidOut };
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
