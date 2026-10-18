namespace Sandbox.UI.NodeGraph;

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
			Plug.Node.MarkNodeChanged();

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
		var bg = NodeTheme.ControlBackground.WithAlpha( 0.4f );
		var fg = NodeTheme.TextControl;

		if ( !paint.HasMouseOver )
		{
			bg = bg.Darken( 0.1f );
			fg = fg.Darken( 0.1f );
		}

		rect = rect.Shrink( 1 );

		paint.ClearPen();
		paint.SetBrush( bg );
		paint.DrawRect( rect, 2 );

		var swatchColor = Value;
		var max = MathF.Max( swatchColor.r, swatchColor.g );
		max = MathF.Max( max, swatchColor.b );
		float intensity = 0;

		if ( max > 1 )
		{
			intensity = max / 1000.0f;

			var div = 1.0f / max;
			swatchColor.r *= div;
			swatchColor.g *= div;
			swatchColor.b *= div;
		}

		var colorRect = rect;
		bool hasTitle = !string.IsNullOrWhiteSpace( Title );

		if ( hasTitle )
		{
			colorRect = colorRect.Shrink( colorRect.Width / 2, 0, 0, 0 );
		}

		paint.SetBrush( ColorPickerTextures.Checkerboard );
		paint.DrawRect( colorRect.Shrink( 2 ), 2 );

		// Qt runs the gradient across the whole rect, the brush runs it across the shape drawn
		var swatchRect = colorRect.Shrink( 1 );
		var left = swatchColor.WithAlpha( 1 );
		var right = Color.Lerp( swatchColor, Color.White.WithAlpha( swatchColor.a ), intensity );
		var from = (swatchRect.Left - rect.Left) / rect.Width;
		var to = (swatchRect.Right - rect.Left) / rect.Width;

		paint.SetBrushLinear( Color.Lerp( left, right, from ), Color.Lerp( left, right, to ) );
		paint.DrawRect( swatchRect, 2 );

		paint.SetDefaultFont();
		paint.SetPen( fg );

		if ( hasTitle )
		{
			paint.DrawText( rect.Shrink( 4, 0, 4, 0 ), Title, TextFlag.LeftCenter );
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

		_popup = new Popup( this, Popup.PositionMode.RightTop, 1 );
		var picker = _popup.AddChild<ColorPickerControl>();
		picker.Property = Property;
	}
}
