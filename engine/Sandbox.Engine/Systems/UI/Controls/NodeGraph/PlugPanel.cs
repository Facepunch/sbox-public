namespace Sandbox.UI.NodeGraph;

/// <summary>
/// One input or output row of a <see cref="NodePanel"/>, placed and sized in node units by the node's layout.
/// </summary>
public abstract partial class PlugPanel : Panel
{
	protected const float handleSize = 14;

	public NodePanel Node { get; }
	public IPlug Inner { get; }
	public NodeValueEditor Editor { get; }

	public string Title => Inner.DisplayInfo.Name;

	Type _overridePropertyType;

	public Type PropertyType
	{
		get => _overridePropertyType ?? Inner.Type;
		set => _overridePropertyType = value;
	}

	public NodeHandleConfig HandleConfig => Node.Graph.GetHandleConfig( PropertyType );

	protected PlugPanel DropTarget => Node.Graph.DropTarget;

	/// <summary>
	/// Where wires attach, in canvas units.
	/// </summary>
	public abstract Vector2 ConnectionPosition { get; }

	public bool Visible { get; set; } = true;

	public abstract bool IsConnected { get; }

	/// <summary>
	/// Top left in the node, in node units.
	/// </summary>
	public Vector2 Position { get; set; }

	/// <summary>
	/// In node units. Rows are 24 tall.
	/// </summary>
	public Vector2 Size { get; set; } = new( 24, 24 );

	protected PlugPanel( NodePanel node, IPlug plug )
	{
		Node = node;
		Inner = plug;

		AddClass( "node-plug" );
		Style.Position = PositionMode.Absolute;
		Style.PointerEvents = PointerEvents.All;
		Style.Overflow = OverflowMode.Visible;
		Style.Cursor = "pointer";

		Editor = plug.CreateEditor( this );
		if ( Editor is not null ) AddChild( Editor );

		// A named method, not a lambda: hotload can't remap lambdas held by a live panel
		OnTooltip = BuildTooltip;
	}

	/// <summary>
	/// Fill the plug's tooltip.
	/// </summary>
	protected virtual void BuildTooltip( Panel tip )
	{
		var display = Inner.DisplayInfo;
		var label = tip.AddChild<Label>();
		label.IsRich = true;
		label.Text = NodeTheme.FormatToolTip( display.Name, display.Description, PropertyType );
	}

	/// <summary>
	/// Apply <see cref="Position"/> and <see cref="Size"/>, then place the editor.
	/// </summary>
	public virtual void Layout()
	{
		Style.Left = Position.x;
		Style.Top = Position.y;
		Style.Width = Size.x;
		Style.Height = Size.y;
	}

	public override void Tick()
	{
		base.Tick();

		if ( Editor is not null )
			Editor.Enabled = !IsConnected;
	}

	/// <summary>
	/// Width the label, and the inline editor if any, need.
	/// </summary>
	public float PreferredWidth
	{
		get
		{
			if ( !Visible )
			{
				return 0f;
			}

			var editorWidth = Editor is null ? 0f : 15f + Editor.PreferredWidth;

			if ( !Inner.ShowLabel )
			{
				return editorWidth;
			}

			var labelWidth = NodePaint.Measure( Title ).x + 24f;

			if ( Inner.DisplayInfo.Group is { } groupName )
			{
				labelWidth += NodePaint.Measure( $"{groupName} > ", 7, italic: true ).x;
			}

			return MathF.Max( labelWidth, editorWidth );
		}
	}

	public override void OnDraw( Painter painter )
	{
		var paint = new NodePaint( this, painter ) { HasMouseOver = HasHovered };
		Paint( ref paint );
	}

	protected abstract void Paint( ref NodePaint paint );

	/// <summary>
	/// A ring that fills in when <paramref name="filled"/>. A thin dark rim of the same shape cuts the
	/// node's edge around it, and hides wires passing underneath.
	/// </summary>
	protected void DrawHandle( ref NodePaint paint, Color color, Rect handleRect, HandleShape shape, bool filled )
	{
		if ( !Visible )
			return;

		if ( !string.IsNullOrEmpty( Inner.ErrorMessage ) )
		{
			color = NodeTheme.Red;
			filled = true;
		}

		color = color.WithAlpha( 1.0f );

		// Filled, not stroked, so nothing underneath bleeds through between the rim and the handle
		paint.ClearPen();
		paint.SetBrush( NodeTheme.Canvas );
		DrawShape( ref paint, handleRect, 2f, shape );

		paint.SetPen( color, 1.5f );
		paint.SetBrush( filled ? color : NodeTheme.Canvas );
		DrawShape( ref paint, handleRect, -0.75f, shape );
	}

	/// <summary>
	/// <paramref name="handleRect"/> grown by <paramref name="grow"/>. Square corners grow with it, so
	/// every offset of the shape stays concentric.
	/// </summary>
	static void DrawShape( ref NodePaint paint, Rect handleRect, float grow, HandleShape shape )
	{
		var rect = handleRect.Grow( grow );

		switch ( shape )
		{
			case HandleShape.Square:
				var radius = MathF.Min( handleRect.Width, handleRect.Height ) * 0.3f + grow;
				paint.DrawRect( rect, MathF.Max( radius, 0f ) );
				break;

			case HandleShape.Circle:
				paint.DrawCircle( rect );
				break;

			case HandleShape.Arrow:
				paint.DrawPolygon( rect.TopLeft, rect.Center with { x = rect.Right }, rect.BottomLeft );
				break;
		}
	}

	protected void DrawLabel( ref NodePaint paint, Rect rect, bool unreachable, TextFlag flags )
	{
		if ( !Inner.ShowLabel || Inner.InTitleBar || Node.IsThumbnailCard || Editor is { Enabled: true, HideLabel: true } )
		{
			return;
		}

		var color = Color.Lerp( NodeTheme.Text, Node.PrimaryColor, 0.15f );

		if ( unreachable )
		{
			color = color.WithAlpha( 0.45f );
		}

		paint.SetPen( color );
		paint.SetDefaultFont();

		var titleRect = rect.Shrink( 0f, 0f, 0f, 2f );

		if ( Inner.DisplayInfo.Group is not { } groupName )
		{
			paint.DrawText( titleRect, Title, flags );
			return;
		}

		var titleWidth = paint.MeasureText( Title ).x;
		var group = $"{groupName} > ";

		paint.SetDefaultFont( size: 7 );
		var groupWidth = paint.MeasureText( group ).x;

		// The prefix sits before the title on either side
		var leftAligned = (flags & TextFlag.Left) != 0;
		var groupRect = leftAligned ? rect : rect.Shrink( 0f, 0f, titleWidth, 0f );

		paint.SetPen( NodeTheme.TextMuted.WithAlpha( color.a ) );
		paint.DrawText( groupRect, group, flags );

		paint.SetPen( color );
		paint.SetDefaultFont();
		paint.DrawText( leftAligned ? titleRect.Shrink( groupWidth, 0f, 0f, 0f ) : titleRect, Title, flags );
	}

	public override string ToString() => Inner.ToString();
}
