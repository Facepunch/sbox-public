using System.Runtime.InteropServices;

namespace NativeEngine;

internal enum AnimParamType : byte
{
	Unknown = 0,
	Bool,
	Enum,
	Int,
	Float,
	Vector,
	Rotation,
};

[StructLayout( LayoutKind.Explicit, Pack = 1, Size = 17 )]
internal readonly struct AnimVariant
{
	[FieldOffset( 0 )]
	private readonly Vector4 _rawValue;

	[FieldOffset( 16 )]
	public readonly AnimParamType Type;

	private AnimVariant( Vector4 value, AnimParamType type )
	{
		_rawValue = value;
		Type = type;
	}

	private object GetValue() => Type switch
	{
		AnimParamType.Bool => (bool)this,
		AnimParamType.Enum => (byte)this,
		AnimParamType.Int => (int)this,
		AnimParamType.Float => (float)this,
		AnimParamType.Vector => (Vector3)this,
		AnimParamType.Rotation => (Rotation)this,
		_ => null
	};

	public T GetValue<T>() => (T)GetValue();

	public bool TryConvertTo( AnimParamType type, out AnimVariant result )
	{
		if ( Type == type )
		{
			result = this;
			return true;
		}

		// Only try to convert between scalar types

		result = (Type, type) switch
		{
			(AnimParamType.Bool, AnimParamType.Int ) => (bool)this ? 1 : 0,
			(AnimParamType.Bool, AnimParamType.Enum ) => (byte)((bool)this ? 1 : 0),
			(AnimParamType.Bool, AnimParamType.Float ) => (bool)this ? 1f : 0f,

			(AnimParamType.Enum, AnimParamType.Bool ) => (byte)this != 0,
			(AnimParamType.Enum, AnimParamType.Int ) => (int)(byte)this,
			(AnimParamType.Enum, AnimParamType.Float ) => (float)(byte)this,

			(AnimParamType.Int, AnimParamType.Bool ) => (int)this != 0,
			(AnimParamType.Int, AnimParamType.Enum ) => (byte)(int)this,
			(AnimParamType.Int, AnimParamType.Float ) => (float)(int)this,

			(AnimParamType.Float, AnimParamType.Bool ) => !((float)this).AlmostEqual( 0f ),
			(AnimParamType.Float, AnimParamType.Enum ) => (byte)(float)this,
			(AnimParamType.Float, AnimParamType.Int ) => (int)(float)this,

			_ => default
		};

		if ( result.Type == AnimParamType.Unknown )
		{
			return false;
		}

		Assert.AreEqual( type, result.Type );
		return true;
	}

	public override string ToString() => $"{Type} {{ {GetValue()} }}";

	// Select integer bits before reinterpreting them: when denormals are treated as zero,
	// the JIT can fold a float conditional between Int32BitsToSingle( 1 ) and 0f to zero.
	public static implicit operator AnimVariant( bool value ) => new( new Vector4( BitConverter.Int32BitsToSingle( value ? 1 : 0 ), 0f, 0f, 0f ), AnimParamType.Bool );
	public static implicit operator AnimVariant( byte value ) => new( new Vector4( BitConverter.Int32BitsToSingle( value ), 0f, 0f, 0f ), AnimParamType.Enum );
	public static implicit operator AnimVariant( int value ) => new( new Vector4( BitConverter.Int32BitsToSingle( value ), 0f, 0f, 0f ), AnimParamType.Int );
	public static implicit operator AnimVariant( float value ) => new( new Vector4( value, 0f, 0f, 0f ), AnimParamType.Float );
	public static implicit operator AnimVariant( Vector3 value ) => new( new Vector4( value, 0f ), AnimParamType.Vector );
	public static implicit operator AnimVariant( Rotation value ) => new( new Vector4( value.x, value.y, value.z, value.w ), AnimParamType.Rotation );

	public static explicit operator bool( AnimVariant value ) => (byte)BitConverter.SingleToInt32Bits( value._rawValue.x ) != 0;
	public static explicit operator byte( AnimVariant value ) => (byte)BitConverter.SingleToInt32Bits( value._rawValue.x );
	public static explicit operator int( AnimVariant value ) => BitConverter.SingleToInt32Bits( value._rawValue.x );
	public static explicit operator float( AnimVariant value ) => value._rawValue.x;
	public static explicit operator Vector3( AnimVariant value ) => new( value._rawValue.x, value._rawValue.y, value._rawValue.z );
	public static explicit operator Rotation( AnimVariant value ) => new( value._rawValue.x, value._rawValue.y, value._rawValue.z, value._rawValue.w );
}
