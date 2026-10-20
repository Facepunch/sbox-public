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

	public override float PreferredWidth
	{
		get
		{
			var title = string.IsNullOrWhiteSpace( Title ) ? 0f : NodePaint.Measure( Title ).x + 8f;
			var value = NodePaint.Measure( $"{Value:0.000}" ).x;

			return 4f + (IsRange ? 14f : 24f) + title + value;
		}
	}

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
		var field = NodeTheme.FieldRect( rect );
		var hover = paint.HasMouseOver || HasActive;

		paint.SetPen( hover ? NodeTheme.HairlineHover : NodeTheme.Hairline );
		paint.SetBrush( NodeTheme.FieldBackground );
		paint.DrawRect( field, 4f );

		var shrink = 7f;

		if ( IsRange )
		{
			var t = Math.Clamp( (Value - Min) / (Max - Min), 0f, 1f );

			if ( t > 0f )
			{
				var fill = field.Shrink( 1f );
				fill.Width *= t;

				paint.ClearPen();
				paint.SetBrush( NodeTheme.Accent.WithAlpha( hover ? 0.5f : 0.38f ) );
				paint.DrawRect( fill, 3f );
			}
		}
		else
		{
			shrink = 12f;

			if ( hover )
			{
				paint.SetPen( NodeTheme.TextMuted );
				paint.DrawIcon( field.Shrink( 1f, 0f ), "chevron_left", 10f, TextFlag.LeftCenter );
				paint.DrawIcon( field.Shrink( 1f, 0f ), "chevron_right", 10f, TextFlag.RightCenter );
			}
		}

		paint.SetDefaultFont();

		if ( !string.IsNullOrWhiteSpace( Title ) )
		{
			paint.SetPen( NodeTheme.TextMuted.Lighten( hover ? 0.3f : 0f ) );
			paint.DrawText( field.Shrink( shrink, 0f ), Title, TextFlag.LeftCenter );
		}

		paint.SetPen( NodeTheme.Text );
		paint.DrawText( field.Shrink( shrink, 0f ), $"{Value:0.000}", TextFlag.RightCenter );
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
		var field = NodeTheme.FieldRect( new Rect( 0f, Box.Rect.Size * ScaleFromScreen ) ).Shrink( 1f );

		var value = (position - field.Left) / field.Width;
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
		Plug.Node.Graph.NodeValueChanged();
		Plug.Node.MarkNodeChanged();
	}
}
