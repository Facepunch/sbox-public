using System;

namespace Editor;

[CustomEditor( typeof( Enum ), WithAllAttributes = [typeof( EnumButtonGroupAttribute )] )]
public class GroupButtonControlWidget : ControlWidget
{
	/// <summary>
	/// If true, then this control is operating in flags mode (FlagsAttribute)
	/// </summary>
	public bool IsFlagsMode { get; init; }

	EnumDescription _enumDesc;

	public override bool IsControlButton => false;
	public override bool SupportsMultiEdit => true;
	protected virtual float? MenuWidthOverride => null;

	public GroupButtonControlWidget( SerializedProperty property ) : base( property )
	{
		var propertyType = property.NullableType ?? property.PropertyType;
		var typeDesc = EditorTypeLibrary.GetType( propertyType );
		if ( typeDesc is null )
		{
			Log.Warning( $"Couldn't create an enum editor for {propertyType} - it's not in EditorTypeLibrary" );
			return;
		}

		Cursor = CursorShape.Finger;
		IsFlagsMode = property.HasAttribute<FlagsAttribute>() || typeDesc.HasAttribute<FlagsAttribute>();

		Layout = Layout.Row();
		Layout.Spacing = 1;
		Layout.Margin = 1;

		_enumDesc = EditorTypeLibrary.GetEnumDescription( propertyType );

		foreach ( var o in _enumDesc )
		{
			if ( !o.Browsable )
				continue;

			var b = Layout.Add( new MenuOption( o, property, IsFlagsMode ) );
		}

	}
}

file class MenuOption : Widget
{
	EnumDescription.Entry info;
	SerializedProperty property;
	bool flagMode;

	Label _text;

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
		_text = c.Add( new Label( e.Title ) );
		_text.Color = Theme.Text;

		if ( !string.IsNullOrWhiteSpace( e.Description ) )
		{
			ToolTip = e.Description;
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
		if ( HasValue() )
		{
			Paint.SetBrushAndPen( Theme.Blue.WithAlpha( 0.9f ) );
		}
		else
		{
			Paint.SetBrushAndPen( Paint.HasMouseOver ? Theme.SurfaceBackground.WithAlpha( 1f ) : Theme.WidgetBackground.WithAlpha( 0.8f ) );
		}

		Paint.DrawRect( LocalRect, 3 );
		UpdateColors();
	}

	void UpdateColors()
	{
		_text?.Color = HasValue() ? Color.White : Theme.Text.WithAlpha( 0.7f );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		var value = property.GetValue<long>( 0 );

		if ( flagMode )
		{
			if ( (value & info.IntegerValue) != 0 )
			{
				value &= ~info.IntegerValue;
			}
			else
			{
				value |= info.IntegerValue;
			}
		}
		else
		{
			value = info.IntegerValue;
		}

		property.SetValue( value );
	}
}
