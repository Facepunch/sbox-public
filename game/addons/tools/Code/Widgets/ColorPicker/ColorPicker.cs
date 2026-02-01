
using Sandbox.UI;

namespace Editor;

/// <summary>
/// A color picker widget that makes it easy to select or edit colors
/// </summary>
public partial class ColorPicker : Widget
{
	public Action<Color> ValueChanged { get; set; }

	bool _hasAlpha = true;
	public bool HasAlpha
	{
		get => _hasAlpha;
		set
		{
			if ( _hasAlpha == value ) return;
			_hasAlpha = value;
			if ( !_hasAlpha )
			{
				this.GetSerialized().GetProperty( nameof( Alpha ) ).SetValue( 1.0f );
			}
			InitRgbaField();
		}
	}
	public bool IsHDR { get; set; } = true;



	public event Action EditingFinished;

	public event Action EditingStarted;

	/// <summary>
	/// The current color value
	/// </summary>
	public Color Value
	{
		get
		{
			var color = ValueWithoutRange;

			color.r *= Range;
			color.g *= Range;
			color.b *= Range;

			return color;
		}

		set
		{
			var max = MathF.Max( value.r, value.g );
			max = MathF.Max( max, value.b );


			Range = 1;

			if ( max > 1 )
			{
				Range = max;

				var div = 1.0f / max;
				value.r *= div;
				value.g *= div;
				value.b *= div;
			}

			var c = value.ToHsv();
			Hue = c.Hue;
			Alpha = c.Alpha;
			Hsv = c;

			Update();
			ValueChanged?.Invoke( c );
		}
	}


	public Color ValueWithoutRange
	{
		get
		{
			Hsv.Hue = Hue;
			Hsv.Alpha = Alpha;

			var color = Hsv.ToColor();

			return color;
		}
	}

	ColorHsv Hsv;

	[Range( 0, 360 ), Step( 1 )]
	public float Hue { get; set; } = 1.0f;

	[Range( 0, 1 ), Step( 0.01f ), ShowIf( nameof( HasAlpha ), true )]
	public float Alpha { get; set; } = 1.0f;

	[Range( 0, 1000 ), Step( 0.01f ), ShowIf( nameof( IsHDR ), true )]
	public float Range { get; set; } = 1.0f;

	Slider PickerBlock { get; init; }
	ColorSampler ColorSampler { get; init; }
	HsvFloatControlWidget AlphaSlider { get; init; }
	HsvFloatControlWidget RangeSlider { get; init; }
	Layout InputRow { get; init; }
	LineEdit RgbaField { get; set; }

	public ColorPicker( Widget parent, Color startColor = default ) : base( parent )
	{
		Layout = Layout.Column();

		Value = startColor;

		Layout.Spacing = 0.0f;

		PickerBlock = new Slider( this );

		if ( parent.IsValid() )
		{
			PickerBlock.FixedHeight = PickerBlock.Width = parent.Width;
		}

		var window = parent as PopupWidget;
		ColorSampler = new ColorSampler();
		ColorSampler.OnPicked = ( v ) =>
		{
			Value = v;
			SignalValuesChanged();

			if ( window.IsValid() )
			{
				window.PreventDestruction = false;
				window.Show();
			}
		};
		ColorSampler.OnCancelled = () =>
		{
			var window = parent as PopupWidget;
			if ( window.IsValid() )
			{
				window.Show();
			}
		};

		var so = this.GetSerialized();

		var hueSlider = new HsvFloatControlWidget( so.GetProperty( nameof( Hue ) ), HueSliderPaint );
		AlphaSlider = new HsvFloatControlWidget( so.GetProperty( nameof( Alpha ) ), AlphaSliderPaint );
		RangeSlider = new HsvFloatControlWidget( so.GetProperty( nameof( Range ) ), RangeSliderPaint, 3 );

		so.OnPropertyStartEdit += ( p ) => EditingStarted?.Invoke();
		so.OnPropertyFinishEdit += ( p ) => EditingFinished?.Invoke();
		PickerBlock.EditingStarted += () => EditingStarted?.Invoke();
		PickerBlock.EditingFinished += () => EditingFinished?.Invoke();

		PickerBlock.Bind( "Value" ).From<Vector2>( () => new( Hsv.Saturation, 1.0f - Hsv.Value ), value => Hsv = Hsv.WithSaturation( value.x ).WithValue( 1.0f - value.y ) );

		Layout.Add( PickerBlock );

		var sliders = Layout.AddColumn();
		{
			sliders.Spacing = 2.0f;
			sliders.Margin = new Margin( 10, 0 );
			sliders.Add( hueSlider );
			sliders.Add( AlphaSlider );
			sliders.Add( RangeSlider );
		}

		InputRow = Layout.AddRow();
		{
			InputRow.Spacing = 5.0f;
			InputRow.Margin = 10.0f;

			var samplerButton = InputRow.Add( new IconButton( "colorize" ) );
			samplerButton.OnClick = () =>
			{
				if ( window.IsValid() )
				{
					window.PreventDestruction = true;
					window.Hide();
				}

				ColorSampler.Show();
			};

			var hexedit = InputRow.Add( new LineEdit( this ) );
			hexedit.Alignment = TextFlag.Center;
			hexedit.Bind( "Value" ).From( () => ValueWithoutRange.Hex, hex => Value = Color.Parse( hex ) ?? Color.Parse( "#" + hex ) ?? Color.Black );
			hexedit.FixedWidth = 75;

			InitRgbaField();
		}

		var palettes = Layout.AddColumn();
		{
			palettes.Margin = new Margin( 10, 0 );
			var palette = palettes.Add( new ColorPalette( this ) );
			palette.Bind( "Value" ).From( this, "Value" );
			palette.Options = EditorCookie.Get( "tools.colorpalette", new List<Color>() );
		}

		Layout.AddSpacingCell( 10 );
	}

	private void InitRgbaField()
	{
		RgbaField?.Destroy();
		RgbaField = new LineEdit( this );
		RgbaField.Alignment = TextFlag.Center;
		InputRow.Add( RgbaField );
		if ( HasAlpha )
		{
			RgbaField.Bind( "Value" ).From( () => $"{Value.r:0.00}, {Value.g:0.00}, {Value.b:0.00}, {Value.a:0.00}", hex => Value = Color.Parse( hex ) ?? Color.Black );
		}
		else
		{
			RgbaField.Bind( "Value" ).From( () => $"{Value.r:0.00}, {Value.g:0.00}, {Value.b:0.00}", hex => Value = (Color.Parse( hex ) ?? Color.Black).WithAlpha( 1 ) );
		}
	}

	private void HueSliderPaint( Rect rect, float pos )
	{
		Paint.ClearPen();
		Paint.Antialiasing = true;
		rect = rect.Shrink( 0, 4 );

		var steps = 10.0f;

		for ( int i = 0; i < steps; i++ )
		{
			var currentAlpha = i / steps;
			var nextAlpha = (i + 1) / steps;

			var colorA = new ColorHsv( currentAlpha * 360.0f, 1.0f, 1.0f );
			var colorB = new ColorHsv( nextAlpha * 360.0f, 1.0f, 1.0f );

			var pointA = rect.Left + currentAlpha * rect.Width;
			var pointB = rect.Left + nextAlpha * rect.Width;
			var gradientRect = new Rect( pointA, rect.Top, rect.Width / steps, rect.Height );

			var radius = 0f;

			if ( i == 0 || (i == steps - 1) )
			{
				radius = Theme.ControlRadius;

				if ( i == 0 )
					gradientRect.Left += 1;
				else
					gradientRect.Left -= 1;
			}

			Paint.SetBrushLinear( new Vector2( pointA, 0 ), new Vector2( pointB, 0 ), colorA, colorB );
			Paint.DrawRect( gradientRect.Grow( 1.0f, 0 ), radius );
		}

		PaintHandle( rect, pos );
	}

	private void AlphaSliderPaint( Rect rect, float pos )
	{
		Paint.ClearPen();
		Paint.Antialiasing = true;
		rect = rect.Shrink( 0, 4 );

		Paint.SetBrush( "/image/transparent-small.png" );
		Paint.DrawRect( rect, Theme.ControlRadius );

		Paint.SetBrushLinear( rect.Position, rect.TopRight, Color.Transparent, ValueWithoutRange.WithAlpha( 1.0f ) );
		Paint.DrawRect( rect, Theme.ControlRadius );

		PaintHandle( rect, pos );
	}

	private void RangeSliderPaint( Rect rect, float pos )
	{
		Paint.ClearPen();
		Paint.Antialiasing = true;
		rect = rect.Shrink( 0, 4 );

		Paint.SetBrushLinear( rect.Position, rect.TopRight, "#333", "#fff" );
		Paint.DrawRect( rect, Theme.ControlRadius );

		PaintHandle( rect, pos );
	}

	protected virtual void PaintHandle( Rect rect, float pos )
	{
		pos /= rect.Width;
		pos *= rect.Width - rect.Height * 0.5f;
		var circleThickness = 2.1f;
		var circleSize = 8;
		var circlePosition = rect.TopLeft + new Vector2( pos + 5, rect.Height * 0.5f );
		var circleColor = Color.White;

		Paint.Antialiasing = true;

		Paint.SetBrushRadial( circlePosition, 11, Color.Black.WithAlpha( .72f ), Color.Transparent );
		Paint.ClearPen();
		Paint.DrawCircle( circlePosition + 1, circleSize + 6f );

		Paint.SetPen( circleColor, circleThickness );
		Paint.SetBrush( "/image/transparent-small.png" );
		Paint.DrawCircle( circlePosition, circleSize );
		Paint.SetBrush( ValueWithoutRange );
		Paint.DrawCircle( circlePosition, circleSize );
	}

	public override void ChildValuesChanged( Widget source )
	{
		base.ChildValuesChanged( source );

		ValueChanged?.Invoke( Value );
		Update();
	}
	protected override void Signal( WidgetSignal signal )
	{
		if ( signal.Type == "save_colorpalette" && signal.SourceWidget is ColorPalette p )
		{
			EditorCookie.Set( "tools.colorpalette", p.Options );
		}

		base.Signal( signal );
	}

	public override void Update()
	{
		base.Update();

		if ( AlphaSlider is not null )
			AlphaSlider.Visible = HasAlpha;
		if ( RangeSlider is not null )
			RangeSlider.Visible = IsHDR;
	}

	[WidgetGallery]
	[Title( "ColorPicker" )]
	[Icon( "palette" )]
	internal static Widget WidgetGallery()
	{
		var canvas = new Widget( null );
		canvas.Layout = Layout.Column();
		canvas.Layout.Add( new ColorPicker( null, new Color( .25f, .25f, .8f, 1 ) ) );
		canvas.SetSizeMode( SizeMode.CanShrink, SizeMode.CanShrink );

		return canvas;
	}

	public static ColorPicker OpenColorPopup( Color color, Action<Color> onChange, Vector2? position = null )
	{
		var popup = new PopupWidget( null );
		popup.FixedWidth = 233;
		popup.Layout = Layout.Column();
		popup.Position = position ?? Application.CursorPosition;

		var colorPicker = popup.Layout.Add( new ColorPicker( popup, color ), 1 );
		colorPicker.OnChildValuesChanged += ( Widget w ) =>
		{
			onChange?.Invoke( colorPicker.Value );
		};

		popup.Show();
		popup.ConstrainToScreen();

		return colorPicker;
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		PickerBlock.FixedHeight = PickerBlock.Width;
	}
}

class HsvFloatControlWidget : FloatControlWidget
{
	public override bool IncludeLabel => false;
	public override bool IsWideMode => true;

	public HsvFloatControlWidget( SerializedProperty property, Action<Rect, float> sliderPaint, float curve = 1f ) : base( property )
	{
		Label = null;
		Icon = "multiple_stop";
		SliderPaint = sliderPaint;
		SliderCurve = curve;
	}
}
