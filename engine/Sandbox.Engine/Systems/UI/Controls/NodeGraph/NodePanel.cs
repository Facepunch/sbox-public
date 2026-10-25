using Sandbox.UI;

namespace EngineNodeGraph;

/// <summary>
/// A node in a <see cref="NodeGraphPanel"/>, drawn like the editor's node graph. Lives in the
/// canvas content, so its layout units are canvas units. Drag it to move the selection.
/// </summary>
public partial class NodePanel : CanvasPanel.Handle
{
	public INode Node { get; }

	public NodeGraphPanel Graph { get; }

	public DisplayInfo DisplayInfo => Node.DisplayInfo;

	public Color PrimaryColor = Color.Parse( "#ff99c8" ) ?? default;
	public Color PrimaryTitleColor = Color.Gray;

	public Color ErrorOutline => NodeTheme.Red;

	/// <summary>
	/// Fill of the node body, from <see cref="PrimaryColor"/>.
	/// </summary>
	public Color BodyColor => NodeTheme.Body( PrimaryColor );

	/// <summary>
	/// Fill the title bar with one colour instead of a gradient.
	/// </summary>
	public bool SimpleNodeHeader { get; set; }

	/// <summary>
	/// See-through stand-in, such as a drop preview: ignores the mouse and never edits the node.
	/// </summary>
	public bool IsPreview { get; }

	public bool Selected
	{
		get => _selected;
		set
		{
			if ( _selected == value ) return;
			_selected = value;
			SetClass( "selected", value );
		}
	}

	bool _selected;

	/// <summary>
	/// Outlined and glowing in <see cref="NodeGraphPanel.HighlightColor"/> to point the node out -
	/// such as everything reading what's picked elsewhere. Selection shows over it.
	/// </summary>
	public bool Highlighted { get; set; }

	public List<PlugInPanel> Inputs = new();
	public List<PlugOutPanel> Outputs = new();

	/// <summary>
	/// Wires this node paints this frame, under its body: see <see cref="Connection.PaintOwner"/>.
	/// </summary>
	internal readonly List<Connection> PaintedWires = new();

	/// <summary>
	/// Node size in node units, from <see cref="Layout"/>.
	/// </summary>
	public Vector2 Size { get; protected set; }

	protected virtual float TitleHeight => Node.HasTitleBar ? 24f + (Node.HasSubtitle ? 12f : 0f) : 0f;

	Rect _thumbRect;

	public NodePanel( NodeGraphPanel graph, INode node, bool isPreview = false )
	{
		Graph = graph;
		Node = node;
		IsPreview = isPreview;

		AddClass( "node" );
		DragStarted += () => _dragDelta = default;
		DragMoved += delta => _dragDelta = delta;
		Positioned = false;
		Style.Position = PositionMode.Absolute;
		Style.Overflow = OverflowMode.Visible;

		if ( isPreview )
		{
			AddClass( "preview" );
			DragEnabled = false;
			Style.PointerEvents = PointerEvents.None;
			Style.Opacity = 0.5f;
		}
		else
		{
			OnTooltip = BuildTooltip;
			Style.Cursor = "move";
			Node.Changed += MarkNodeChanged;
		}

		MoveTo( node.Position );
		UpdatePlugs();
	}

	public override void OnDeleted()
	{
		if ( !IsPreview ) Node.Changed -= MarkNodeChanged;
		base.OnDeleted();
	}

	/// <summary>
	/// Rebuild plugs and layout from the node.
	/// </summary>
	public void MarkNodeChanged()
	{
		if ( !IsValid ) return;

		UpdatePlugs();
		MoveTo( Node.Position );
	}

	/// <summary>
	/// Re-read everything from the node, such as after the graph changed under the view.
	/// </summary>
	public void Rebuild()
	{
		if ( !IsValid ) return;

		OnRebuild();
		MarkNodeChanged();
	}

	/// <summary>
	/// Re-read what <see cref="MarkNodeChanged"/> doesn't. Overrides should call the base.
	/// </summary>
	protected virtual void OnRebuild() => MoveTo( Node.Position );

	/// <summary>
	/// Fill the node's tooltip.
	/// </summary>
	protected virtual void BuildTooltip( Panel tip )
	{
		var label = tip.AddChild<Label>();
		label.IsRich = true;
		label.Text = NodeTheme.FormatToolTip( DisplayInfo.Name, DisplayInfo.Description, null, Node.ErrorMessage );
	}

	public override bool HasTooltip => !IsDragging && base.HasTooltip;

	/// <summary>
	/// Whether dragging or focusing the node lifts it over the others. Comments stay behind.
	/// </summary>
	protected virtual bool RaisesOnInteraction => true;

	/// <summary>
	/// Z-index while the node is dragged or holds focus (itself or an editor in it).
	/// </summary>
	protected virtual int InteractionZIndex => 100;

	/// <summary>
	/// Z-index once that ends: 99 lower, so the node stays above the ones it was moved onto.
	/// </summary>
	protected virtual int ReleasedZIndex => InteractionZIndex - 99;

	bool _interacting;

	public override void Tick()
	{
		base.Tick();

		if ( !RaisesOnInteraction ) return;

		var interacting = IsDragging || HasFocus;
		if ( interacting == _interacting ) return;
		_interacting = interacting;

		// Render order is sibling index plus z-index, so the z-index alone loses to later nodes:
		// go last among them too. The parent ticks a copy of its children, so reordering here is safe.
		if ( interacting && Parent is { } parent ) parent.SetChildIndex( this, parent.ChildrenCount - 1 );

		Style.ZIndex = interacting ? InteractionZIndex : ReleasedZIndex;
	}

	/// <summary>
	/// Put the node's top left at <paramref name="position"/>, in canvas units.
	/// </summary>
	public void MoveTo( Vector2 position )
	{
		Position = position;
		Style.Left = position.x;
		Style.Top = position.y;
	}

	/// <summary>
	/// Write the panel position back to the node, snapped to the grid.
	/// </summary>
	public void CommitPosition()
	{
		MoveTo( Position.SnapToGrid( Graph.GridSize ) );

		if ( !IsPreview && !Equals( Node.Position, Position ) )
		{
			Node.Position = Position;
		}
	}
}
