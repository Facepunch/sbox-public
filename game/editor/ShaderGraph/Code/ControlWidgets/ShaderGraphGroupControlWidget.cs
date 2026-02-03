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

		var groupProperty = GetGroupProperty( property );
		if ( !input.ShowGroups )
		{
			return;
		}

		var parentNode = GetShaderNode( property );
		if ( groupProperty is not null && parentNode is not null )
		{
			foreach ( var node in parentNode.Graph.Nodes )
			{
				var serialized = node.GetSerialized();
				foreach ( var prop in serialized )
				{
					if ( prop.PropertyType == typeof( IParameterUI ) || prop.PropertyType == typeof( TextureInput ) )
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

		}
		if ( originalProperty.PropertyType == typeof( TextureInput ) )
		{
			return originalProperty;
		}
		return GetGroupProperty( originalProperty.Parent?.ParentProperty );
	}

	SerializedProperty GetGroupProperty( SerializedProperty originalProperty )
	{
		if ( originalProperty is null )
		{
			return null;
		}
		if ( originalProperty.PropertyType == typeof( UIGroup ) )
		{
			return originalProperty;
		}
		return GetGroupProperty( originalProperty.Parent?.ParentProperty );
	}

	ShaderNode GetShaderNode( SerializedProperty originalProperty )
	{
		if ( originalProperty is null )
		{
			return null;
		}
		if ( originalProperty.Parent.Targets.First() is ShaderNode shaderNode )
		{
			return shaderNode;
		}
		return GetShaderNode( originalProperty.Parent?.ParentProperty );
	}
}
