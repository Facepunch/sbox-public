using Sandbox;
using System;
using System.IO;
using System.Text;

namespace RasLib;

// Row-vector 4x3: rows 0-2 basis, row 3 translation. point' = T + x*R + y*U + z*F.
public struct Mat43
{
	public Vector3 Right, Up, Forward, Translation;

	public static readonly Mat43 Identity = new() { Right = new( 1, 0, 0 ), Up = new( 0, 1, 0 ), Forward = new( 0, 0, 1 ) };

	public readonly Vector3 Point( Vector3 v ) => Translation + v.x * Right + v.y * Up + v.z * Forward;
	public readonly Vector3 Direction( Vector3 v ) => v.x * Right + v.y * Up + v.z * Forward;

	// basis row lengths in converted axis order (sbox x=Right, y=Forward, z=Up)
	public readonly Vector3 AxisScales => new( Right.Length, Forward.Length, Up.Length );

	public readonly Mat43 Then( Mat43 parent ) => new()
	{
		Right = parent.Direction( Right ),
		Up = parent.Direction( Up ),
		Forward = parent.Direction( Forward ),
		Translation = parent.Point( Translation ),
	};

	// MP2 (Y-up, mirrored) -> s&box (Z-up): conjugate by the (x,y,z)->(-x,-z,y) map.
	// Quaternion built directly from the conjugated basis - LookAt reconstructs the frame
	// from forward+up and can flip handedness for some orientations (mangled face/finger bones).
	public readonly Transform ToSbox( float scale )
	{
		// columns of the converted rotation: images of s&box +X, +Y, +Z
		var c0 = new Vector3( Right.x, Right.z, -Right.y ).Normal;
		var c1 = new Vector3( Forward.x, Forward.z, -Forward.y );
		// rebuild right-handed orthonormal: female Breast-R is a det=-1 mirror clone, which garbles quaternion extraction
		var c2 = Vector3.Cross( c0, c1 ).Normal;
		c1 = Vector3.Cross( c2, c0 );

		float x, y, z, w;
		var trace = c0.x + c1.y + c2.z;
		if ( trace > 0f )
		{
			var s = MathF.Sqrt( trace + 1f ) * 2f;
			w = 0.25f * s;
			x = (c1.z - c2.y) / s;
			y = (c2.x - c0.z) / s;
			z = (c0.y - c1.x) / s;
		}
		else if ( c0.x > c1.y && c0.x > c2.z )
		{
			var s = MathF.Sqrt( 1f + c0.x - c1.y - c2.z ) * 2f;
			w = (c1.z - c2.y) / s;
			x = 0.25f * s;
			y = (c1.x + c0.y) / s;
			z = (c2.x + c0.z) / s;
		}
		else if ( c1.y > c2.z )
		{
			var s = MathF.Sqrt( 1f + c1.y - c0.x - c2.z ) * 2f;
			w = (c2.x - c0.z) / s;
			x = (c1.x + c0.y) / s;
			y = 0.25f * s;
			z = (c2.y + c1.z) / s;
		}
		else
		{
			var s = MathF.Sqrt( 1f + c2.z - c0.x - c1.y ) * 2f;
			w = (c0.y - c1.x) / s;
			x = (c2.x + c0.z) / s;
			y = (c2.y + c1.z) / s;
			z = 0.25f * s;
		}

		var rotation = new Rotation( x, y, z, w );
		var position = new Vector3( -Translation.x, -Translation.z, Translation.y ) * scale;
		return new Transform( position, rotation );
	}
}

// Reads Max Payne's tagged-value stream (1 tag byte + width-compressed payload).
public class MaxReader( byte[] data )
{
	readonly byte[] _data = data;

	public int Position { get; set; }
	public int Length => _data.Length;
	public bool Eof => Position >= _data.Length;
	public byte Peek => _data[Position];

	public byte RawByte() => _data[Position++];

	public uint RawUInt32()
	{
		var v = BitConverter.ToUInt32( _data, Position );
		Position += 4;
		return v;
	}

	public void Skip( int count ) => Position += count;

	public float[] RawFloats( int count )
	{
		var result = new float[count];
		Buffer.BlockCopy( _data, Position, result, 0, count * 4 );
		Position += count * 4;
		return result;
	}

	public ushort[] RawUInt16s( int count )
	{
		var result = new ushort[count];
		Buffer.BlockCopy( _data, Position, result, 0, count * 2 );
		Position += count * 2;
		return result;
	}

	public byte[] RawBytes( int count )
	{
		var result = new byte[count];
		Array.Copy( _data, Position, result, 0, count );
		Position += count;
		return result;
	}

	public int Int()
	{
		var tag = _data[Position++];
		switch ( tag )
		{
			case 0x00: case 0x02: { var v = BitConverter.ToInt32( _data, Position ); Position += 4; return v; }
			case 0x01: case 0x03: { var v = (int)BitConverter.ToUInt32( _data, Position ); Position += 4; return v; }
			case 0x04: { var v = BitConverter.ToInt16( _data, Position ); Position += 2; return v; }
			case 0x05: { var v = BitConverter.ToUInt16( _data, Position ); Position += 2; return v; }
			case 0x06: case 0x07: return (sbyte)_data[Position++];
			case 0x08: return _data[Position++];
			case 0x0F: { int v = _data[Position] | (_data[Position + 1] << 8) | (_data[Position + 2] << 16); Position += 3; return v; }
			case 0x10: { var v = BitConverter.ToUInt16( _data, Position ); Position += 2; return v; }
			case 0x11: return _data[Position++];
			case 0x12: { int v = _data[Position] | (_data[Position + 1] << 8) | (_data[Position + 2] << 16); if ( (v & 0x800000) != 0 ) v |= unchecked((int)0xFF000000); Position += 3; return v; }
			case 0x13: { var v = BitConverter.ToInt16( _data, Position ); Position += 2; return v; }
			case 0x14: return (sbyte)_data[Position++];
			default: throw new Exception( $"unexpected int tag 0x{tag:X2} at {Position - 1}" );
		}
	}

	public float Float()
	{
		var tag = _data[Position++];
		switch ( tag )
		{
			case 0x09: { var v = BitConverter.ToSingle( _data, Position ); Position += 4; return v; }
			case 0x0A: { var v = (float)BitConverter.ToDouble( _data, Position ); Position += 8; return v; }
			case 0x26: { var v = (float)BitConverter.ToHalf( _data, Position ); Position += 2; return v; }
			default: throw new Exception( $"unexpected float tag 0x{tag:X2} at {Position - 1}" );
		}
	}

	public bool Bool()
	{
		Position++; // 0x0E
		return _data[Position++] != 0;
	}

	public string String()
	{
		Position++; // 0x0D
		var count = Int();
		var s = Encoding.Latin1.GetString( _data, Position, count );
		Position += count;
		return s;
	}

	public Vector3 Vector3()
	{
		Position++; // 0x16
		var v = new Vector3( BitConverter.ToSingle( _data, Position ), BitConverter.ToSingle( _data, Position + 4 ), BitConverter.ToSingle( _data, Position + 8 ) );
		Position += 12;
		return v;
	}

	public Mat43 Matrix4x3()
	{
		Position++; // 0x1A
		return new Mat43
		{
			Right = new Vector3( ReadF(), ReadF(), ReadF() ),
			Up = new Vector3( ReadF(), ReadF(), ReadF() ),
			Forward = new Vector3( ReadF(), ReadF(), ReadF() ),
			Translation = new Vector3( ReadF(), ReadF(), ReadF() ),
		};
	}

	float ReadF()
	{
		var v = BitConverter.ToSingle( _data, Position );
		Position += 4;
		return v;
	}

	// Consume a typed value of any kind, return nothing - for fields parsed only to advance.
	public void SkipTyped()
	{
		var tag = _data[Position];
		switch ( tag )
		{
			case 0x0D: String(); return;
			case 0x0E: Bool(); return;
			case 0x09: case 0x0A: case 0x26: Float(); return;
			case 0x15: Position += 1 + 8; return;
			case 0x16: Position += 1 + 12; return;
			case 0x17: Position += 1 + 16; return;
			case 0x18: Position += 1 + 16; return;
			case 0x19: Position += 1 + 36; return;
			case 0x1A: Position += 1 + 48; return;
			case 0x1B: Position += 1 + 64; return;
			default: Int(); return;
		}
	}

	public string StringAt( int offset )
	{
		var end = offset;
		while ( end < _data.Length && _data[end] != 0 ) end++;
		return Encoding.Latin1.GetString( _data, offset, end - offset );
	}
}
