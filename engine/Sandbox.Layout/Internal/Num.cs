using System.Runtime.CompilerServices;

namespace Sandbox.Layout;

/// <summary>
/// Float helpers. Layout uses NaN as "undefined" throughout, so most
/// arithmetic propagates undefined-ness naturally and these helpers cover the cases where it shouldn't.
/// </summary>
internal static class Num
{
	public const float Undefined = float.NaN;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool IsUndefined( float value ) => float.IsNaN( value );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool IsDefined( float value ) => !float.IsNaN( value );

	/// <summary>Max of two values, ignoring undefined operands.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float MaxOrDefined( float a, float b )
	{
		if ( IsDefined( a ) && IsDefined( b ) )
		{
			return MathF.Max( a, b );
		}

		return IsUndefined( a ) ? b : a;
	}

	/// <summary>Min of two values, ignoring undefined operands.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float MinOrDefined( float a, float b )
	{
		if ( IsDefined( a ) && IsDefined( b ) )
		{
			return MathF.Min( a, b );
		}

		return IsUndefined( a ) ? b : a;
	}

	/// <summary>Equal within 0.0001, or both undefined.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool InexactEquals( float a, float b )
	{
		if ( IsDefined( a ) && IsDefined( b ) )
		{
			return MathF.Abs( a - b ) < 0.0001f;
		}

		return IsUndefined( a ) && IsUndefined( b );
	}

	/// <summary>Exactly equal, or both undefined (FloatOptional equality).</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool OptionalEquals( float a, float b ) => a == b
		|| (IsUndefined( a ) && IsUndefined( b ));

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static float UnwrapOrDefault( float value, float fallback ) => IsUndefined( value ) ? fallback : value;

}
