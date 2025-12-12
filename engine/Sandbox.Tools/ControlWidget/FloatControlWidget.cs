using System;

namespace Editor;

[CustomEditor( typeof( float ) )]
[CustomEditor( typeof( decimal ) )]
[CustomEditor( typeof( double ) )]
public class FloatControlWidget : StringControlWidget
{
	public Color HighlightColor { get; set; }
	public string Icon { get; set; }
	public string Label { get; set; }
	public Action<Rect, float> SliderPaint { get; set; }

	/// <summary>
	/// If true we can draw a slider
	/// </summary>
	public bool HasRange { get; set; }

	/// <summary>
	/// The range, min and max
	/// </summary>
	public Vector2 Range { get; set; }

	/// <summary>
	/// The step size between range
	/// </summary>
	public float RangeStep { get; set; }

	/// <summary>
	/// True if the range is clamped between min and max
	/// </summary>
	public bool RangeClamped { get; set; }

	private FloatSlider SliderWidget;

	public FloatControlWidget( SerializedProperty property ) : base( property )
	{
		Cursor = CursorShape.SizeH;
		Label = "f";
		HighlightColor = Theme.Green;
		MinimumWidth = 60;

		MakeRanged( property );
	}

	internal void MakeRanged( SerializedProperty property )
	{
		var attributes = property.GetAttributes();

		var stepAttribute = attributes.OfType<StepAttribute>().FirstOrDefault();
		if ( stepAttribute is not null )
		{
			RangeStep = stepAttribute.Step;
		}

		var clampAttribute = attributes.OfType<RangeAttribute>().FirstOrDefault();
		if ( clampAttribute is not null )
		{
			MakeRanged(
				new Vector2( clampAttribute.Min, clampAttribute.Max ),
				RangeStep,
				clampAttribute.Clamped,
				clampAttribute.Slider
			);
		}
	}

	public void MakeRanged( Vector2 range, float step, bool clamped, bool slider )
	{
		HasRange = true;
		Range = range;
		RangeStep = step;
		RangeClamped = clamped;

		if ( !slider )
			return;

		SliderWidget ??= new FloatSlider( this );
		SliderWidget.SliderPaint = PaintSliderInternal;
		SliderWidget.Minimum = Range.x;
		SliderWidget.Maximum = Range.y;
		SliderWidget.Step = RangeStep;
		SliderWidget.HighlightColor = HighlightColor;
		SliderWidget.OnValueEdited = () =>
		{
			if ( ReadOnly ) return;
			SerializedProperty.As.Float = SliderWidget.Value;
			LineEdit.Blur();
		};
		SliderWidget.EditingStarted += PropertyStartEdit;
		SliderWidget.EditingFinished += PropertyFinishEdit;
	}

	protected override void DoLayout()
	{
		var rect = LocalRect;

		if ( Icon is not null || Label is not null )
		{
			rect.Left += Theme.RowHeight;
		}

		if ( SliderWidget is not null )
		{
			var sliderRect = rect.Shrink( 2, 0, 60, 0 );
			SliderWidget.Position = sliderRect.Position;
			SliderWidget.Size = sliderRect.Size;

			rect.Left += SliderWidget.Width + 2;
		}

		LineEdit.Position = rect.Position;
		LineEdit.Size = rect.Size;
	}

	internal static string ValueToStringImpl( SerializedProperty property )
	{
		var value = property.As.Double;

		// Ensure positive zero
		if ( value == 0.0d ) value = 0.0d;

		return value.ToString( "0.###" );
	}

	internal static object StringToValueImpl( string text, SerializedProperty property )
	{
		Type underlyingType = Nullable.GetUnderlyingType( property.PropertyType ) ?? property.PropertyType;
		return Convert.ChangeType( text.ToDoubleEval( property.As.Double ), underlyingType );
	}

	protected override string ValueToString() => ValueToStringImpl( SerializedProperty );

	protected override object StringToValue( string text ) => StringToValueImpl( text, SerializedProperty );

	internal void PaintSliderInternal( Rect rect, float pos )
	{
		if ( SliderPaint is null )
		{
			PaintSlider( rect, pos );
		}
		else
		{
			SliderPaint( rect, pos );
		}
	}

	protected virtual void PaintSlider( Rect rect, float pos )
	{
		if ( SliderWidget is null )
			return;

		SliderWidget.HighlightColor = HighlightColor;
		SliderWidget.PaintSlider( rect, pos );
	}

	protected override void PaintControl()
	{
		var h = Size.y;
		bool hovered = IsUnderMouse;
		if ( !Enabled ) hovered = false;

		if ( Icon == null && Label == null )
			return;

		// icon box
		Paint.ClearPen();
		Paint.SetBrush( HighlightColor.Darken( hovered ? 0.7f : 0.8f ).Desaturate( 0.8f ).WithAlphaMultiplied( IsControlDisabled ? 0.5f : 1.0f ) );
		Paint.DrawRect( new Rect( 0, 0, h, h ).Shrink( 2 ), Theme.ControlRadius - 1.0f );

		Paint.SetPen( HighlightColor.Darken( hovered ? 0.0f : 0.1f ).Desaturate( hovered ? 0.0f : 0.2f ).WithAlphaMultiplied( IsControlDisabled ? 0.5f : 1.0f ) );

		if ( string.IsNullOrEmpty( Label ) )
		{
			Paint.DrawIcon( new Rect( 0, h ), Icon, h - 6, TextFlag.Center );
		}
		else
		{
			Paint.SetHeadingFont( 9, 500 );
			Paint.DrawText( new Rect( 1, h - 1 ), Label, TextFlag.Center );
		}
	}

	float dragValue = default;
	bool dragging = false;
	bool dragPressed = false;

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.RightMouseButton && !dragPressed )
		{
			LineEdit.Focus();
			LineEdit.SelectAll();
		}

		if ( e.LeftMouseButton && !ReadOnly && e.LocalPosition.x < Height )
		{
			LineEdit.Blur();

			dragPressed = true;
			dragValue = SerializedProperty.As.Float;
			e.Accepted = true;
			PropertyStartEdit();
			return;
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		if ( e.LeftMouseButton && !ReadOnly && dragging )
		{
			LineEdit.Focus();
			e.Accepted = true;
		}

		if ( e.LeftMouseButton )
		{
			dragPressed = false;
			Cursor = CursorShape.SizeH;
			dragging = false;
			PropertyFinishEdit();
		}
	}

	protected virtual void OnDragValue( decimal add ) { /*Value = add;*/ }
	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		if ( !dragPressed )
			return;

		if ( e.ButtonState.Contains( MouseButtons.Left ) )
		{
			dragging = true;
			Update();

			var dragStep = (RangeStep > 0f) ? RangeStep : (HasRange ? (Range.y - Range.x) / 1000.0f : 0.01f);
			var delta = Application.CursorDelta.x * dragStep;
			if ( e.ButtonState.Contains( MouseButtons.Right ) ) delta *= 0.1f;

			dragValue += delta;
			if ( HasRange && RangeClamped ) dragValue = dragValue.Clamp( Range.x, Range.y );

			var steppedValue = (RangeStep != 0.0f) ? dragValue.SnapToGrid( RangeStep ) : dragValue;
			SerializedProperty.As.Float = steppedValue;

			LineEdit.Text = ValueToString();
			SignalValuesChanged();

			Application.CursorPosition = ScreenPosition + Theme.RowHeight * 0.5f;
			Cursor = CursorShape.Blank;

			e.Accepted = true;

			return;
		}
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		if ( SliderWidget is not null )
		{
			SliderWidget.Value = SerializedProperty.As.Float;
		}
	}
}

public class FloatSlider : Widget
{
	public float Minimum { get; set; }
	public float Maximum { get; set; }
	public Action OnValueEdited { get; set; }
	public Color HighlightColor { get; set; } = Theme.TextLight;
	public Action<Rect, float> SliderPaint { get; set; }

	public Action EditingStarted { get; set; }
	public Action EditingFinished { get; set; }

	float _value;

	public float Value
	{
		get => _value;
		set
		{
			var snapped = Step > 0 ? value.SnapToGrid( Step ) : value;
			snapped = snapped.Clamp( Minimum, Maximum );
			if ( _value == snapped ) return;

			_value = snapped;
			Update();
		}
	}

	public float DeltaValue
	{
		get
		{
			return MathX.LerpInverse( Value, Minimum, Maximum, true );
		}

		set
		{
			var v = MathX.LerpTo( Minimum, Maximum, value, true );
			Value = v;
		}

	}

	public float Step { get; set; } = 0.01f;

	const float thumbWidth = 6;
	const float thumbHeight = 12;

	public FloatSlider( Widget parent ) : base( parent )
	{
		Minimum = 0;
		Maximum = 100;
		Value = 25;

		MinimumSize = Theme.RowHeight;
		MaximumSize = new Vector2( 4096, Theme.RowHeight );

		Cursor = CursorShape.Arrow;
	}

	protected override void OnMouseEnter()
	{
		base.OnMouseEnter();
		Update();
	}

	protected override void OnMouseLeave()
	{
		base.OnMouseLeave();
		Update();
	}

	internal void PaintSlider( Rect rect, float pos )
	{
		var trackWidth = 5.0f;
		var center = rect.Height / 2.0f;
		var halfThumb = thumbWidth * 0.5f;
		var width = rect.Width;
		var highlightColor = HighlightColor;

		Paint.Antialiasing = true;

		Paint.ClearPen();

		Paint.SetBrush( highlightColor.WithAlpha( 0.2f ) );
		Paint.DrawRect( new( halfThumb, center - trackWidth * 0.5f, width - thumbWidth, trackWidth ), 3.0f );

		Paint.SetBrush( highlightColor.WithAlpha( 0.8f ) );
		Paint.DrawRect( new( halfThumb, center - trackWidth * 0.5f, pos, trackWidth ), 3.0f );

		Paint.SetBrush( Theme.TextControl );
		Paint.SetPen( Theme.WindowBackground.WithAlpha( 0.3f ), 1 );
		Paint.DrawRect( new( pos, center - thumbHeight * 0.5f, thumbWidth, thumbHeight ), 2 );
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		var pos = DeltaValue * (Width - thumbWidth);

		if ( SliderPaint is null )
		{
			PaintSlider( LocalRect, pos );
		}
		else
		{
			SliderPaint( LocalRect, pos );
		}
	}

	void UpdateFromLocalPosition( float position )
	{
		if ( ReadOnly )
			return;

		var delta = (position - thumbWidth * 0.5f) / (Width - thumbWidth);
		DeltaValue = delta.Clamp( 0.0f, 1.0f );
		OnValueEdited?.Invoke();

		SignalValuesChanged();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( ReadOnly )
			return;

		e.Accepted = true;

		if ( e.LeftMouseButton )
		{
			EditingStarted?.Invoke();
			UpdateFromLocalPosition( e.LocalPosition.x );
		}
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( ReadOnly )
			return;

		e.Accepted = true;

		if ( e.ButtonState.Contains( MouseButtons.Left ) )
		{
			UpdateFromLocalPosition( e.LocalPosition.x );
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		e.Accepted = true;

		if ( e.LeftMouseButton )
		{
			EditingFinished?.Invoke();
		}
	}
}
