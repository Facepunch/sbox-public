using Sandbox.UI;

namespace EngineNodeGraph;

/// <summary>
/// Inline colour swatch. A click opens the engine's <see cref="ColorPickerControl"/> in a popup,
/// bound to <see cref="Property"/>.
/// </summary>
public class ColorValueEditor : NodeValueEditor
{
	public string Title { get; set; }

	/// <summary>
	/// Read from <see cref="Property"/> every frame when one is set.
	/// </summary>
	public Color Value { get; set; }

	/// <summary>
	/// The property the colour is read from and the picker writes to. Without one the swatch
	/// only displays <see cref="Value"/> and can't open the picker.
	/// </summary>
	public SerializedProperty Property { get; set; }

	Popup _popup;

	/// <summary>
	/// With a title, the swatch takes the right half.
	/// </summary>
	public override float PreferredWidth => string.IsNullOrWhiteSpace( Title ) ? 32f : (NodePaint.Measure( Title ).x + 10f) * 2f;

	public ColorValueEditor( PlugPanel plug, SerializedProperty property = null ) : base( plug )
	{
		Property = property;
		Style.Cursor = "pointer";
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is null )
			return;

		var current = Property.GetValue<Color>();

		// The picker writes the property directly, so tell the node when it changed
		if ( current != Value && _popup is { IsValid: true } )
		{
			Plug.Node.Graph.NodeValueChanged();
			Plug.Node.MarkNodeChanged();
		}

		Value = current;
	}

	public override void OnDeleted()
	{
		if ( _popup is { IsValid: true } )
			_popup.Delete();

		base.OnDeleted();
	}

	protected override void Paint( ref NodePaint paint, Rect rect )
	{
		var field = NodeTheme.FieldRect( rect );
		var hover = paint.HasMouseOver;

		paint.SetPen( hover ? NodeTheme.HairlineHover : NodeTheme.Hairline );
		paint.SetBrush( NodeTheme.FieldBackground );
		paint.DrawRect( field, 4f );

		var swatchColor = Value;
		var max = MathF.Max( MathF.Max( swatchColor.r, swatchColor.g ), swatchColor.b );
		var intensity = 0f;

		// HDR: show the hue, brightening to the right with intensity
		if ( max > 1 )
		{
			intensity = max / 1000.0f;

			var div = 1.0f / max;
			swatchColor.r *= div;
			swatchColor.g *= div;
			swatchColor.b *= div;
		}

		var hasTitle = !string.IsNullOrWhiteSpace( Title );
		var swatchRect = field.Shrink( 3f );

		if ( hasTitle )
		{
			swatchRect = swatchRect.Shrink( swatchRect.Width * 0.5f, 0f, 0f, 0f );
		}

		paint.ClearPen();
		paint.SetBrush( NodeTheme.Checkerboard );
		paint.DrawRect( swatchRect, 3f );

		paint.SetPen( Color.White.WithAlpha( hover ? 0.25f : 0.12f ) );
		paint.SetBrushLinear( swatchColor, Color.Lerp( swatchColor, Color.White.WithAlpha( swatchColor.a ), intensity ) );
		paint.DrawRect( swatchRect, 3f );

		if ( hasTitle )
		{
			paint.SetDefaultFont();
			paint.SetPen( NodeTheme.TextMuted.Lighten( hover ? 0.3f : 0f ) );
			paint.DrawText( field.Shrink( 7f, 0f ), Title, TextFlag.LeftCenter );
		}
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( !Enabled || e.MouseButton != MouseButtons.Left )
		{
			base.OnMouseDown( e );
			return;
		}

		e.StopPropagation();

		if ( Property is null || _popup is { IsValid: true } )
			return;

		// Popup placement reads Box.Rect, which ignores the canvas pan/zoom transform. Anchor on the on-screen rect instead.
		var topLeft = PanelPositionToScreenPosition( Vector2.Zero );
		var bottomRight = PanelPositionToScreenPosition( Box.Rect.Size );
		_popup = new Popup { AnchorRect = new Rect( topLeft, bottomRight - topLeft ) };
		_popup.SetPositioning( this, Popup.PositionMode.RightTop, 1 );
		var picker = _popup.AddChild<ColorPickerControl>();
		picker.Property = Property;
	}
}
