namespace Editor;

/// <summary>
/// Abstract class to enable easily creating ControlWidgets with dropdowns.
/// </summary>
public abstract class DropdownControlWidget<T> : ControlWidget
{
	public override bool SupportsMultiEdit => true;

	protected PopupWidget _menu;

	public DropdownControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Cursor = CursorShape.Finger;
	}

	public struct Entry
	{
		public T Value { get; set; }
		public string Label { get; set; }
		public string Icon { get; set; }
		public string Description { get; set; }
	}

	protected abstract IEnumerable<object> GetDropdownValues();

	/// <summary>
	/// Returns the display text shown in the dropdown button.
	/// </summary>
	protected virtual string GetDisplayText()
	{
		return SerializedProperty.GetValue<object>()?.ToString() ?? "None";
	}

	/// <summary>
	/// Called when a dropdown item is selected. Override for custom selection behavior.
	/// </summary>
	protected virtual void OnItemSelected( object item )
	{
		if ( item is Entry e )
			SerializedProperty.SetValue( e.Value );
		else
			SerializedProperty.SetValue( item );
	}

	protected override void PaintControl()
	{
		var color = IsControlHovered ? Theme.Blue : Theme.TextControl;
		var rect = new Rect( 0, 0, Width, Theme.RowHeight ).Shrink( 8, 0 );

		Paint.SetPen( color );
		Paint.SetDefaultFont();

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			Paint.SetPen( Theme.MultipleValues );
			Paint.DrawText( rect, "Multiple Values", TextFlag.LeftCenter );
		}
		else
		{
			Paint.DrawText( rect, GetDisplayText(), TextFlag.LeftCenter );
		}

		Paint.SetPen( color );
		Paint.DrawIcon( rect, "Arrow_Drop_Down", 17, TextFlag.RightCenter );
	}

	public override void StartEditing()
	{
		if ( !_menu.IsValid )
		{
			OpenMenu();
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( e.LeftMouseButton && !_menu.IsValid() )
		{
			OpenMenu();
		}
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		// nothing
	}

	void OpenMenu()
	{
		_menu = new PopupWidget( null );

		_menu.Layout = Layout.Column();
		_menu.Width = ScreenRect.Width;

		var scroller = _menu.Layout.Add( new ScrollArea( this ), 1 );
		scroller.Canvas = new Widget( scroller )
		{
			Layout = Layout.Column(),
			VerticalSizeMode = SizeMode.CanGrow | SizeMode.Expand
		};

		object[] entries = GetDropdownValues().ToArray();

		foreach ( var o in entries )
		{
			var b = scroller.Canvas.Layout.Add( new MenuOption<T>( o, SerializedProperty ) );
			b.MouseLeftPress = () =>
			{
				OnItemSelected( o );
				_menu.Update();
				_menu.Close();
			};
		}

		_menu.Position = ScreenRect.BottomLeft;
		_menu.Visible = true;
		_menu.AdjustSize();
		_menu.ConstrainToScreen();
		_menu.OnPaintOverride = PaintMenuBackground;
	}

	bool PaintMenuBackground()
	{
		Paint.SetBrushAndPen( Theme.ControlBackground );
		Paint.DrawRect( Paint.LocalRect, 0 );
		return true;
	}

}

file class MenuOption<T> : Widget
{
	object info;
	SerializedProperty property;

	public MenuOption( object e, SerializedProperty p ) : base( null )
	{
		info = e;
		property = p;

		Layout = Layout.Row();
		Layout.Margin = 8;

		if ( e is DropdownControlWidget<T>.Entry entry )
		{
			if ( !string.IsNullOrWhiteSpace( entry.Icon ) )
			{
				Layout.Add( new IconButton( entry.Icon ) { Background = Color.Transparent, TransparentForMouseEvents = true, IconSize = 18 } );
			}

			Layout.AddSpacingCell( 8 );
			var c = Layout.AddColumn();
			var title = c.Add( new Label( entry.Label ) );
			title.SetStyles( "font-size: 12px; font-weight: bold; font-family: Poppins; color: white;" );

			if ( !string.IsNullOrWhiteSpace( entry.Description ) )
			{
				var desc = c.Add( new Label( entry.Description.Trim( '\n', '\r', '\t', ' ' ) ) );
				desc.WordWrap = true;
				desc.MinimumHeight = 1;
				desc.MinimumWidth = 400;
			}
		}
		else
		{
			Layout.AddSpacingCell( 8 );
			var c = Layout.AddColumn();
			var title = c.Add( new Label( e.ToString() ) );
			title.SetStyles( "font-size: 12px; font-weight: bold; font-family: Poppins; color: white;" );
		}
	}

	bool HasValue()
	{
		if ( property.IsMultipleDifferentValues ) return false;

		var value = property.GetValue<object>( default );
		return value == info;
	}

	protected override void OnPaint()
	{
		if ( Paint.HasMouseOver || HasValue() )
		{
			Paint.SetBrushAndPen( Theme.Blue.WithAlpha( HasValue() ? 0.3f : 0.1f ) );
			Paint.DrawRect( LocalRect.Shrink( 2 ), 2 );
		}
	}
}

/// <summary>
/// A generic dropdown control widget. This is useful in circimstances where you want to
/// manually present a bunch of different options which aren't enums etc.
/// </summary>
sealed class DropdownControlWidget : DropdownControlWidget<object>
{
	public List<Entry> Entries { get; } = new();

	public DropdownControlWidget( SerializedProperty property ) : base( property )
	{
	}

	/// <summary>
	/// Clear the dropdown options
	/// </summary>
	public void ClearOptions()
	{
		Entries.Clear();
		Update();
	}

	/// <summary>
	/// Add an option to the dropdown options
	/// </summary>
	public void AddOption( object value, string label, string description = null, string icon = null )
	{
		Entries.Add( new DropdownControlWidget<object>.Entry { Value = value, Label = label, Description = description, Icon = icon } );
		Update();
	}

	protected override IEnumerable<object> GetDropdownValues()
	{
		foreach ( var entry in Entries )
		{
			yield return entry;
		}
	}
}
