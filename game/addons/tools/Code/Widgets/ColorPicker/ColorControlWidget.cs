namespace Editor;

/// <summary>
/// A full-width colour swatch. Click it to open the colour picker; hover it for the exact values.
/// </summary>
[CustomEditor( typeof( Color ) )]
[CustomEditor( typeof( Color32 ) )]
[CustomEditor( typeof( ColorHsv ) )]
public class ColorControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => true;

	ColorSwatchWidget colorSwatchWidget;

	public ColorControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();

		colorSwatchWidget = Layout.Add( new ColorSwatchWidget( property ) { FixedHeight = Theme.RowHeight, HorizontalSizeMode = SizeMode.Expand } );
	}

	protected override void OnPaint()
	{
		// nothing
	}

	/// <summary>
	/// Copying keeps full float precision. Pasting also accepts hex, named and css colours.
	/// </summary>
	public override void FromClipboardString( string clipboard )
	{
		clipboard = clipboard.Trim();

		if ( IsNumberList( clipboard ) || !Color.TryParse( clipboard, out var color ) )
		{
			base.FromClipboardString( clipboard );
			return;
		}

		object value = color;
		if ( SerializedProperty.PropertyType == typeof( Color32 ) ) value = color.ToColor32();
		else if ( SerializedProperty.PropertyType == typeof( ColorHsv ) ) value = color.ToHsv();

		SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
		SerializedProperty.SetValue( value );
		SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
	}

	static bool IsNumberList( string text ) => text.Split( ',' ).All( x => float.TryParse( x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _ ) );

	public void OpenColorPopup()
	{
		colorSwatchWidget?.OpenColorPopup();
	}
}


public class ColorSwatchWidget : ControlWidget
{
	public override bool SupportsMultiEdit => true;
	bool IsColor32 = false;
	bool IsColorHsv = false;
	bool HasAlpha = true;
	string _toolTipColor;

	public ColorSwatchWidget( SerializedProperty property ) : base( property )
	{
		Cursor = CursorShape.Finger;
		IsColor32 = property.PropertyType == typeof( Color32 );
		IsColorHsv = property.PropertyType == typeof( ColorHsv );

		if ( property.TryGetAttribute<ColorUsageAttribute>( out var colorUsage ) )
			HasAlpha = colorUsage.HasAlpha;
	}

	Color GetColor()
	{
		if ( IsColor32 ) return SerializedProperty.GetValue( Color32.White ).ToColor();
		if ( IsColorHsv ) return SerializedProperty.GetValue( Color.Magenta.ToHsv() ).ToColor();
		return SerializedProperty.GetValue( Color.Magenta );
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		PaintUnder();

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.5f ) );
			Paint.SetDefaultFont();
			Paint.DrawText( LocalRect, "Multiple Values", TextFlag.Center );
			return;
		}

		var color = GetColor();
		UpdateToolTip( color );

		var rect = LocalRect.Shrink( 3 );

		// Alpha shows as a bar along the bottom: white for the opaque fraction, black for the rest.
		var showAlpha = HasAlpha && color.a < 1;
		var swatchRect = showAlpha ? rect.Shrink( 0, 0, 0, 4 ) : rect;

		// HDR colours show their hue at full brightness, with a tag for the overbright part.
		var max = MathF.Max( color.r, MathF.Max( color.g, color.b ) );
		var shown = (max > 1 ? color * (1 / max) : color).WithAlpha( 1 );
		if ( IsControlDisabled )
			shown = shown.Desaturate( 0.5f ).Darken( 0.3f );

		Paint.ClearPen();
		Paint.SetBrush( shown );
		Paint.DrawRect( swatchRect, 2 );

		if ( showAlpha )
		{
			var barRect = rect.Shrink( 0, rect.Height - 3, 0, 0 );
			Paint.SetBrush( Color.Black );
			Paint.DrawRect( barRect );
			Paint.SetBrush( Color.White );
			Paint.DrawRect( barRect.Shrink( 0, 0, barRect.Width * (1 - color.a.Clamp( 0, 1 )), 0 ) );
		}

		if ( max > 1 )
		{
			Paint.SetPen( shown.Luminance > 0.5f ? Color.Black.WithAlpha( 0.7f ) : Color.White.WithAlpha( 0.8f ) );
			Paint.SetDefaultFont( 7, 600 );
			Paint.DrawText( swatchRect.Shrink( 6, 0 ), "HDR", TextFlag.RightCenter );
		}
	}

	void UpdateToolTip( Color color )
	{
		var text = HasAlpha
			? $"R {color.r:0.###}  G {color.g:0.###}  B {color.b:0.###}  A {color.a:0.###}"
			: $"R {color.r:0.###}  G {color.g:0.###}  B {color.b:0.###}";

		if ( color.IsSdr )
			text += $"\n{color.Hex}";

		if ( text == _toolTipColor )
			return;

		_toolTipColor = text;
		ToolTip = text;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		OpenColorPopup();
	}

	public void OpenColorPopup()
	{
		var color = GetColor();

		var picker = ColorPicker.OpenColorPopup( color, c =>
		{
			SerializedProperty.SetValue( c );
		}, ScreenRect.BottomLeft );

		picker.EditingStarted += () => PropertyStartEdit();
		picker.EditingFinished += () => PropertyFinishEdit();

		if ( SerializedProperty.TryGetAttribute<ColorUsageAttribute>( out var colorUsage ) )
		{
			picker.HasAlpha = colorUsage.HasAlpha;
			picker.IsHDR = colorUsage.IsHDR;
			picker.Update();
		}
	}
}

public class ColorStringWidget : StringControlWidget
{
	bool IsColor32 = false;
	bool IsColorHsv = false;

	public ColorStringWidget( SerializedProperty property ) : base( property )
	{
		IsColor32 = property.PropertyType == typeof( Color32 );
		IsColorHsv = property.PropertyType == typeof( ColorHsv );
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		if ( !LineEdit.IsFocused )
			return;

		if ( SerializedProperty.TryGetAttribute<ColorUsageAttribute>( out var colorUsage ) )
		{
			// Force alpha to 1 if not using alpha
			if ( !colorUsage.HasAlpha )
			{
				var col = SerializedProperty.GetValue<Color>();
				SerializedProperty.SetValue( col.WithAlpha( 1 ) );
			}
		}
	}

	protected override string ValueToString()
	{
		if ( IsColor32 )
		{
			var color32 = SerializedProperty.GetValue( Color.Magenta.ToColor32() );
			return color32.Hex;
		}
		else if ( IsColorHsv )
		{
			var colorHsv = SerializedProperty.GetValue( Color.Magenta.ToHsv() );
			return colorHsv.ToString();
		}

		var color = SerializedProperty.GetValue( Color.Magenta );
		return color.ToString( true, true );
	}

	protected override object StringToValue( string text )
	{
		ArgumentNullException.ThrowIfNull( text );

		var color = Color.White;

		if ( Color.TryParse( text, out var newColor ) )
		{
			color = newColor;
		}

		if ( IsColor32 )
		{
			return color.ToColor32();
		}
		else if ( IsColorHsv )
		{
			return color.ToHsv();
		}

		return color;
	}
}

public class ColorVectorWidget : ControlWidget
{
	public override bool SupportsMultiEdit => true;

	private bool _hasAlpha = true;
	public bool HasAlpha
	{
		get => _hasAlpha;
		set
		{
			_hasAlpha = value;
			if ( !_hasAlpha )
			{
				// Force alpha to 1 if not using alpha
				var col = Target.ParentProperty.GetValue<Color>();
				Target.ParentProperty.SetValue( col.WithAlpha( 1 ) );
			}
			Update();
		}
	}

	bool IsColorHsv = false;

	SerializedObject Target;
	FloatControlWidget FirstControl;

	public ColorVectorWidget( SerializedProperty property ) : base( property )
	{
		property.TryGetAsObject( out Target );
		if ( Target is null )
		{
			Log.Warning( $"Error when trying to get {property} as object" );
			return;
		}

		if ( Target is null )
			return;

		IsColorHsv = property.PropertyType == typeof( ColorHsv );

		if ( property.TryGetAttribute<ColorUsageAttribute>( out var colorUsage ) )
		{
			HasAlpha = colorUsage.HasAlpha;
		}

		Layout = Layout.Row();
		Layout.Spacing = 2;

		if ( IsColorHsv )
		{
			FirstControl = TryAddField( "Hue", Theme.Pink, "H" );
			TryAddField( "Saturation", Theme.Yellow, "S" );
			TryAddField( "Value", Theme.Blue.Lighten( 0.2f ), "V" );
			TryAddField( "Alpha", Color.White, "A" );
		}
		else
		{
			FirstControl = TryAddField( "r", Theme.Red, "R" );
			TryAddField( "g", Theme.Green, "G" );
			TryAddField( "b", Theme.Blue, "B" );
			TryAddField( "a", Color.White, "A" );
		}
	}

	private FloatControlWidget TryAddField( string propertyName, Color color, string text )
	{
		var prop = Target.GetProperty( propertyName );
		if ( prop is null ) return null;
		if ( !HasAlpha && text == "A" ) return null;

		FloatControlWidget control = null;

		if ( IsColorHsv )
			control = Layout.Add( new FloatControlWidget( prop ) { HighlightColor = color, Label = text } );
		else
			control = Layout.Add( new IntegerControlWidget( prop ) { HighlightColor = color, Label = text } );

		control.MinimumWidth = Theme.RowHeight;
		control.HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;

		control.HasRange = true;
		control.RangeClamped = true;

		if ( IsColorHsv )
		{
			if ( text == "H" )
				control.Range = new Vector2( 0, 360 );
			else
				control.Range = new Vector2( 0f, 1f );

			control.RangeStep = 0.01f;
		}
		else
		{
			control.Range = new Vector2( 0, 255 );
			control.RangeStep = 1f;
		}

		return control;
	}

	public override void StartEditing()
	{
		FirstControl?.StartEditing();
	}

	protected override void OnPaint()
	{
		// nothing
	}
}
