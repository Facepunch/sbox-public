namespace Editor.DooEditor;

public class ComponentPropertySelector : ControlWidget
{
	public Type TargetComponentType { get; set; }
	public bool RequireReadable { get; set; }
	public bool RequireWritable { get; set; }

	public ComponentPropertySelector( SerializedProperty p ) : base( p )
	{
		Layout = Layout.Row();
		Layout.AddStretchCell();
		Cursor = CursorShape.Finger;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( e.LeftMouseButton )
			OpenSelector();
	}

	public void OpenSelector()
	{
		var popup = new AdvancedDropdownPopup( this );
		popup.Dropdown.RootTitle = "Property";
		popup.Dropdown.SearchPlaceholderText = "Find Properties";
		popup.Dropdown.OnBuildItems = BuildProperties;

		popup.Dropdown.OnSelect = value =>
		{
			if ( value is PropertyDescription pd )
				SerializedProperty.SetValue( $"{pd.TypeDescription.FullName}.{pd.Name}" );
		};

		popup.Dropdown.Rebuild();
		popup.OpenAtCursor();
	}

	void BuildProperties( AdvancedDropdownItem root )
	{
		if ( TargetComponentType is null )
			return;
		var t = TypeLibrary.GetType( TargetComponentType.FullName );
		if ( t == null )
			return;

		var properties = t.Members.OfType<PropertyDescription>().Where( ShouldShow ).ToArray();
		foreach ( var group in properties.GroupBy( x => x.DeclaringType?.Name ).OrderBy( x => x.Key ) )
		{
			var category = root.Add( group.Key );
			foreach ( var p in group.OrderBy( x => x.Name ) )
			{
				category.Add( new AdvancedDropdownItem
				{
					Title = p.Name,
					Description = p.PropertyType?.Name,
					Value = p
				} );
			}
		}
	}

	bool ShouldShow( PropertyDescription description )
	{
		if ( !description.IsPublic ) return false;
		if ( description.DeclaringType == null ) return false;
		if ( description.IsIndexer ) return false;
		if ( description.IsStatic ) return false;

		if ( RequireReadable && !(description.CanRead && description.IsGetMethodPublic) ) return false;
		if ( RequireWritable && !(description.CanWrite && description.IsSetMethodPublic) ) return false;

		return true;
	}

	protected override void PaintControl()
	{
		var rect = LocalRect.Shrink( 0, 0, Theme.RowHeight, 0 );

		var icon = rect.Shrink( 4, 4 ) with { Width = Theme.RowHeight - 8 };
		Paint.SetPen( Theme.Green.WithAlpha( 0.5f ) );
		Paint.DrawIcon( icon, "tune", 17 );
		rect = rect.Shrink( Theme.RowHeight - 4, 0, 0, 0 );

		var propertyPath = SerializedProperty.GetValue<string>()?.ToString();
		if ( string.IsNullOrWhiteSpace( propertyPath ) )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( rect.Shrink( 8, 0 ), "No Property Selected", TextFlag.LeftCenter );
			return;
		}

		var propDesc = Doo.Helpers.FindProperty( propertyPath );
		if ( propDesc == null )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( rect.Shrink( 8, 0 ), $"Missing: {propertyPath}", TextFlag.LeftCenter );
			return;
		}

		Paint.SetPen( Theme.Text );
		Paint.DrawText( rect.Shrink( 8, 0 ), $"{propDesc.DeclaringType.Name}.{propDesc.Name}", TextFlag.LeftCenter );
	}
}
