namespace Sandbox.UI;

/// <summary>
/// A navigable plot with two axis controls and a grid. Subclasses draw their data after base.OnDraw.
/// Axis fields report edits to the owner; they never change data or the view.
/// </summary>
[Library( "graphpanel" )]
[StyleSheet.Inline( "graphpanel", ".graphpanel { min-width: 0; min-height: 0; }" )]
public class GraphPanel : CanvasPanel
{
	public enum Axis
	{
		Horizontal,
		Vertical
	}

	/// <summary>
	/// Axis controls own ticks, labels, titles and endpoint editing. GraphPanel supplies their visible ranges.
	/// </summary>
	public GraphAxis HorizontalAxis { get; }
	public GraphAxis VerticalAxis { get; }

	/// <summary>
	/// Fixed numeric format for horizontal ticks and endpoints.
	/// </summary>
	public string HorizontalAxisFormat { get => HorizontalAxis.NumberFormat; set => HorizontalAxis.NumberFormat = value; }

	/// <summary>
	/// Fixed numeric format for vertical ticks and endpoints.
	/// </summary>
	public string VerticalAxisFormat { get => VerticalAxis.NumberFormat; set => VerticalAxis.NumberFormat = value; }

	/// <summary>
	/// Show editable endpoints in both axis gutters.
	/// </summary>
	public bool EditableAxes
	{
		get => HorizontalAxis.EditableBounds && VerticalAxis.EditableBounds;
		set => HorizontalAxis.EditableBounds = VerticalAxis.EditableBounds = value;
	}

	/// <summary>
	/// Draw grid lines at the ticks supplied by the axes.
	/// </summary>
	public bool ShowGrid { get; set; } = true;

	/// <summary>
	/// Draw a frame around all four sides of the plot.
	/// </summary>
	public bool ShowFrame { get; set; } = true;

	/// <summary>
	/// Emphasize the zero lines inside the plot.
	/// </summary>
	public bool ShowZeroLines { get; set; } = true;

	/// <summary>
	/// Reports an axis, whether its maximum is edited, and the requested value in axis units.
	/// </summary>
	public event Action<Axis, bool, float> AxisBoundEdited;
	public event Action AxisEditStarted;
	public event Action AxisEditFinished;

	/// <summary>
	/// Plot bounds in local screen pixels. Each axis supplies the gutter space it needs.
	/// </summary>
	protected virtual Rect Plot => new( VerticalAxis.Thickness * ScaleToScreen, 16 * ScaleToScreen,
		Math.Max( 1, Box.Rect.Width - (VerticalAxis.Thickness + 18) * ScaleToScreen ),
		Math.Max( 1, Box.Rect.Height - (HorizontalAxis.Thickness + 16) * ScaleToScreen ) );
	protected override Rect Viewport => Plot;
	protected virtual Color AxisColor => ComputedStyle?.FontColor ?? Color.White;

	/// <summary>
	/// Values shown in editable endpoints. Defaults to the visible bounds.
	/// </summary>
	protected virtual Vector2 AxisMinimum => CanvasToAxis( VisibleMinimum );
	protected virtual Vector2 AxisMaximum => CanvasToAxis( VisibleMaximum );
	Vector2 VisibleMinimum => Vector2.Min( ScreenToCanvas( Plot.Position ), ScreenToCanvas( Plot.Position + Plot.Size ) );
	Vector2 VisibleMaximum => Vector2.Max( ScreenToCanvas( Plot.Position ), ScreenToCanvas( Plot.Position + Plot.Size ) );

	/// <summary>
	/// Map canvas coordinates to axis units. Override with AxisToCanvas for a linear unit conversion.
	/// </summary>
	public virtual Vector2 CanvasToAxis( Vector2 point ) => point;
	public virtual Vector2 AxisToCanvas( Vector2 point ) => point;

	public GraphPanel()
	{
		AddClass( "graphpanel" );
		HorizontalAxis = AddAxis( Axis.Horizontal );
		VerticalAxis = AddAxis( Axis.Vertical );
		YAxisUp = true;
		PreserveAspectRatio = false;
	}

	GraphAxis AddAxis( Axis direction )
	{
		var axis = AddChild<GraphAxis>();
		axis.Orientation = direction;
		axis.ShowLine = false;
		axis.TickLength = 0;
		axis.BoundEdited += ( maximum, value ) => AxisBoundEdited?.Invoke( direction, maximum, value );
		axis.EditStarted += () => AxisEditStarted?.Invoke();
		axis.EditFinished += () => AxisEditFinished?.Invoke();
		return axis;
	}

	public override void Tick()
	{
		base.Tick();
		SetClass( "y-axis-down", !YAxisUp );
		var plot = Plot;
		var min = CanvasToAxis( VisibleMinimum );
		var max = CanvasToAxis( VisibleMaximum );
		HorizontalAxis.Minimum = min.x;
		HorizontalAxis.Maximum = max.x;
		VerticalAxis.Minimum = min.y;
		VerticalAxis.Maximum = max.y;
		VerticalAxis.Reversed = YAxisUp;
		HorizontalAxis.MinimumBound = AxisMinimum.x;
		HorizontalAxis.MaximumBound = AxisMaximum.x;
		VerticalAxis.MinimumBound = AxisMinimum.y;
		VerticalAxis.MaximumBound = AxisMaximum.y;
		HorizontalAxis.ColorOverride = VerticalAxis.ColorOverride = AxisColor;
		HorizontalAxis.Style.Left = plot.Left / ScaleToScreen;
		HorizontalAxis.Style.Top = plot.Bottom / ScaleToScreen;
		HorizontalAxis.Style.Width = plot.Width / ScaleToScreen;
		HorizontalAxis.Style.Height = HorizontalAxis.Thickness;
		VerticalAxis.Style.Left = 0;
		VerticalAxis.Style.Top = plot.Top / ScaleToScreen;
		VerticalAxis.Style.Width = plot.Left / ScaleToScreen;
		VerticalAxis.Style.Height = plot.Height / ScaleToScreen;
	}

	public override void OnDraw( Painter painter )
	{
		base.OnDraw( painter );
		using var scope = painter.Scope();
		var plot = Plot;
		var color = AxisColor;
		painter.Clip( plot );
		if ( ShowGrid )
		{
			DrawGrid( painter, HorizontalAxis, plot, color );
			DrawGrid( painter, VerticalAxis, plot, color );
		}
		painter.Stroke = Stroke.Solid( color.WithAlpha( 0.18f ), 1 );
		painter.Fill = Color.Transparent;
		if ( ShowFrame )
			painter.Rect( plot );
		if ( ShowZeroLines )
		{
			var origin = AxisToCanvas( Vector2.Zero );
			painter.Line( CanvasToScreen( new( origin.x, VisibleMinimum.y ) ), CanvasToScreen( new( origin.x, VisibleMaximum.y ) ) );
			painter.Line( CanvasToScreen( new( VisibleMinimum.x, origin.y ) ), CanvasToScreen( new( VisibleMaximum.x, origin.y ) ) );
		}
	}

	void DrawGrid( Painter painter, GraphAxis axis, Rect plot, Color color )
	{
		bool horizontal = axis.Orientation == Axis.Horizontal;
		foreach ( var tick in axis.GetTicks( (horizontal ? plot.Width : plot.Height) / ScaleToScreen ) )
		{
			painter.Stroke = Stroke.None;
			painter.Fill = color.WithAlpha( tick.Major ? 0.10f : 0.035f );
			float fraction = axis.Fraction( tick.Value );
			if ( horizontal )
			{
				float x = plot.Left + plot.Width * fraction;
				painter.Rect( new Rect( x - 0.5f, plot.Top, 1, plot.Height ) );
			}
			else
			{
				float y = plot.Top + plot.Height * fraction;
				painter.Rect( new Rect( plot.Left, y - 0.5f, plot.Width, 1 ) );
			}
		}
	}
}
