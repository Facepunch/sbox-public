namespace Editor.DooEditor;

/// <summary>
/// Inspector for Doo Get Property blocks.
/// </summary>
[Inspector( typeof( Doo.GetPropertyBlock ) )]
public class GetPropertyBlock : InspectorWidget
{
	SerializedObject Target { get; }

	readonly SerializedProperty _targetComponent;
	readonly SerializedProperty _property;
	readonly SerializedProperty _returnVariable;

	public GetPropertyBlock( SerializedObject obj ) : base( obj )
	{
		Target = obj;
		Layout = Layout.Column();
		Layout.Spacing = 4;

		_targetComponent = Target.GetProperty( nameof( Doo.GetPropertyBlock.TargetComponent ) );
		_property = Target.GetProperty( nameof( Doo.GetPropertyBlock.Property ) );
		_returnVariable = Target.GetProperty( nameof( Doo.GetPropertyBlock.ReturnVariable ) );

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
			selector.RequireReadable = true;
		}
		if ( !hasComponent )
            return;

		var propDesc = Doo.Helpers.FindProperty( _property.As.String );
		if ( propDesc == null )
            return;

		if ( !propDesc.DeclaringType.TargetType.IsAssignableFrom( targetComponentType ) )
			return;

		Layout.Add( new Label.Header( $"Return Value ({propDesc.PropertyType.Name})" ) );
		cs = new ControlSheet();
		Layout.Add( cs );
		cs.AddControl<DooVariableControlWidget>( _returnVariable );
	}
}
