using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// In-place composition of local transforms for 2D affine matrices.
/// </summary>
public static class MatrixExtensions
{
	/// <summary>
	/// Prepends a translation along the current axes of a 2D affine matrix.
	/// Mutates the matrix without validating its affine form or checking for overflow.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static void TranslateLocal2D( this ref Matrix matrix, float x, float y )
	{
		matrix.M41 = float.MultiplyAddEstimate( y, matrix.M21, x * matrix.M11 ) + matrix.M41;
		matrix.M42 = float.MultiplyAddEstimate( y, matrix.M22, x * matrix.M12 ) + matrix.M42;
	}

	/// <summary>
	/// Prepends a translation along the current axes of a 2D affine matrix.
	/// Mutates the matrix without validating its affine form or checking for overflow.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static void TranslateLocal2D( this ref Matrix matrix, Vector2 offset ) => matrix.TranslateLocal2D( offset.x, offset.y );

	/// <summary>
	/// Prepends a rotation in degrees to a 2D affine matrix, leaving its origin unchanged.
	/// Positive angles rotate clockwise in screen coordinates. Mutates the matrix without validation.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static void RotateLocal2D( this ref Matrix matrix, float degrees )
	{
		var (sin, cos) = MathF.SinCos( degrees.DegreeToRadian() );
		float m11 = matrix.M11;
		float m12 = matrix.M12;
		matrix.M11 = float.MultiplyAddEstimate( sin, matrix.M21, cos * m11 );
		matrix.M12 = float.MultiplyAddEstimate( sin, matrix.M22, cos * m12 );
		matrix.M21 = float.MultiplyAddEstimate( cos, matrix.M21, -sin * m11 );
		matrix.M22 = float.MultiplyAddEstimate( cos, matrix.M22, -sin * m12 );
	}

	/// <summary>
	/// Prepends a scale to a 2D affine matrix, leaving its origin unchanged.
	/// Mutates the matrix without validating its affine form or checking for overflow.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static void ScaleLocal2D( this ref Matrix matrix, float x, float y )
	{
		matrix.M11 *= x;
		matrix.M12 *= x;
		matrix.M21 *= y;
		matrix.M22 *= y;
	}

	/// <summary>
	/// Prepends a scale to a 2D affine matrix, leaving its origin unchanged. Mutates without validation.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static void ScaleLocal2D( this ref Matrix matrix, Vector2 scale ) => matrix.ScaleLocal2D( scale.x, scale.y );

	/// <summary>
	/// Prepends a uniform scale to a 2D affine matrix, leaving its origin unchanged. Mutates without validation.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static void ScaleLocal2D( this ref Matrix matrix, float scale ) => matrix.ScaleLocal2D( scale, scale );
}
