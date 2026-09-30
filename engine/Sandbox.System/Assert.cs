
using System;

namespace Sandbox.Diagnostics;

public static class Assert
{
	/// <summary>
	/// Throws an exception when the given object is null.
	/// </summary>
	/// <typeparam name="T">Any type capable of being null.</typeparam>
	/// <param name="obj">Object to test</param>
	/// <param name="message">Message to show when object is null</param>
	/// <exception cref="System.Exception">Thrown when the given object is null.</exception>
	public static void NotNull<T>( T obj, string message )
	{
		if ( obj == null )
			throw new System.Exception( message );
	}

	/// <summary>
	/// Throws an exception when the given object is null.
	/// </summary>
	/// <typeparam name="T">Any type capable of being null.</typeparam>
	/// <param name="obj">Object to test</param>
	/// <exception cref="System.Exception">Thrown when the given object is null.</exception>
	public static void NotNull<T>( T obj ) => NotNull( obj, "Object should not be null" );

	/// <summary>
	/// Throws an exception when the given object is not null.
	/// </summary>
	/// <typeparam name="T">Any type capable of being null.</typeparam>
	/// <param name="obj">Object to test</param>
	/// <param name="message">Message to show when null</param>
	/// <exception cref="System.Exception">Thrown when the given object is null.</exception>
	public static void IsNull<T>( T obj, string message )
	{
		if ( obj != null )
			throw new System.Exception( message );
	}

	/// <summary>
	/// Throws an exception when the given object is not null.
	/// </summary>
	/// <typeparam name="T">Any type capable of being null.</typeparam>
	/// <param name="obj">Object to test</param>
	/// <exception cref="System.Exception">Thrown when the given object is null.</exception>
	public static void IsNull<T>( T obj ) => IsNull( obj, "Object should be null" );

	/// <summary>
	/// Throws an exception when the given object is not valid.
	/// </summary>
	public static void IsValid( IValid obj )
	{
		if ( !obj.IsValid() )
			throw new System.Exception( "Assert: IsValid" );
	}

	/// <summary>
	/// Throws an exception when the 2 given objects are not equal to each other.
	/// </summary>
	/// <typeparam name="T">Any type that supports <see cref="object.Equals(object?,object?)"/>.</typeparam>
	/// <param name="a">Object A to test.</param>
	/// <param name="b">Object B to test.</param>
	/// <param name="message">Message to include in the exception, if any.</param>
	/// <exception cref="System.Exception">Thrown when 2 given objects are not equal</exception>
	public static void AreEqual<T>( T a, T b, string message = null )
	{
		if ( !EqualityComparer<T>.Default.Equals( a, b ) )
			throw new System.Exception( $"Assert: AreEqual {message}" );
	}

	/// <summary>
	/// Throws an exception when the 2 given objects are equal to each other.
	/// </summary>
	public static void AreNotEqual<T>( T a, T b, string message = null )
	{
		if ( EqualityComparer<T>.Default.Equals( a, b ) )
			throw new System.Exception( $"Assert: AreNotEqual {message}" );
	}

	/// <summary>
	/// Throws an exception when given expression does not resolve to <b>true</b>.
	/// </summary>
	/// <param name="isValid">The expression to test</param>
	/// <param name="message">Message to include in the exception, if any.</param>
	/// <exception cref="System.Exception">Thrown when given expression is not <b>true</b>.</exception>
	public static void True( bool isValid, string message = null )
	{
		if ( !isValid )
			throw new System.Exception( $"Assert: {message ?? "True"}" );
	}

	/// <summary>
	/// Throws an exception when given expression does not resolve to <b>false</b>.
	/// </summary>
	/// <param name="isValid">The expression to test</param>
	/// <param name="message">Message to include in the exception, if any.</param>
	/// <exception cref="System.Exception">Thrown when given expression is not <b>false</b>.</exception>
	public static void False( bool isValid, string message = null )
	{
		if ( isValid )
			throw new System.Exception( $"Assert: {message ?? "False"}" );
	}
}
