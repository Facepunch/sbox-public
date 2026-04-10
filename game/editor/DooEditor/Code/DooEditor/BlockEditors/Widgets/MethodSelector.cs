namespace Editor.DooEditor;

public class MethodSelector : ControlWidget
{
	public virtual string Icon => "🌍";

	public MethodSelector( SerializedProperty p ) : base( p )
	{
		Layout = Layout.Row();
		Layout.AddStretchCell();

		Cursor = CursorShape.Finger;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( e.LeftMouseButton )
		{
			OpenSelector();
		}
	}

	public void OpenSelector()
	{
		var popup = new AdvancedDropdownPopup( this );
		popup.Dropdown.RootTitle = "Method";
		popup.Dropdown.SearchPlaceholderText = "Find Methods";
		popup.Dropdown.OnBuildItems = BuildMethods;

		popup.Dropdown.OnSelect = ( value ) =>
		{
			if ( value is MethodDescription md )
			{
				SerializedProperty.SetValue( $"{md.TypeDescription.FullName}.{md.Name}" );
			}
		};
		popup.Dropdown.Rebuild();
		popup.OpenAtCursor();
	}


	protected virtual void BuildMethods( AdvancedDropdownItem root )
	{
		var methods = TypeLibrary.GetMethodsWithAttribute<Doo.MethodAttribute>( true );

		foreach ( var group in methods.GroupBy( x => x.Attribute.CategoryName ).OrderBy( x => x.Key ) )
		{
			var category = root.Add( group.Key );

			foreach ( var m in group.OrderBy( x => x.Attribute.Path ) )
			{
				category.Add( new AdvancedDropdownItem
				{
					Title = m.Attribute.Path,
					Description = m.Method.TypeDescription?.FullName,
					Value = m.Method
				} );
			}
		}
	}

	public virtual bool IsValidMethod( MethodDescription method )
	{
		return method.IsStatic;
	}

	protected override void PaintControl()
	{
		var rect = LocalRect.Shrink( 0, 0, Theme.RowHeight, 0 );

		var icon = rect.Shrink( 4, 4 ) with { Width = Theme.RowHeight - 8 };
		Paint.SetPen( Theme.Green.WithAlpha( 0.5f ) );
		Paint.DrawIcon( icon, "terminal", 17 );

		rect = rect.Shrink( Theme.RowHeight - 4, 0, 0, 0 );

		var methodpath = SerializedProperty.GetValue<string>()?.ToString();
		if ( string.IsNullOrWhiteSpace( methodpath ) )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( rect.Shrink( 8, 0 ), "No Method Selected", TextFlag.LeftCenter );
			return;
		}

		var methodDesc = Doo.Helpers.FindMethod( methodpath );
		if ( methodDesc == null )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( rect.Shrink( 8, 0 ), $"Missing: {methodpath}", TextFlag.LeftCenter );
			return;
		}

		if ( !IsValidMethod( methodDesc ) )
		{
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );
			Paint.DrawText( rect.Shrink( 8, 0 ), "No Method Selected", TextFlag.LeftCenter );
			return;
		}

		if ( methodDesc.GetCustomAttribute<Doo.MethodAttribute>() is Doo.MethodAttribute attr )
		{
			Paint.SetPen( Theme.Text );
			Paint.DrawText( rect.Shrink( 8, 0 ), attr.Path, TextFlag.LeftCenter );
			return;
		}

		Paint.SetPen( Theme.Text );
		Paint.DrawText( rect.Shrink( 8, 0 ), $"{methodDesc.DeclaringType.Name}.{methodDesc.Name}", TextFlag.LeftCenter );
	}
}

