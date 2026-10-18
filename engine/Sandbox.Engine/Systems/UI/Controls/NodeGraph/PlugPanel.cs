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

		OnTooltip = tip =>
		{
			var display = Inner.DisplayInfo;
			var label = tip.AddChild<Label>();
			label.IsRich = true;
			label.Text = NodeTheme.FormatToolTip( display.Name, display.Description, PropertyType );
		};
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

	public float PreferredWidth
	{
		get
		{
			if ( !Visible || !Inner.ShowLabel )
			{
				return 0f;
			}

			var titleWidth = NodePaint.Measure( Title ).x;

			if ( Inner.DisplayInfo.Group is not { } groupName )
			{
				return titleWidth + 24f;
			}

			return NodePaint.Measure( $"{groupName} > ", 7, italic: true ).x + titleWidth + 24f;
		}
	}

	public override void OnDraw( Painter painter )
	{
		var paint = new NodePaint( this, painter ) { HasMouseOver = HasHovered };
		Paint( ref paint );
	}

	protected abstract void Paint( ref NodePaint paint );

	protected void DrawHandle( ref NodePaint paint, Color color, Rect handleRect, HandleShape shape )
	{
		if ( !Visible )
			return;

		if ( !string.IsNullOrEmpty( Inner.ErrorMessage ) )
		{
			color = NodeTheme.Red;
		}

		paint.ClearPen();
		paint.SetBrush( color.WithAlpha( 1.0f ) );

		switch ( shape )
		{
			case HandleShape.Square:
				paint.DrawRect( handleRect, 2.0f );
				break;

			case HandleShape.Circle:
				paint.DrawCircle( handleRect );
				break;

			case HandleShape.Arrow:
				paint.DrawPolygon( handleRect.TopLeft, handleRect.Center with { x = handleRect.Right }, handleRect.BottomLeft );
				break;
		}
	}

	protected void DrawLabel( ref NodePaint paint, Rect rect, bool unreachable, TextFlag flags )
	{
		if ( !Inner.ShowLabel || Inner.InTitleBar || Editor is { Enabled: true, HideLabel: true } )
		{
			return;
		}

		var color = Node.PrimaryColor.Lighten( 0.8f );

		if ( unreachable )
		{
			color = color.WithAlpha( 0.5f );
		}

		paint.SetPen( color );
		paint.SetDefaultFont();

		var titleWidth = paint.MeasureText( Title ).x;
		paint.DrawText( rect.Shrink( 0f, 0f, 0f, 2f ), Title, flags );

		if ( Inner.DisplayInfo.Group is not { } groupName )
		{
			return;
		}

		paint.SetPen( color.Darken( 0.2f ) );
		paint.SetDefaultFont( size: 7, italic: true );

		paint.DrawText( rect.Shrink( 0f, 0f, titleWidth, 0f ), $"{groupName} > ", flags );
	}

	public override string ToString() => Inner.ToString();
}
