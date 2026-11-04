using System.Globalization;

namespace Sandbox.UI;

/// <summary>
/// A horizontal or vertical graph axis with numeric or custom ticks, an optional title,
/// rotated tick labels and optional editable endpoints. It does not own the graph's view or data.
/// </summary>
[Library( "graphaxis" )]
[StyleSheet.Inline( "graphaxis", ".graphaxis { position: absolute; overflow: visible; pointer-events: none; } .graphaxis > .graph-axis-bound { position: absolute; min-width: 0; height: 24px; pointer-events: all; } .graphaxis > .graph-axis-bound .content-label { width: 100%; }" )]
public class GraphAxis : Panel
{
	/// <summary>
	/// A tick in axis units. Custom labels support categories, dates and other non-numeric scales.
	/// </summary>
	public readonly record struct TickMark( double Value, string Label, bool Major = true );

	/// <summary>
	/// Direction along which values increase, before applying Reversed.
	/// </summary>
	public GraphPanel.Axis Orientation
	{
		get => _orientation;
		set
		{
			_orientation = value;
			UpdateBoundClasses();
		}
	}
	GraphPanel.Axis _orientation;

	/// <summary>
	/// Reverse the direction of increasing values, for example for an upward vertical axis.
	/// </summary>
	public bool Reversed { get; set; }

	/// <summary>
	/// Visible range in axis units.
	/// </summary>
	public double Minimum { get; set; }
	public double Maximum { get; set; } = 1;

	/// <summary>
	/// Optional axis title, drawn below a horizontal axis or rotated beside a vertical axis.
	/// </summary>
	public string Label { get; set; }

	/// <summary>
	/// Fixed numeric format. For example "0.00" or "$#,0". Never changes with zoom.
	/// </summary>
	public string NumberFormat { get; set; } = "0.0";

	/// <summary>
	/// Optional formatter for generated tick labels. Editable endpoints still use NumberFormat.
	/// </summary>
	public Func<double, string> LabelFormatter { get; set; }

	/// <summary>
	/// Explicit tick positions and labels. Null generates numeric ticks automatically.
	/// </summary>
	public IReadOnlyList<TickMark> Ticks { get; set; }

	/// <summary>
	/// Optional fixed major tick interval in axis units. Null chooses spacing from the visible range.
	/// </summary>
	public double? TickInterval { get; set; }

	/// <summary>
	/// Clockwise rotation of tick labels in degrees. Negative angles suit month names on a horizontal axis.
	/// </summary>
	public float LabelRotation { get; set; }

	/// <summary>
	/// Space reserved for each tick label, in logical pixels. Also determines the axis gutter size.
	/// </summary>
	public float LabelWidth { get; set; } = 48;

	/// <summary>
	/// Tick-label font size in logical pixels.
	/// </summary>
	public float FontSize { get; set; } = 10;

	/// <summary>
	/// Length of major tick marks in logical pixels. Set to zero for grid-only graphs.
	/// </summary>
	public float TickLength { get; set; } = 4;

	/// <summary>
	/// Draw a baseline along the edge of the plot.
	/// </summary>
	public bool ShowLine { get; set; } = true;

	/// <summary>
	/// Show editable endpoints instead of tick labels at the ends of the axis.
	/// </summary>
	public bool EditableBounds { get; set; }

	/// <summary>
	/// Override the values shown in endpoint fields, independently of the visible range.
	/// </summary>
	public float? MinimumBound { get; set; }
	public float? MaximumBound { get; set; }

	/// <summary>
	/// Requested endpoint edit. True selects the maximum. The owner decides how to apply it.
	/// </summary>
	public event Action<bool, float> BoundEdited;
	public event Action EditStarted;
	public event Action EditFinished;

	/// <summary>
	/// Required gutter size in logical pixels, including rotated labels and the optional title.
	/// </summary>
	public float Thickness
	{
		get
		{
			float angle = LabelRotation * MathF.PI / 180;
			float extent = Horizontal
				? MathF.Abs( MathF.Sin( angle ) ) * LabelWidth + MathF.Abs( MathF.Cos( angle ) ) * (FontSize + 6)
				: MathF.Abs( MathF.Cos( angle ) ) * LabelWidth + MathF.Abs( MathF.Sin( angle ) ) * (FontSize + 6);
			return Math.Max( Horizontal ? 40 : 60, extent + 12 ) + (string.IsNullOrEmpty( Label ) ? 0 : 24);
		}
	}

	bool Horizontal => Orientation == GraphPanel.Axis.Horizontal;
	readonly NumberEntry _minimum, _maximum;
	internal Color? ColorOverride { get; set; }
	Color AxisColor => ColorOverride ?? ComputedStyle?.FontColor ?? Color.White;

	public GraphAxis()
	{
		AddClass( "graphaxis" );
		_minimum = AddBound( false );
		_maximum = AddBound( true );
		UpdateBoundClasses();
	}

	NumberEntry AddBound( bool maximum )
	{
		var entry = AddChild<NumberEntry>( "graph-axis-bound" );
		entry.Tooltip = maximum ? "Axis maximum" : "Axis minimum";
		entry.OnTextEdited = text =>
		{
			if ( float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value ) )
				BoundEdited?.Invoke( maximum, value );
		};
		entry.AddEventListener( "onfocus", () =>
		{
			entry.Text = BoundValue( maximum ).ToString( "G9", CultureInfo.InvariantCulture );
			EditStarted?.Invoke();
		} );
		entry.AddEventListener( "onblur", () =>
		{
			EditFinished?.Invoke();
			SyncBounds();
		} );
		return entry;
	}

	float BoundValue( bool maximum ) => maximum ? MaximumBound ?? (float)Maximum : MinimumBound ?? (float)Minimum;

	void UpdateBoundClasses()
	{
		if ( _minimum is null || _maximum is null )
			return;
		_minimum.SetClass( "axis-x-min", Horizontal );
		_minimum.SetClass( "axis-y-min", !Horizontal );
		_maximum.SetClass( "axis-x-max", Horizontal );
		_maximum.SetClass( "axis-y-max", !Horizontal );
	}

	void SyncBounds()
	{
		foreach ( var entry in new[] { _minimum, _maximum } )
		{
			bool maximum = entry == _maximum;
			entry.Style.Display = EditableBounds ? DisplayMode.Flex : DisplayMode.None;
			float width = Math.Max( 54, LabelWidth );
			float fraction = maximum != Reversed ? 1 : 0;
			entry.Style.Width = width;
			entry.Style.Left = Horizontal ? fraction * (Box.Rect.Width / ScaleToScreen - width) : Box.Rect.Width / ScaleToScreen - width - 6;
			entry.Style.Top = Horizontal ? 4 : fraction * Box.Rect.Height / ScaleToScreen - 12;
			entry.Style.TextAlign = Horizontal && fraction == 0 ? TextAlign.Left : TextAlign.Right;
			if ( !entry.HasFocus )
				entry.Text = FormatLabel( BoundValue( maximum ), NumberFormat );
		}
	}

	/// <summary>
	/// Map an axis value to its fraction along the axis, including reversal.
	/// </summary>
	public float Fraction( double value )
	{
		float fraction = (float)((value - Minimum) / (Maximum - Minimum));
		return Reversed ? 1 - fraction : fraction;
	}

	/// <summary>
	/// Generate the ticks shared by the axis labels and the graph's grid. Length is in logical pixels.
	/// </summary>
	public IEnumerable<TickMark> GetTicks( float length )
	{
		if ( !double.IsFinite( Minimum ) || !double.IsFinite( Maximum ) || Maximum <= Minimum )
			yield break;
		if ( Ticks is not null )
		{
			foreach ( var tick in Ticks )
				if ( tick.Value >= Minimum && tick.Value <= Maximum )
					yield return tick;
			yield break;
		}
		double step = TickInterval ?? TickStep( Maximum - Minimum, length, Horizontal ? Math.Max( 90, LabelWidth + 12 ) : Math.Max( 55, FontSize + 12 ) );
		if ( !double.IsFinite( step ) || step <= 0 )
			yield break;
		double minor = step / 5;
		double first = Math.Ceiling( Minimum / minor ) * minor;
		for ( int i = 0; i < 500; i++ )
		{
			double value = first + i * minor;
			if ( value > Maximum )
				break;
			bool major = Math.Abs( value / step - Math.Round( value / step ) ) < 0.0001;
			yield return new( value, major ? LabelFormatter?.Invoke( value ) ?? FormatLabel( value, NumberFormat ) : null, major );
		}
	}

	internal static double TickStep( double span, double pixels, double spacing )
	{
		double rough = span / Math.Max( 2, pixels / spacing );
		if ( !double.IsFinite( rough ) || rough <= 0 )
			return 1;
		double power = Math.Pow( 10, Math.Floor( Math.Log10( rough ) ) );
		double mantissa = rough / power;
		return (mantissa <= 1 ? 1 : mantissa <= 2 ? 2 : mantissa <= 2.5 ? 2.5 : mantissa <= 5 ? 5 : 10) * power;
	}

	internal static string FormatLabel( double value, string format )
	{
		var text = value.ToString( format, CultureInfo.InvariantCulture );
		return double.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rounded ) && rounded == 0
			? 0d.ToString( format, CultureInfo.InvariantCulture ) : text;
	}

	public override void Tick()
	{
		base.Tick();
		SyncBounds();
	}

	public override void OnDraw( Painter painter )
	{
		using var scope = painter.Scope();
		var size = Box.Rect.Size;
		float length = Horizontal ? size.x : size.y;
		painter.Stroke = Stroke.Solid( AxisColor.WithAlpha( 0.35f ), 1 );
		if ( ShowLine )
			painter.Line( Horizontal ? Vector2.Zero : new( size.x, 0 ), Horizontal ? new( size.x, 0 ) : size );
		foreach ( var tick in GetTicks( length / ScaleToScreen ) )
		{
			if ( !tick.Major )
				continue;
			float position = Fraction( tick.Value ) * length;
			var origin = Horizontal ? new Vector2( position, 0 ) : new Vector2( size.x, position );
			var end = origin + (Horizontal ? new Vector2( 0, TickLength ) : new Vector2( -TickLength, 0 )) * ScaleToScreen;
			if ( TickLength > 0 )
				painter.Line( origin, end );
			float margin = EditableBounds ? (Horizontal ? Math.Max( 80, LabelWidth ) : 16) * ScaleToScreen : 0;
			if ( EditableBounds && (position < margin || position > length - margin) )
				continue;
			using var labelScope = painter.Scope();
			painter.Translate( end + (Horizontal ? new Vector2( 0, 5 ) : new Vector2( -6, 0 )) * ScaleToScreen );
			painter.Rotate( LabelRotation );
			bool centered = Horizontal && LabelRotation == 0;
			bool left = Horizontal && LabelRotation > 0;
			painter.TextStyle = new TextStyle { FontSize = FontSize, Color = AxisColor.WithAlpha( 0.65f ), Alignment = centered ? TextFlag.Center : left ? TextFlag.LeftCenter : TextFlag.RightCenter };
			float width = LabelWidth * ScaleToScreen;
			painter.Text( tick.Label, new Rect( centered ? -width / 2 : left ? 0 : -width, Horizontal && LabelRotation == 0 ? 0 : -8 * ScaleToScreen, width, 16 * ScaleToScreen ) );
		}
		if ( string.IsNullOrEmpty( Label ) )
			return;
		painter.TextStyle = new TextStyle { FontSize = FontSize + 2, Color = AxisColor, Alignment = TextFlag.Center };
		if ( Horizontal )
		{
			painter.Text( Label, new Rect( 0, size.y - 22 * ScaleToScreen, size.x, 20 * ScaleToScreen ) );
		}
		else
		{
			painter.Translate( new Vector2( 10 * ScaleToScreen, size.y / 2 ) );
			painter.Rotate( -90 );
			painter.Text( Label, new Rect( -size.y / 2, -10 * ScaleToScreen, size.y, 20 * ScaleToScreen ) );
		}
	}
}
