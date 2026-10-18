namespace Sandbox.UI.NodeGraph;

/// <summary>
/// Inline float editor. With a range (<see cref="Min"/> != <see cref="Max"/>) it is a slider,
/// otherwise dragging sideways scrubs the value.
/// </summary>
public class FloatValueEditor : NodeValueEditor
{
	public string Title { get; set; }

	/// <summary>
	/// Read from <see cref="Property"/> every frame when one is set.
	/// </summary>
	public float Value { get; set; }

	public float Min { get; set; } = 0;
	public float Max { get; set; } = 1;
	public float Step { get; set; } = 0f;

	/// <summary>
	/// The property the value is read from and written to.
	/// </summary>
	public SerializedProperty Property { get; set; }

	bool IsRange => Min != Max;

	bool _dragging;
	Vector2 _scrubLast;
	bool? _rangeCursor;

	public FloatValueEditor( PlugPanel plug, SerializedProperty property = null ) : base( plug )
	{
		Property = property;
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is not null )
			Value = Property.GetValue<float>();

		if ( _dragging && (!HasActive || !Enabled) )
			EndDrag();

		if ( _dragging )
		{
			if ( IsRange )
			{
				UpdateValue( MousePosition.x * ScaleFromScreen );
			}
			else
			{
				var position = ScreenMousePosition;
				var moved = (position.x - _scrubLast.x) / ScaleToScreen;
				_scrubLast = position;

				if ( moved != 0 )
				{
					Value += moved * 0.01f;
					Commit();
				}
			}
		}

		if ( _rangeCursor != IsRange )
		{
			_rangeCursor = IsRange;
			Style.Cursor = IsRange ? "pointer" : "ew-resize";
		}
	}

	public override void OnDeleted()
	{
		EndDrag();
		base.OnDeleted();
	}

	protected override void Paint( ref NodePaint paint, Rect rect )
	{
		var isRange = IsRange;
		var sliderBg = NodeTheme.Blue.WithAlpha( 0.6f );
		var bg = NodeTheme.ControlBackground.WithAlpha( 0.4f );
		var fg = NodeTheme.TextControl;

		if ( !paint.HasMouseOver )
		{
			bg = bg.Darken( 0.2f );
			fg = fg.Darken( 0.1f );
			sliderBg = sliderBg.WithAlpha( 0.5f );
		}

		rect = rect.Shrink( 1 );

		paint.ClearPen();
		paint.SetBrush( bg );
		paint.DrawRect( rect, 2 );

		if ( isRange && Value >= Min )
		{
			paint.SetBrush( sliderBg.WithAlpha( 0.2f ) );
			paint.DrawRect( rect.Shrink( 1, 1, (Value <= Max) ? 40 : 1, 1 ), 2 );

			var sliderRect = rect.Shrink( 1, 1, (Value <= Max) ? 40 : 1, 1 );
			sliderRect.Right = sliderRect.Left.LerpTo( sliderRect.Right, (Value - Min) / (Max - Min) );

			paint.SetBrush( sliderBg );
			paint.DrawRect( sliderRect, 2 );
		}

		if ( !isRange )
		{
			paint.SetBrush( HasActive ? sliderBg : sliderBg.WithAlpha( 0.2f ) );
			paint.DrawRect( rect.Shrink( 1 ), 2 );

			if ( paint.HasMouseOver )
			{
				paint.SetPen( fg );
				paint.DrawIcon( rect.Shrink( 1 ), "navigate_before", 10, TextFlag.LeftCenter );
				paint.DrawIcon( rect.Shrink( 1 ), "navigate_next", 10, TextFlag.RightCenter );
			}
		}

		paint.SetDefaultFont();
		paint.SetPen( fg );

		var shrink = isRange ? 4 : 10;

		if ( !string.IsNullOrWhiteSpace( Title ) )
		{
			paint.DrawText( rect.Shrink( shrink, 0, shrink, 0 ), Title, TextFlag.LeftCenter );
		}

		paint.DrawText( rect.Shrink( shrink, 0, shrink, 0 ), $"{Value:0.000}", TextFlag.RightCenter );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( !Enabled || e.MouseButton != MouseButtons.Left )
		{
			base.OnMouseDown( e );
			return;
		}

		_dragging = true;

		if ( IsRange )
		{
			UpdateValue( MousePosition.x * ScaleFromScreen );
		}
		else
		{
			_scrubLast = ScreenMousePosition;
		}

		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		if ( !_dragging )
		{
			base.OnMouseUp( e );
			return;
		}

		EndDrag();
		e.StopPropagation();
	}

	void EndDrag()
	{
		_dragging = false;
	}

	void UpdateValue( float position )
	{
		var width = Box.Rect.Width * ScaleFromScreen;

		var value = (position - 1) / (width - 43);
		value = Min.LerpTo( Max, value ).Clamp( Min, Max );

		if ( Step > 0 )
		{
			value = (float)Math.Round( value / Step ) * Step;
		}

		if ( value == Value )
			return;

		Value = value;
		Commit();
	}

	void Commit()
	{
		Property?.SetValue( Value );
		Plug.Node.MarkNodeChanged();
	}
}
