namespace Editor;

/// <summary>
/// Editor control widget for <see cref="AnyOfType{T}"/>.
/// Shows a dropdown to select the concrete type, and inline property editors for the selected instance.
/// </summary>
[CustomEditor( typeof( AnyOfType<> ) )]
sealed class AnyOfTypeControlWidget : DropdownControlWidget<TypeDescription>
{
	public override bool SupportsMultiEdit => false;

	Type _baseType;
	TypeDescription _wrapperType;
	Layout _propertyContainer;

	public AnyOfTypeControlWidget( SerializedProperty property ) : base( property )
	{
		_baseType = property.PropertyType.GenericTypeArguments[0];
		_wrapperType = EditorTypeLibrary.GetType( typeof( AnyOfType<> ).MakeGenericType( _baseType ) );

		Layout = Layout.Column();
		Layout.AddSpacingCell( Theme.RowHeight );

		_propertyContainer = Layout.AddColumn();
		_propertyContainer.Margin = new Sandbox.UI.Margin( 0, 2, 0, 0 );

		var inner = GetInnerValue();
		if ( inner is not null )
			RebuildPropertySheet( inner );
	}

	protected override string GetDisplayText()
	{
		var inner = GetInnerValue();
		if ( inner is not null )
			return DisplayInfo.ForType( inner.GetType() ).Name;

		return $"Select {DisplayInfo.ForType( _baseType ).Name}...";
	}

	protected override IEnumerable<object> GetDropdownValues()
	{
		yield return new Entry { Value = null, Label = "None", Icon = "block" };

		foreach ( var type in EditorTypeLibrary.GetTypes( _baseType ).OrderBy( x => x.Title ) )
		{
			if ( type.IsAbstract ) continue;
			if ( !type.TargetType.IsAssignableTo( _baseType ) ) continue;

			yield return new Entry { Value = type, Label = type.Title, Icon = type.Icon ?? "widgets" };
		}
	}

	protected override void OnItemSelected( object item )
	{
		var typeDesc = item is Entry e ? e.Value : item as TypeDescription;

		_propertyContainer.Clear( true );

		if ( typeDesc is null )
		{
			SerializedProperty.SetValue( _wrapperType.Create<object>() );
			return;
		}

		var instance = typeDesc.Create<object>();
		if ( instance is null ) return;

		WriteWrapper( instance );
		RebuildPropertySheet( instance );
	}

	object GetInnerValue()
	{
		var wrapper = SerializedProperty.GetValue<object>();
		return wrapper?.GetType().GetProperty( "Value" )?.GetValue( wrapper );
	}

	void WriteWrapper( object instance )
	{
		SerializedProperty.SetValue( _wrapperType.Create<object>( [instance] ) );
	}

	void RebuildPropertySheet( object instance )
	{
		_propertyContainer.Clear( true );
		if ( instance is null ) return;

		var so = instance.GetSerialized();
		if ( so is null ) return;

		so.OnPropertyChanged += ( _ ) => WriteWrapper( instance );

		var cs = new ControlSheet();
		cs.AddObject( so );
		cs.Margin = 0;

		_propertyContainer.Add( cs );
	}
}
