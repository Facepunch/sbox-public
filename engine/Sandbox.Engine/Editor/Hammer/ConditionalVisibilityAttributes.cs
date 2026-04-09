
using Sandbox;

/// <summary>
/// Hide a property if a condition matches.
/// </summary>
public abstract class ConditionalVisibilityAttribute : InspectorVisibilityAttribute
{
	// TODO - we should change this to return a flag indicating that we want
	// * show / hidden
	// * enabled / disabled

	/// <summary>
	/// The test condition.
	/// </summary>
	/// <param name="targetObject">The class instance of the property this attribute is attached to.</param>
	/// <param name="td">Description of the <paramref name="targetObject"/>'s type.</param>
	/// <returns>Return true if the property should be visible.</returns>
	public abstract bool TestCondition( object targetObject, TypeDescription td );
}


/// <summary>
/// Hide this property if a given property within the same class has the given value. Used typically in the Editor Inspector.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = true )]
public class HideIfAttribute : ConditionalVisibilityAttribute
{
	/// <summary>
	/// Property name to test.
	/// </summary>
	public string PropertyName { get; set; }

	/// <summary>
	/// Property value to test against.
	/// </summary>
	public object Value { get; set; }

	public HideIfAttribute( string propertyName, object value )
	{
		PropertyName = propertyName;
		Value = value;
	}

	public override bool TestCondition( object targetObject, TypeDescription td )
	{
		var property = td.GetProperty( PropertyName );
		if ( property == null ) return true;
		if ( !property.CanRead ) return true;

		var val = property.GetValue( targetObject );
		if ( val == Value ) return false;
		if ( $"{val}" == $"{Value}" ) return false;

		return true;
	}

	public override bool TestCondition( SerializedObject so )
	{
		if ( so.TryGetProperty( PropertyName, out var property ) )
		{
			var value = property.GetValue<object>();
			return Equals( value, Value );
		}

		Log.Warning( $"HideIfAttribute: Couldn't find property '{PropertyName}' on {so.TypeName}" );
		return true;
	}
}

/// <summary>
/// Show this property if a given property within the same class has the given value. Used typically in the Editor Inspector.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = true )]
public class ShowIfAttribute : HideIfAttribute
{
	public ShowIfAttribute( string propertyName, object value ) : base( propertyName, value )
	{
	}

	public override bool TestCondition( object targetObject, TypeDescription td )
	{
		// opposite
		return !base.TestCondition( targetObject, td );
	}

	public override bool TestCondition( SerializedObject so )
	{
		// opposite
		return !base.TestCondition( so );
	}
}
