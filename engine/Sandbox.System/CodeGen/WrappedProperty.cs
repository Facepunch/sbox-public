namespace Sandbox;

/// <summary>
/// Provides data about a wrapped property setter in a <see cref="CodeGeneratorAttribute"/> callback.
/// </summary>
/// <typeparam name="T">The expected type of the wrapped property.</typeparam>
public readonly ref struct WrappedPropertySet<T>
{
	/// <summary>
	/// The value the property wants to be set to.
	/// </summary>
	public T Value { get; init; }

	/// <summary>
	/// The object whose property is being wrapped. This will be null if we're wrapping a static property.
	/// </summary>
	public object Object { get; init; }

	/// <summary>
	/// Invoke the original setter with the provided value.
	/// </summary>
	public Action<T> Setter { get; init; }

	/// <summary>
	/// Get the current value
	/// </summary>
	public Func<T> Getter { get; init; }

	/// <summary>
	/// Is this a static property?
	/// </summary>
	public bool IsStatic { get; init; }

	/// <summary>
	/// The name of the type that the property belongs to.
	/// </summary>
	public string TypeName { get; init; }

	/// <summary>
	/// The name of the original property. If static, will return the full name including the type.
	/// </summary>
	public string PropertyName { get; init; }

	/// <summary>
	/// The identity of the original property. Used by TypeLibrary as a unique identifier for the property.
	/// </summary>
	public int MemberIdent { get; init; }

	/// <summary>
	/// An array of all attributes on the original property.
	/// </summary>
	public Attribute[] Attributes { get; init; }

	/// <summary>
	/// Get the attributes of the specified type, or null if it doesn't exist.
	/// </summary>
	public U GetAttribute<U>() where U : System.Attribute
	{
		var attributes = Attributes;
		var length = attributes.Length;

		if ( length == 1 )
			return attributes[0] as U;

		for ( int i = 0; i < length; i++ )
		{
			if ( attributes[i] is U t )
				return t;
		}

		return null;
	}
}

/// <summary>
/// Provides data about a wrapped property getter in a <see cref="CodeGeneratorAttribute"/> callback.
/// </summary>
/// <typeparam name="T">The expected type of the wrapped property.</typeparam>
public readonly ref struct WrappedPropertyGet<T>
{
	/// <summary>
	/// The value from the original getter.
	/// </summary>
	public T Value { get; init; }

	/// <summary>
	/// The object whose property is being wrapped. This will be null if we're wrapping a static property.
	/// </summary>
	public object Object { get; init; }

	/// <summary>
	/// Is this a static property?
	/// </summary>
	public bool IsStatic { get; init; }

	/// <summary>
	/// The name of the type that the property belongs to.
	/// </summary>
	public string TypeName { get; init; }

	/// <summary>
	/// The name of the original property. If static, will return the full name including the type.
	/// </summary>
	public string PropertyName { get; init; }

	/// <summary>
	/// The identity of the original property. Used by TypeLibrary as a unique identifier for the property.
	/// </summary>
	public int MemberIdent { get; init; }

	/// <summary>
	/// An array of all attributes on the original property.
	/// </summary>
	public Attribute[] Attributes { get; init; }

	/// <summary>
	/// Get the attributes of the specified type, or null if it doesn't exist.
	/// </summary>
	public U GetAttribute<U>() where U : System.Attribute
	{
		var attributes = Attributes;
		var length = attributes.Length;

		if ( length == 1 )
			return attributes[0] as U;

		for ( int i = 0; i < length; i++ )
		{
			if ( attributes[i] is U t )
				return t;
		}

		return null;
	}
}
