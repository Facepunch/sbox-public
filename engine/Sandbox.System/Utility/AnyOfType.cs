namespace Sandbox;

/// <summary>
/// A wrapper that holds an instance of any concrete type assignable to <typeparamref name="T"/>.
/// Use this as a property type when you want the inspector to let you pick from all
/// non-abstract implementations of an abstract class or interface.
/// <para>
/// <code>
/// public AnyOfType&lt;Scatterer&gt; MyScatterer { get; set; }
/// </code>
/// </para>
/// Serialization stores the concrete type name alongside the property values
/// </summary>
public readonly struct AnyOfType<T> where T : class
{
	/// <summary>
	/// The concrete instance, or null if no type is selected.
	/// </summary>
	public T Value { get; init; }

	/// <summary>
	/// Returns true if <see cref="Value"/> is not null.
	/// </summary>
	public bool HasValue => Value is not null;

	/// <summary>
	/// The concrete type name for serialization. When null, falls back to <see cref="Value"/>'s runtime type.
	/// </summary>
	internal string TypeName { get; init; }

	public AnyOfType( T value )
	{
		Value = value;
		TypeName = value?.GetType().FullName;
	}

	internal AnyOfType( T value, string typeName )
	{
		Value = value;
		TypeName = typeName;
	}

	/// <summary>
	/// Implicitly convert a concrete instance to <see cref="AnyOfType{T}"/>.
	/// </summary>
	public static implicit operator AnyOfType<T>( T value ) => new( value );

	/// <summary>
	/// Implicitly unwrap to the underlying value.
	/// </summary>
	public static implicit operator T( AnyOfType<T> wrapper ) => wrapper.Value;

	public override string ToString() => Value?.ToString() ?? "(none)";
}
