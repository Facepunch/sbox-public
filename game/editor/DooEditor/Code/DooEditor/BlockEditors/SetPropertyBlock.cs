namespace Editor.DooEditor;

/// <summary>
/// Inspector for Doo Set Property blocks.
/// </summary>
[Inspector( typeof( Doo.SetPropertyBlock ) )]
public class SetPropertyBlock : InspectorWidget
{
	SerializedObject Target { get; }

	readonly SerializedProperty _targetComponent;
	readonly SerializedProperty _property;
	readonly SerializedProperty _value;

	public SetPropertyBlock( SerializedObject obj ) : base( obj )
	{
		Target = obj;
		Layout = Layout.Column();
		Layout.Spacing = 4;

		_targetComponent = Target.GetProperty( nameof( Doo.SetPropertyBlock.TargetComponent ) );
		_property = Target.GetProperty( nameof( Doo.SetPropertyBlock.Property ) );
		_value = Target.GetProperty( nameof( Doo.SetPropertyBlock.Value ) );

		_targetComponent.OnChanged += _ => BuildUI();
		_property.OnChanged += _ => BuildUI();
		BuildUI();
	}

	void BuildUI()
	{
		Layout.Clear( true );

		var targetComponent = _targetComponent.GetValue<Doo.TargetComponent>();
		var targetComponentType = targetComponent?.GetComponentType();
		var hasComponent = targetComponentType != null;

		var property = _property.GetCustomizable();
		property.SetDisplayName( "Property" );

		var cs = new ControlSheet();
		Layout.Add( cs );

		cs.AddRow( _targetComponent );

		if ( hasComponent )
		{
			var selector = cs.AddControl<ComponentPropertySelector>( property );
			selector.TargetComponentType = targetComponentType;
			selector.RequireWritable = true;
		}
        if ( !hasComponent )
            return;

		var propDesc = Doo.Helpers.FindProperty( _property.As.String );
		if ( propDesc == null )
            return;

		if ( !propDesc.DeclaringType.TargetType.IsAssignableFrom( targetComponentType ) )
			return;

        // fallbakc if the expression is null just in case
        var expr = _value.GetValue<Doo.Expression>();
        if ( expr == null )
        {
            var defaultValue = propDesc.PropertyType.IsValueType
                ? System.Activator.CreateInstance( propDesc.PropertyType )
                : null;
            _value.SetValue( new Doo.LiteralExpression
            {
                LiteralValue = new Variant( defaultValue, propDesc.PropertyType )
            } );
        }
        var value = _value.GetCustomizable();
		value.SetDisplayName( "Value" );
		value.AddAttribute( new TypeHintAttribute( propDesc.PropertyType ) );

		cs = new ControlSheet();
		Layout.Add( cs );
		cs.AddRow( value );
	}
}
