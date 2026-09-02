namespace Sandbox.UI;

/// <summary>
/// Dragging the label in front of a number pushes its value up and down, the same as the
/// editor's number widgets. Holding the right button at the same time moves it in finer steps.
/// </summary>
public partial class NumberEntry
{
	/// <summary>
	/// How much one pixel of drag is worth when there's no range to work it out from.
	/// </summary>
	const float DefaultStep = 0.01f;

	/// <summary>
	/// How much finer the step gets while the right button is held.
	/// </summary>
	const float FineScale = 0.1f;

	bool _scrubbing;
	float _scrubValue;
	Vector2 _scrubLast;

	/// <summary>
	/// Is this position over the label in front of the number - the part you drag?
	/// </summary>
	bool IsOnScrubHandle( Vector2 screenPosition )
	{
		if ( !PrefixLabel.IsValid() ) return false;

		return PrefixLabel.Box.Rect.IsInside( screenPosition );
	}

	/// <summary>
	/// What one pixel of drag is worth. A ranged number covers its range over a thousand
	/// pixels, anything else moves in hundredths.
	/// </summary>
	float ScrubStep
	{
		get
		{
			if ( MinValue.HasValue && MaxValue.HasValue )
				return (MaxValue.Value - MinValue.Value) / 1000.0f;

			return DefaultStep;
		}
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( CanEdit && e.Button == "mouseleft" && IsOnScrubHandle( ScreenMousePosition ) )
		{
			_scrubbing = true;
			_scrubLast = ScreenMousePosition;
			_scrubValue = CurrentValue();

			// Typing and scrubbing at the same time makes no sense
			Blur();

			e.StopPropagation();
			return;
		}

		base.OnMouseDown( e );
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		// The cursor says the label is a handle before you press it
		Style.Cursor = IsOnScrubHandle( ScreenMousePosition ) ? "ew-resize" : null;

		if ( !_scrubbing )
		{
			base.OnMouseMove( e );
			return;
		}

		var position = ScreenMousePosition;
		var moved = (position.x - _scrubLast.x) / ScaleToScreen;
		_scrubLast = position;

		var step = ScrubStep;
		if ( e.MouseButton == MouseButtons.Right ) step *= FineScale;

		_scrubValue += moved * step;
		_scrubValue = _scrubValue.Clamp( MinValue ?? _scrubValue, MaxValue ?? _scrubValue );

		SetValue( _scrubValue );

		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		if ( _scrubbing )
		{
			_scrubbing = false;
			e.StopPropagation();
			return;
		}

		base.OnMouseUp( e );
	}

	/// <summary>
	/// Selection is driven from the surface rather than through mouse events, so stopping those
	/// isn't enough - a scrub would drag-select the text underneath it too.
	/// </summary>
	protected override void OnDragSelect( SelectionEvent e )
	{
		if ( _scrubbing ) return;

		base.OnDragSelect( e );
	}

	/// <summary>
	/// The number as it stands, from the property if there is one.
	/// </summary>
	float CurrentValue()
	{
		if ( Property is not null ) return Property.As.Float;

		return float.TryParse( Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value ) ? value : 0.0f;
	}

	/// <summary>
	/// Write a scrubbed value out, rounded when this is a whole number entry.
	/// </summary>
	void SetValue( float value )
	{
		if ( WholeNumbers ) value = MathF.Round( value );

		if ( Property is not null )
		{
			Property.As.Float = value;
		}

		Text = value.ToString( WholeNumbers ? "0" : NumberFormat, System.Globalization.CultureInfo.InvariantCulture );

		OnValueChanged();
	}
}
