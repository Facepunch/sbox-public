using System.Buffers.Binary;
using System.Numerics;

namespace Basalt.Source2.Models;

/// <summary>
/// The packed normal and tangent encodings compiled meshes use (<c>src/public/mathlib/mathlib.h</c>): a signed
/// octahedral normal and a diamond-encoded tangent angle in 10:10:10:2 (<c>PackNormalTangent_OctDiamond</c>), and the
/// older 8:8:8:8 per-octant encoding (<c>PackNormal_UBYTE4</c>) native repacks to it at load.
/// </summary>
public static class TangentFrame
{
	/// <summary>
	/// Unpack a 10:10:10:2 normal, tangent and bitangent sign (<c>UnpackNormalTangent_OctDiamond</c>).
	/// </summary>
	public static (Vector3 Normal, Vector4 Tangent) UnpackOctDiamond( uint packed )
	{
		var ox = ((packed & 0x3FF) / 1023.0f) * 2 - 1;
		var oy = (((packed >> 10) & 0x3FF) / 1023.0f) * 2 - 1;
		var diamond = ((packed >> 20) & 0x3FF) / 1023.0f;
		var sign = ((packed >> 30) & 3) > 1 ? 1.0f : -1.0f;

		var normal = OctDecode( ox, oy );
		var (b1, b2) = OrthonormalBasis( normal );
		var (vx, vy) = DiamondDecode( diamond );
		var tangent = vx * b1 + vy * b2;
		return (normal, new Vector4( tangent, sign ));
	}

	/// <summary>
	/// Pack a unit normal, unit tangent and bitangent sign as 10:10:10:2 (<c>PackNormalTangent_OctDiamond</c>).
	/// </summary>
	public static uint PackOctDiamond( Vector3 normal, Vector3 tangent, float sign )
	{
		var (ox, oy) = OctEncode( normal );
		var octX = (uint)(Math.Clamp( ox * 0.5f + 0.5f, 0, 1 ) * 1023.0f + 0.5f);
		var octY = (uint)(Math.Clamp( oy * 0.5f + 0.5f, 0, 1 ) * 1023.0f + 0.5f);

		// The tangent is encoded against the normal the shader will reconstruct
		var reconstructed = OctDecode( octX / 1023.0f * 2 - 1, octY / 1023.0f * 2 - 1 );
		var (b1, b2) = OrthonormalBasis( reconstructed );
		var diamond = DiamondEncode( Vector3.Dot( tangent, b1 ), Vector3.Dot( tangent, b2 ) );
		var d = (uint)(Math.Clamp( diamond, 0, 1 ) * 1023.0f + 0.5f);
		var s = sign < 0 ? 0u : 3u;
		return octX | (octY << 10) | (d << 20) | (s << 30);
	}

	/// <summary>
	/// Unpack an 8:8:8:8 normal (bytes 0 and 1) and tangent and sign (bytes 2 and 3) (<c>UnpackNormal_UBYTE4</c>).
	/// </summary>
	public static (Vector3 Normal, Vector4 Tangent) UnpackLegacy( uint packed )
	{
		var (normal, _) = UnpackUByte4( (byte)packed, (byte)(packed >> 8) );
		var (tangent, sign) = UnpackUByte4( (byte)(packed >> 16), (byte)(packed >> 24) );
		return (normal, new Vector4( tangent, sign ));
	}

	static (Vector3 Vector, float Sign) UnpackUByte4( byte cx, byte cy )
	{
		float x = cx - 128.0f;
		float y = cy - 128.0f;

		var zSignBit = x < 0 ? 1.0f : 0.0f;
		var tSignBit = y < 0 ? 1.0f : 0.0f;
		var zSign = -(2 * zSignBit - 1);
		var tSign = -(2 * tSignBit - 1);

		x = x * zSign - zSignBit - 64;
		y = y * tSign - tSignBit - 64;

		var xSignBit = x < 0 ? 1.0f : 0.0f;
		var ySignBit = y < 0 ? 1.0f : 0.0f;
		var xSign = -(2 * xSignBit - 1);
		var ySign = -(2 * ySignBit - 1);

		x = (x * xSign - xSignBit) / 63.0f;
		y = (y * ySign - ySignBit) / 63.0f;
		var z = 1.0f - x - y;

		var scale = 1.0f / MathF.Sqrt( x * x + y * y + z * z );
		return (new Vector3( x * scale * xSign, y * scale * ySign, z * scale * zSign ), tSign);
	}

	/// <summary>
	/// Repack a vertex buffer's 8:8:8:8 normal field as 10:10:10:2 in place, as native does once it knows the draw's
	/// shader decodes it (<c>ResolvePendingTangentFrameCheck</c>, compatible branch). Returns the field's new format.
	/// </summary>
	public static ColorFormat RepackLegacy( byte[] data, int count, int stride, int offset )
	{
		for ( int v = 0; v < count; v++ )
		{
			var at = data.AsSpan( v * stride + offset, 4 );
			var (normal, tangent) = UnpackLegacy( BinaryPrimitives.ReadUInt32LittleEndian( at ) );
			BinaryPrimitives.WriteUInt32LittleEndian( at, PackOctDiamond( normal, new Vector3( tangent.X, tangent.Y, tangent.Z ), tangent.W ) );
		}

		return ColorFormat.R10G10B10A2_UNORM;
	}

	static (float X, float Y) OctEncode( Vector3 n )
	{
		var inverse = 1.0f / (MathF.Abs( n.X ) + MathF.Abs( n.Y ) + MathF.Abs( n.Z ));
		var ox = n.X * inverse;
		var oy = n.Y * inverse;
		if ( n.Z < 0 )
		{
			var wrapX = (1.0f - MathF.Abs( oy )) * (ox >= 0 ? 1.0f : -1.0f);
			var wrapY = (1.0f - MathF.Abs( ox )) * (oy >= 0 ? 1.0f : -1.0f);
			ox = wrapX;
			oy = wrapY;
		}

		return (ox, oy);
	}

	static Vector3 OctDecode( float ox, float oy )
	{
		var nx = ox;
		var ny = oy;
		var nz = 1.0f - MathF.Abs( ox ) - MathF.Abs( oy );
		var t = Math.Clamp( -nz, 0, 1 );
		nx += nx >= 0 ? -t : t;
		ny += ny >= 0 ? -t : t;
		return Vector3.Normalize( new Vector3( nx, ny, nz ) );
	}

	static float DiamondEncode( float px, float py )
	{
		var sum = MathF.Abs( px ) + MathF.Abs( py );
		if ( sum < 1.1920929e-7f ) return 0.5f; // FLT_EPSILON
		var x = px / sum;
		var sign = MathF.Sign( py );
		return -sign * 0.25f * x + 0.5f + sign * 0.25f;
	}

	static (float X, float Y) DiamondDecode( float p )
	{
		float sign = MathF.Sign( p - 0.5f );
		var vx = -sign * 4.0f * p + 1.0f + sign * 2.0f;
		var vy = sign * (1.0f - MathF.Abs( vx ));
		var scale = 1.0f / MathF.Sqrt( vx * vx + vy * vy );
		return (vx * scale, vy * scale);
	}

	/// <summary><c>MakeOrthonormalBasis</c> ("Building an Orthonormal Basis, Revisited").</summary>
	static (Vector3 B1, Vector3 B2) OrthonormalBasis( Vector3 n )
	{
		var sign = n.Z >= 0 ? 1.0f : -1.0f;
		var a = -1.0f / (sign + n.Z);
		var b = n.X * n.Y * a;
		return (new Vector3( 1.0f + sign * n.X * n.X * a, sign * b, -sign * n.X ), new Vector3( b, sign + n.Y * n.Y * a, -n.Y ));
	}
}
