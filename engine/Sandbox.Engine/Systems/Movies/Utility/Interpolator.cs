
using System;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// Interpolates between two values of the same type.
/// </summary>
/// <typeparam name="T">Value type to interpolate.</typeparam>
public interface IInterpolator<T>
{
	/// <summary>
	/// Interpolate between two values.
	/// </summary>
	/// <param name="a">Value to return when <paramref name="t"/> is <c>0</c>.</param>
	/// <param name="b">Value to return when <paramref name="t"/> is <c>1</c>.</param>
	/// <param name="t">Fraction between <c>0</c> and <c>1</c> (inclusive).</param>
	T Interpolate( T a, T b, float t );
}

/// <summary>
/// Helper for accessing <see cref="IInterpolator{T}"/> implementations,
/// for interpolating between two values of the same type
/// </summary>
public static class Interpolator
{
	/// <summary>
	/// Attempts to find a default interpolator for type <typeparamref name="T"/>,
	/// returning <see langword="null"/> if not found.
	/// </summary>
	/// <typeparam name="T">Value type to interpolate.</typeparam>
	public static IInterpolator<T>? GetDefault<T>()
	{
		// TODO: type library lookup?

		return DefaultInterpolator.Instance as IInterpolator<T>;
	}

	/// <summary>
	/// Attempts to find a default interpolator for the given <paramref name="type"/>,
	/// returning <see langword="null"/> if not found.
	/// </summary>
	public static bool CanInterpolate( Type type )
	{
		// TODO: type library lookup?

		var interpolatorType = typeof( IInterpolator<> ).MakeGenericType( type );

		return typeof( DefaultInterpolator ).IsAssignableTo( interpolatorType );
	}

	public static IInterpolator<T> GetDefaultOrThrow<T>() =>
		GetDefault<T>() ?? throw new Exception( $"Type {typeof( T )} can't be interpolated." );
}

// TODO: special float interpolators for degrees / radians? special vector interpolators for normals?

/// <summary>
/// Interpolator for common types.
/// </summary>
file sealed class DefaultInterpolator :
	IInterpolator<int>, IInterpolator<float>, IInterpolator<double>,
	IInterpolator<Vector2>, IInterpolator<Vector3>, IInterpolator<Vector4>,
	IInterpolator<Rotation>, IInterpolator<Angles>,
	IInterpolator<Color>,
	IInterpolator<Transform>
{
	public static DefaultInterpolator Instance { get; } = new();

	public int Interpolate( int a, int b, float delta ) => a + (int)MathF.Round( (b - a) * delta, MidpointRounding.AwayFromZero );
	public float Interpolate( float a, float b, float delta ) => a + (b - a) * delta;
	public double Interpolate( double a, double b, float delta ) => a + (b - a) * delta;
	public Vector2 Interpolate( Vector2 a, Vector2 b, float delta ) => a + (b - a) * delta;
	public Vector3 Interpolate( Vector3 a, Vector3 b, float delta ) => a + (b - a) * delta;
	public Vector4 Interpolate( Vector4 a, Vector4 b, float delta ) => a + (b - a) * delta;
	public Rotation Interpolate( Rotation a, Rotation b, float delta ) => a.SlerpTo( b, delta );
	public Angles Interpolate( Angles a, Angles b, float delta ) => a.LerpTo( b, delta );
	public Color Interpolate( Color a, Color b, float delta ) => a + (b - a) * delta;
	public Transform Interpolate( Transform a, Transform b, float t ) => a.LerpTo( b, t );
}
