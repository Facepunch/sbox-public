using System.Runtime.InteropServices;

/// <summary>
/// An affine transformation stored as three column-vector rows with translation in W.
/// Matches native <c>matrix3x4_t</c> layout.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct Matrix3x4
{
	/// <summary>
	/// First row, including the X translation in W.
	/// </summary>
	public Vector4 Row0;

	/// <summary>
	/// Second row, including the Y translation in W.
	/// </summary>
	public Vector4 Row1;

	/// <summary>
	/// Third row, including the Z translation in W.
	/// </summary>
	public Vector4 Row2;

	/// <summary>
	/// The identity transformation.
	/// </summary>
	public static readonly Matrix3x4 Identity = new() { Row0 = new( 1, 0, 0, 0 ), Row1 = new( 0, 1, 0, 0 ), Row2 = new( 0, 0, 1, 0 ) };

	/// <summary>
	/// Convert an affine row-vector <see cref="Matrix"/> into column-vector layout.
	/// The input's fourth column is omitted; perspective projection is not supported.
	/// </summary>
	public static Matrix3x4 From( in Matrix matrix ) => new()
	{
		Row0 = new( matrix.M11, matrix.M21, matrix.M31, matrix.M41 ),
		Row1 = new( matrix.M12, matrix.M22, matrix.M32, matrix.M42 ),
		Row2 = new( matrix.M13, matrix.M23, matrix.M33, matrix.M43 ),
	};

	/// <summary>
	/// Create an affine matrix from a position, rotation and scale.
	/// </summary>
	public static Matrix3x4 From( in Transform transform ) => From( Matrix.FromTransform( transform ) );

	/// <summary>
	/// <paramref name="a"/> after <paramref name="b"/> - native's <c>ConcatTransforms( a, b, out )</c>.
	/// </summary>
	public static Matrix3x4 Concat( in Matrix3x4 a, in Matrix3x4 b )
	{
		return new()
		{
			Row0 = Row( a.Row0, b ),
			Row1 = Row( a.Row1, b ),
			Row2 = Row( a.Row2, b ),
		};
	}

	static Vector4 Row( Vector4 row, in Matrix3x4 matrix ) => new(
		row.x * matrix.Row0.x + row.y * matrix.Row1.x + row.z * matrix.Row2.x,
		row.x * matrix.Row0.y + row.y * matrix.Row1.y + row.z * matrix.Row2.y,
		row.x * matrix.Row0.z + row.y * matrix.Row1.z + row.z * matrix.Row2.z,
		row.x * matrix.Row0.w + row.y * matrix.Row1.w + row.z * matrix.Row2.w + row.w );
}
