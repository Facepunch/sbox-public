namespace Sandbox;

/// <summary>
/// Provides data about a wrapped method in a <see cref="CodeGeneratorAttribute"/> callback.
/// </summary>
public readonly ref struct WrappedMethod
{
	/// <summary>
	/// Invoke the original method.
	/// </summary>
	public Action Resume { get; init; }

	/// <summary>
	/// The object whose method is being wrapped. This will be null if we're wrapping a static method.
	/// </summary>
	public object Object { get; init; }

	/// <summary>
	/// Is this a static method?
	/// </summary>
	public bool IsStatic { get; init; }

	/// <summary>
	/// The name of the type that the method belongs to.
	/// </summary>
	public string TypeName { get; init; }

	/// <summary>
	/// The name of the original method.
	/// </summary>
	public string MethodName { get; init; }

	/// <summary>
	/// The Identity of the original method. This is an integer that each MethodDescription has to distinguish itself from other methods of the same class.
	/// </summary>
	public int MethodIdentity { get; init; }

	/// <summary>
	/// The generic argument types of the method or null if the method is not generic.
	/// </summary>
	public Type[] GenericArguments { get; init; }

	/// <summary>
	/// An array of all attributes decorated with <see cref="CodeGeneratorAttribute"/> on the original method.
	/// </summary>
	public Attribute[] Attributes { get; init; }


	/// <summary>
	/// Get the attribute of type, or null if it doesn't exist
	/// </summary>
	public U GetAttribute<U>() where U : System.Attribute
	{
		for ( int i = 0; i < Attributes.Length; i++ )
		{
			if ( Attributes[i] is U t )
				return t;
		}

		return default;
	}
}

/// <summary>
/// Provides data about a wrapped method in a <see cref="CodeGeneratorAttribute"/> callback.
/// </summary>
/// <typeparam name="T">The expected return type for the wrapped method.</typeparam>
public readonly struct WrappedMethod<T>
{
	/// <summary>
	/// Invoke the original method.
	/// </summary>
	public Func<T> Resume { get; init; }

	/// <summary>
	/// The object whose method is being wrapped. This will be null if we're wrapping a static method.
	/// </summary>
	public object Object { get; init; }

	/// <summary>
	/// Is this a static method?
	/// </summary>
	public bool IsStatic { get; init; }

	/// <summary>
	/// The name of the type that the method belongs to.
	/// </summary>
	public string TypeName { get; init; }

	/// <summary>
	/// The name of the original method. If static, will return the full name including the type.
	/// </summary>
	public string MethodName { get; init; }

	/// <summary>
	/// The Identity of the original method. This is an integer that each MethodDescription has to distinguish itself from other methods of the same class.
	/// </summary>
	public int MethodIdentity { get; init; }

	/// <summary>
	/// The generic argument types of the method or null if the method is not generic.
	/// </summary>
	public Type[] GenericArguments { get; init; }

	/// <summary>
	/// An array of all attributes decorated with <see cref="CodeGeneratorAttribute"/> on the original method.
	/// </summary>
	public Attribute[] Attributes { get; init; }


	/// <summary>
	/// Get the attribute of type, or null if it doesn't exist
	/// </summary>
	public U GetAttribute<U>() where U : System.Attribute
	{
		for ( int i = 0; i < Attributes.Length; i++ )
		{
			if ( Attributes[i] is U t )
				return t;
		}

		return default;
	}
}
