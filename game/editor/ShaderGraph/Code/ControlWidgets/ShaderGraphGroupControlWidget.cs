namespace Editor.ShaderGraph;

[CustomEditor( typeof( string ), NamedEditor = "shadergraphgroup" )]
internal class ShaderGraphGroupControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => false;

	ComboBox _comboBox;

	public ShaderGraphGroupControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();

		_comboBox = Layout.Add( new ComboBox( this ) );

		var currentVal = SerializedProperty.GetValue<string>();
		List<string> namesSoFar = [currentVal];

		_comboBox.AddItem( "" );
		if ( !string.IsNullOrEmpty( currentVal ) )
		{
			_comboBox.AddItem( currentVal );
			_comboBox.CurrentIndex = 1;
		}

		var groupProperty = GetProperty<UIGroup>( property );
		var parameterUIProperty = groupProperty.Parent?.ParentProperty;

		if ( !parameterUIProperty.PropertyType.IsAssignableTo( typeof( IParameterUI ) ) )
			return;

		// Case for hiding Group properties.
		if ( parameterUIProperty.PropertyType.IsAssignableFrom( typeof( TextureInput ) ) )
		{
			if ( !parameterUIProperty.GetValue<TextureInput>().ShowGroups )
				return;
		}

		var blackboardProperty = parameterUIProperty.Parent;

		if ( !blackboardProperty.TryGetProperty( nameof( BlackboardParameter.Graph ), out var graphProp ) )
		{
			return;
		}

		var graph = graphProp.GetValue<ShaderGraph>();
		if ( groupProperty is not null && graph is not null )
		{
			foreach ( var parameter in graph.Parameters )
			{
				var serialized = parameter.GetSerialized();

				foreach ( var prop in serialized )
				{
					if ( prop.PropertyType.IsAssignableTo( typeof( IParameterUI ) ) )
					{
						if ( prop.TryGetAsObject( out var propObj ) )
						{
							// Get same property name so groups only show group names, sub-groups only show sub-group names, ect
							var innerProp = propObj.GetProperty( groupProperty?.Name );
							var groupVal = innerProp?.GetValue<UIGroup>();
							if ( !string.IsNullOrEmpty( groupVal?.Name ) && !namesSoFar.Contains( groupVal?.Name ) )
							{
								_comboBox.AddItem( groupVal?.Name );
								namesSoFar.Add( groupVal?.Name );
							}
						}
					}
				}
			}
		}

		_comboBox.Editable = true;
		_comboBox.Insertion = ComboBox.InsertMode.Skip;

		_comboBox.TextChanged += () =>
		{
			SerializedProperty.SetValue<string>( _comboBox.CurrentText );
		};
	}

	SerializedProperty GetProperty<T>( SerializedProperty originalProperty )
	{
		if ( originalProperty is null )
		{
			return null;
		}
		if ( originalProperty.PropertyType == typeof( T ) )
		{
			return originalProperty;
		}
		return GetProperty<T>( originalProperty.Parent?.ParentProperty );
	}
}
