using System;

namespace Editor;

[CustomEditor( typeof( Enum ) )]
public class EnumControlWidget : ControlWidget
{
	/// <summary>
	/// If true, then this control is operating in flags mode (FlagsAttribute)
	/// </summary>
	public bool IsFlagsMode { get; init; }

	EnumDescription _enumDesc;

	public override bool IsControlActive => base.IsControlActive || _menu.IsValid();

	public override bool IsControlButton => true;
	public override bool IsControlHovered => base.IsControlHovered || _menu.IsValid();

	public override bool SupportsMultiEdit => true;

	protected virtual float? MenuWidthOverride => null;

	public EnumControlWidget( SerializedProperty property ) : base( property )
	{
		var propertyType = property.NullableType ?? property.PropertyType;
		var typeDesc = EditorTypeLibrary.GetType( propertyType );
		if ( typeDesc is null )
		{
			Log.Warning( $"Couldn't create an enum editor for {propertyType} - it's not in EditorTypeLibrary" );
			return;
		}

		_enumDesc = EditorTypeLibrary.GetEnumDescription( propertyType );

		// Always use group button for small enums
		if ( _enumDesc?.Count() < 4 )
		{
			Layout = Layout.Row();
			Layout.Add( new GroupButtonControlWidget( property ) );
			_enumDesc = default;
			return;
		}

		IsFlagsMode = property.HasAttribute<FlagsAttribute>() || typeDesc.HasAttribute<FlagsAttribute>();

		Cursor = CursorShape.Finger;
		Layout = Layout.Row();
		Layout.Spacing = 2;
	}

	protected override void PaintControl()
	{
		if ( _enumDesc is null )
			return;

		var value = SerializedProperty.GetValue<long>( 0 );

		var color = IsControlHovered ? Theme.Blue : Theme.TextControl;
		if ( IsControlDisabled ) color = color.WithAlpha( 0.5f );

		var rect = LocalRect;

		rect = rect.Shrink( 8, 0 );

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			Paint.SetPen( Theme.MultipleValues );
			Paint.DrawText( rect, $"Multiple Values", TextFlag.LeftCenter );
		}
		else if ( IsFlagsMode )
		{
			var e = _enumDesc.GetEntries( value );
			var str = string.Join( ", ", e.Select( x => $"{x.Name}" ) );

			Paint.SetPen( color );
			Paint.DrawText( rect, str, TextFlag.LeftCenter );
		}
		else
		{
			var e = _enumDesc.GetEntry( value );

			if ( !string.IsNullOrEmpty( e.Icon ) )
			{
				Paint.SetPen( color.WithAlpha( 0.5f ) );
				var i = Paint.DrawIcon( rect, e.Icon, 16, TextFlag.LeftCenter );
				rect.Left += i.Width + 8;
			}

			Paint.SetPen( color );
			Paint.DrawText( rect, e.Title ?? "Unset", TextFlag.LeftCenter );
		}

		Paint.SetPen( color );
		Paint.DrawIcon( rect, "Arrow_Drop_Down", 17, TextFlag.RightCenter );

	}

	PopupWidget _menu;

	public override void StartEditing()
	{
		if ( IsControlDisabled ) return;

		if ( !_menu.IsValid )
		{
			OpenMenu();
		}
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( IsControlDisabled ) return;

		if ( e.LeftMouseButton && !_menu.IsValid() )
		{
			OpenMenu();
		}
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		// nothing
	}

	void ToggleValue( EnumDescription.Entry e )
	{
		var value = SerializedProperty.GetValue<long>( 0 );

		if ( IsFlagsMode )
		{
			if ( (value & e.IntegerValue) != 0 )
			{
				value &= ~e.IntegerValue;
			}
			else
			{
				value |= e.IntegerValue;
			}
		}
		else
		{
			value = e.IntegerValue;
		}


		SerializedProperty.SetValue( value );
	}

	void OpenMenu()
	{
		PropertyStartEdit();

		if ( _enumDesc is null )
			return;

		_menu = new PopupWidget( null );

		_menu.Layout = Layout.Column();
		var menuWidth = MenuWidthOverride ?? ScreenRect.Width;
		_menu.MinimumWidth = menuWidth;
		_menu.MaximumWidth = menuWidth;
		_menu.OnLostFocus += PropertyFinishEdit;
		_menu.VerticalSizeMode = SizeMode.CanGrow | SizeMode.Expand;

		var scroller = _menu.Layout.Add( new ScrollArea( this ), 1 );
		scroller.NoSystemBackground = true;
		scroller.TranslucentBackground = true;
		scroller.Canvas = new Widget( scroller )
		{
			Layout = Layout.Column(),
			VerticalSizeMode = SizeMode.CanGrow | SizeMode.Expand,
			MaximumWidth = menuWidth
		};

		var entries = IsFlagsMode ? _enumDesc.AsEnumerable() : _enumDesc;
		float h = 0;

		foreach ( var o in entries )
		{
			if ( !o.Browsable )
				continue;

			var b = scroller.Canvas.Layout.Add( new MenuOption( o, SerializedProperty, IsFlagsMode ) );
			b.MouseLeftPress = () =>
			{
				ToggleValue( o );
				_menu.Update();

				if ( !IsFlagsMode )
				{
					_menu.Close();
				}
			};

			h += b.FixedHeight;
		}

		scroller.Canvas.AdjustSize();

		_menu.Position = ScreenRect.BottomLeft;
		_menu.Visible = true;
		_menu.AdjustSize();
		_menu.ConstrainToScreen();
		_menu.OnPaintOverride = PaintMenuBackground;

		if ( h < 200 )
		{
			scroller.FixedHeight = h;
			_menu.FixedHeight = h;
		}

		if ( scroller.VerticalScrollbar.Minimum != scroller.VerticalScrollbar.Maximum )
		{
			scroller.Canvas.MaximumWidth -= 8; // leave some space for the scrollbar
		}
	}

	bool PaintMenuBackground()
	{
		Paint.SetBrushAndPen( Theme.ControlBackground, Theme.WidgetBackground, 1 );
		Paint.DrawRect( Paint.LocalRect.Shrink( 1 ), 4 );
		return true;
	}

}

file class MenuOption : Widget
{
	EnumDescription.Entry info;
	SerializedProperty property;
	bool flagMode;

	public MenuOption( EnumDescription.Entry e, SerializedProperty p, bool flags ) : base( null )
	{
		info = e;
		property = p;
		flagMode = flags;

		Layout = Layout.Row();
		Layout.Margin = 0;
		VerticalSizeMode = SizeMode.Default;
		FixedHeight = Theme.RowHeight;
		Cursor = CursorShape.Finger;

		if ( !string.IsNullOrWhiteSpace( e.Icon ) )
		{
			Layout.AddSpacingCell( 4 );
			Layout.Add( new IconButton( e.Icon ) { Background = Color.Transparent, TransparentForMouseEvents = true, IconSize = 12, FixedSize = Theme.RowHeight } );
		}

		var c = Layout.AddColumn();
		c.Margin = new Sandbox.UI.Margin( 8, 4 );
		var title = c.Add( new Label( e.Title ) );
		title.Color = Theme.Text;

		if ( !string.IsNullOrWhiteSpace( e.Description ) )
		{
			var desc = c.Add( new Label( e.Description.Trim( '\n', '\r', '\t', ' ' ) ) );
			desc.WordWrap = true;
			desc.MinimumHeight = 1;
			desc.VerticalSizeMode = SizeMode.CanGrow;
			desc.Color = Theme.Text.WithAlpha( 0.5f );
			FixedHeight = Theme.RowHeight * 2;

			c.AddStretchCell();
		}
	}

	bool HasValue()
	{
		var value = property.GetValue<long>( 0 );
		if ( flagMode ) return (value & info.IntegerValue) == info.IntegerValue;
		return value == info.IntegerValue;
	}

	protected override void OnPaint()
	{
		if ( Paint.HasMouseOver || HasValue() )
		{
			Paint.SetBrushAndPen( Theme.Blue.WithAlpha( HasValue() ? 0.5f : 0.1f ) );
			Paint.DrawRect( LocalRect );
		}
	}
}
