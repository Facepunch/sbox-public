using System.Diagnostics.CodeAnalysis;

namespace Editor;

/// <summary>
/// A control widget that converts its property into a SerializedObject, so it can edit subproperties
/// </summary>
public class ControlObjectWidget : ControlWidget
{
	public SerializedObject SerializedObject { get; private set; }

	/// <summary>
	/// Do we want to create a new instance when editing a property of
	/// the given type containing null?
	/// </summary>
	private static bool ShouldCreateInstanceWhenNull( SerializedProperty property )
	{
		var type = property.PropertyType;

		if ( type.IsAbstract ) return false;

		// Allow Nullable<T> to be null
		if ( type.IsValueType ) return false;

		// There's nothing to edit inside a plain System.Object instance
		if ( type == typeof( object ) ) return false;

		// Don't try to create delegates
		if ( type.IsAssignableTo( typeof( Delegate ) ) ) return false;

		if ( property.HasAttribute<AllowNullAttribute>() ) return false;

		return true;
	}

	public ControlObjectWidget( SerializedProperty property, bool create ) : base( property )
	{
		try
		{
			//
			// Create new entry, no null?
			//
			if ( create && property.GetValue<object>() == null && ShouldCreateInstanceWhenNull( property ) )
			{
				var newValue = Activator.CreateInstance( property.PropertyType );
				property.SetValue( newValue );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't create ControlObjectWidget: {e}" );
		}

		if ( property.TryGetAsObject( out var obj ) )
		{
			SerializedObject = obj;
		}
	}
}
