namespace Sandbox;

/// <summary>
/// Draws a thin line above this property or button in a <see cref="UISheet"/>.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method )]
public sealed class SeparatorAttribute : Attribute
{
}

/// <summary>
/// Puts this property or button in the same row as the one above it, buttons share the row evenly.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method )]
public sealed class SameLineAttribute : Attribute
{
}

/// <summary>
/// Puts this [Button] in the title of its group in a <see cref="UISheet"/>, as a square icon with its title as the tooltip.
/// For things that act on the whole group, like resetting it.
/// </summary>
[AttributeUsage( AttributeTargets.Method )]
public sealed class HeaderButtonAttribute : Attribute
{
}

/// <summary>
/// Leaves out the name on the left of a property in a <see cref="UISheet"/>, its control takes the whole row.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class HideLabelAttribute : Attribute
{
}

/// <summary>
/// A slider for this property only sets it when you let go of it or finish typing, not on every change.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class ApplyOnReleaseAttribute : Attribute
{
}

/// <summary>
/// A button or property in a <see cref="UISheet"/> only works while a property of the same object has this value, otherwise it shows greyed out.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method )]
public sealed class EnabledIfAttribute : Attribute
{
	public string PropertyName { get; }
	public object Value { get; }

	public EnabledIfAttribute( string propertyName, object value = null )
	{
		PropertyName = propertyName;
		Value = value ?? true;
	}

	public bool Test( SerializedObject target )
	{
		if ( target is null || !target.TryGetProperty( PropertyName, out var property ) ) return true;

		return Equals( property.GetValue<object>(), Value );
	}
}
