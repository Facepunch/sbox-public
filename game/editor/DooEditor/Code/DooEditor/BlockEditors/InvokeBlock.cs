namespace Editor.DooEditor;

/// <summary>
/// Main window for editing Doo scripts.
/// </summary>
[Inspector( typeof( Doo.InvokeBlock ) )]
public class InvokeBlock : InspectorWidget
{
	SerializedObject Target { get; }

	readonly SerializedProperty _invokeType;
	readonly SerializedProperty _targetComponent;
	readonly SerializedProperty _memberProperty;
	readonly SerializedProperty _argumentsProperty;

	public InvokeBlock( SerializedObject obj ) : base( obj )
	{
		Target = obj;
		Layout = Layout.Column();
		Layout.Spacing = 4;

		_invokeType = Target.GetProperty( nameof( Doo.InvokeBlock.InvokeType ) );
		_invokeType.OnChanged += ( p ) => BuildUI();

		_targetComponent = Target.GetProperty( nameof( Doo.InvokeBlock.TargetComponent ) );
		_targetComponent.OnChanged += ( p ) => BuildUI();

		_memberProperty = Target.GetProperty( nameof( Doo.InvokeBlock.Member ) );
		_memberProperty.OnChanged += ( p ) => BuildUI();

		_argumentsProperty = Target.GetProperty( nameof( Doo.InvokeBlock.Arguments ) );

		BuildUI();
	}

	void BuildUI()
	{
		Layout.Clear( true );

		var invokeType = _invokeType.GetValue<Doo.InvokeType>();
		var targetComponent = _targetComponent.GetValue<Doo.TargetComponent>();
		var targetComponentType = targetComponent?.GetComponentType();
		var hasComponent = targetComponentType != null;

		var method = _memberProperty.GetCustomizable();
		method.SetDisplayName( "Method" );

		{
			var type = _invokeType.GetCustomizable();
			type.AddAttribute( new WideModeAttribute() { HasLabel = false } );

			var header = new ControlSheet();
			Layout.Add( header );
			header.AddRow( type );
		}

		Layout.AddSpacingCell( 16 );

		var cs = new ControlSheet();
		Layout.Add( cs );

		if ( invokeType == Doo.InvokeType.Member )
		{
			cs.AddRow( _targetComponent );

			if ( hasComponent )
			{
				var methodSelect = cs.AddControl<ComponentMethodSelector>( method );
				methodSelect.TargetComponentType = targetComponentType;
			}
		}
		else
		{
			var methodSelect = cs.AddControl<MethodSelector>( method );
		}

		// member invoke and no component
		if ( invokeType == Doo.InvokeType.Member && !hasComponent ) return;

		var methodDesc = Doo.Helpers.FindMethod( _memberProperty.As.String );
		if ( methodDesc == null ) return;

		// member invoke and not found on component!
		if ( invokeType == Doo.InvokeType.Member )
		{
			if ( targetComponent == null ) return;

			if ( !methodDesc.DeclaringType.TargetType.IsAssignableFrom( targetComponentType ) )
				return;
		}

		List<SerializedProperty> arguments = [];

		if ( _argumentsProperty.TryGetAsObject( out var obj ) && obj is SerializedCollection sc )
		{
			if ( sc.Count() != methodDesc.Parameters.Length )
			{
				while ( sc.Count() > 0 )
					sc.RemoveAt( 0 );

				foreach ( var param in methodDesc.Parameters )
				{
					if ( param.HasDefaultValue )
					{
						sc.Add( new Doo.LiteralExpression() { LiteralValue = new( param.RawDefaultValue, param.ParameterType ) } );
					}
					else
					{
						sc.Add( new Doo.LiteralExpression() { LiteralValue = new( null, param.ParameterType ) } );
					}
				}
			}

			if ( methodDesc.Parameters.Length == 0 )
			{
				return;
			}

			Layout.Add( new Label.Header( $"Parameters" ) );

			cs = new ControlSheet();
			Layout.Add( cs );

			var array = obj.ToArray();
			int i = 0;
			foreach ( var param in methodDesc.Parameters )
			{
				var prop = array[i];

				var csp = prop.GetCustomizable();
				csp.SetDisplayName( $"{param.Name.ToTitleCase()}" );
				csp.AddAttribute( new TypeHintAttribute( param.ParameterType ) );

				cs.AddRow( csp );
				i++;
			}
		}

		if ( methodDesc.ReturnType != typeof( void ) )
		{
			Layout.Add( new Label.Header( $"Return Value ({methodDesc.ReturnType.Name})" ) );

			cs = new ControlSheet();
			Layout.Add( cs );

			cs.AddControl<DooVariableControlWidget>( Target.GetProperty( nameof( Doo.InvokeBlock.ReturnVariable ) ) );
		}
	}
}

