using System.Text.RegularExpressions;

namespace Sandbox.UI.NodeGraph;

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

	public List<PlugInPanel> Inputs = new();
	public List<PlugOutPanel> Outputs = new();

	/// <summary>
	/// Node size in node units, from <see cref="Layout"/>.
	/// </summary>
	public Vector2 Size { get; private set; }

	protected virtual float TitleHeight => Node.HasTitleBar ? 24f + (Node.HasSubtitle ? 24f : 0f) : 0f;

	Rect _thumbRect;

	public NodePanel( NodeGraphPanel graph, INode node, bool isPreview = false )
	{
		Graph = graph;
		Node = node;
		IsPreview = isPreview;

		AddClass( "node" );
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

	void BuildTooltip( Panel tip )
	{
		var label = tip.AddChild<Label>();
		label.IsRich = true;
		label.Text = NodeTheme.FormatToolTip( DisplayInfo.Name, DisplayInfo.Description, null, Node.ErrorMessage );
	}

	public override bool HasTooltip => !IsDragging && base.HasTooltip;

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

	Dictionary<NodePanel, Vector2> _dragStart;

	protected override bool OnDragStart( MousePanelEvent e )
	{
		Graph.PressNode( this, e.HasCtrl, e.HasShift );

		_dragStart = Graph.SelectedNodes.ToDictionary( x => x, x => x.Position );
		return Selected;
	}

	protected override void OnDragMove( Vector2 delta )
	{
		foreach ( var (node, start) in _dragStart )
		{
			if ( node.IsValid() ) node.MoveTo( (start + delta).SnapToGrid( Graph.GridSize ) );
		}
	}

	protected override void OnDragFinish( bool cancelled )
	{
		foreach ( var (node, start) in _dragStart )
		{
			if ( !node.IsValid() ) continue;
			if ( cancelled ) node.MoveTo( start );
			node.CommitPosition();
		}

		_dragStart = null;
	}

	void UpdatePlugs()
	{
		for ( var i = Inputs.Count - 1; i >= 0; --i )
		{
			var plugIn = Inputs[i];
			if ( !Node.Inputs.Contains( plugIn.Inner ) )
			{
				Inputs.RemoveAt( i );
				plugIn.Delete( true );
			}
		}

		for ( var i = Outputs.Count - 1; i >= 0; --i )
		{
			var plugOut = Outputs[i];
			if ( !Node.Outputs.Contains( plugOut.Inner ) )
			{
				Outputs.RemoveAt( i );
				plugOut.Delete( true );
			}
		}

		var index = 0;
		foreach ( var plug in Node.Inputs )
		{
			var match = Inputs.FirstOrDefault( x => x.Inner == plug );

			if ( match is null )
			{
				Inputs.Insert( Math.Min( index, Inputs.Count ), AddChild( new PlugInPanel( this, plug ) ) );
			}
			else if ( Inputs.IndexOf( match ) != index )
			{
				Inputs.Remove( match );
				Inputs.Insert( Math.Min( index, Inputs.Count ), match );
			}

			++index;
		}

		index = 0;
		foreach ( var plug in Node.Outputs )
		{
			var match = Outputs.FirstOrDefault( x => x.Inner == plug );

			if ( match is null )
			{
				Outputs.Insert( Math.Min( index, Outputs.Count ), AddChild( new PlugOutPanel( this, plug ) ) );
			}
			else if ( Outputs.IndexOf( match ) != index )
			{
				Outputs.Remove( match );
				Outputs.Insert( Math.Min( index, Outputs.Count ), match );
			}

			++index;
		}

		Layout();
	}

	/// <summary>
	/// A node without a title bar but with a thumbnail is drawn as the image itself, plugs on its edges.
	/// </summary>
	public bool IsThumbnailCard => !Node.HasTitleBar && Node.Thumbnail is not null;

	const float HeaderPadLeft = 9f;
	const float HeaderPadRight = 14f;

	/// <summary>
	/// The icon column: wider when a subtitle makes the header two lines tall and the icon bigger.
	/// </summary>
	static float HeaderIconWidth( bool twoLines ) => twoLines ? 27f : 20f;

	/// <summary>
	/// Width the header needs for its icon, title and subtitle, in the fonts they're drawn with.
	/// </summary>
	float HeaderWidth()
	{
		var text = NodePaint.Measure( DisplayInfo.Name, 8f, 600 ).x;

		if ( Node.HasSubtitle )
		{
			text = MathF.Max( text, NodePaint.Measure( Node.Subtitle, 7f ).x );
		}

		return HeaderPadLeft + (DisplayInfo.Icon is null ? 0f : HeaderIconWidth( Node.HasSubtitle )) + text + HeaderPadRight;
	}

	protected virtual void Layout()
	{
		var hasThumb = !Node.HasTitleBar && Node.DisplayInfo.Icon is not null || Node.Thumbnail is not null;
		var inputHeight = Inputs.Sum( x => x.Inner.InTitleBar ? 0f : 24f );
		var outputHeight = Outputs.Sum( x => x.Inner.InTitleBar ? 0f : 24f );
		var thumbnailSize = IsThumbnailCard ? 96f : hasThumb ? Node.Thumbnail is not null ? 88f : 24f : 0f;
		var bodyHeight = MathF.Max( MathF.Max( inputHeight, outputHeight ), thumbnailSize );

		var totalWidth = 160f;
		var inputWidth = 80f;
		var outputWidth = 80f;

		if ( Node.AutoSize )
		{
			var titleWidth = Node.HasTitleBar ? HeaderWidth() : 0f;

			if ( Inputs.Any( x => x.Inner.InTitleBar ) )
			{
				titleWidth += 24f;
			}

			if ( Outputs.Any( x => x.Inner.InTitleBar ) )
			{
				titleWidth += 24f;
			}

			inputWidth = Inputs
				.Select( x => x.PreferredWidth )
				.DefaultIfEmpty( 0f )
				.Max();
			outputWidth = Outputs
				.Select( x => x.PreferredWidth )
				.DefaultIfEmpty( 0f )
				.Max();

			totalWidth = Math.Max( 24f, Math.Max( titleWidth, inputWidth + outputWidth + thumbnailSize + (Node.HasTitleBar ? 8f : 0f) ) );
		}

		// The card's plugs are unlabelled, so the image takes the whole width
		if ( IsThumbnailCard )
		{
			inputWidth = 0f;
			outputWidth = 0f;
			totalWidth = thumbnailSize;
		}

		var verticalCenter = !Node.HasTitleBar;

		totalWidth += Node.ExpandSize.x;
		bodyHeight += Node.ExpandSize.y;

		if ( verticalCenter )
		{
			bodyHeight = MathF.Ceiling( bodyHeight / Graph.GridSize ) * Graph.GridSize;
		}

		var totalHeight = TitleHeight + bodyHeight;

		if ( !Node.HasTitleBar )
		{
			var size = Math.Max( totalWidth, totalHeight );

			(totalWidth, totalHeight) = (Math.Max( 36f, size ), size);
		}

		// Up, not to nearest, so the header keeps its right padding
		totalWidth = MathF.Ceiling( totalWidth / Graph.GridSize ) * Graph.GridSize;
		totalHeight = totalHeight.SnapToGrid( Graph.GridSize );

		var top = TitleHeight + (verticalCenter ? totalHeight - inputHeight : 0f) * 0.5f;
		var index = 0;

		top = top.SnapToGrid( Graph.GridSize );

		var handleOffset = 6f;

		foreach ( var input in Inputs )
		{
			if ( input.Inner.InTitleBar )
			{
				input.Position = new Vector2( -handleOffset, 0f );
				input.Size = input.Size.WithX( 24f );
			}
			else
			{
				var plugWidth = inputWidth;

				if ( index >= Outputs.Count && input.Inner.AllowStretch )
				{
					plugWidth = totalWidth;
				}

				plugWidth = Math.Max( 24f, plugWidth );

				input.Position = new Vector2( -handleOffset, top );
				input.Size = input.Size.WithX( plugWidth );

				top += 24f;
				++index;
			}

			input.Layout();
		}

		top = TitleHeight + (verticalCenter ? totalHeight - outputHeight : 0f) * 0.5f;
		top = top.SnapToGrid( Graph.GridSize );

		index = 0;

		foreach ( var output in Outputs )
		{
			if ( output.Inner.InTitleBar )
			{
				output.Position = new Vector2( totalWidth - 24f + handleOffset, 0f );
				output.Size = output.Size.WithX( 24f );
			}
			else
			{
				var plugWidth = Math.Max( 24f, outputWidth );

				if ( index >= Inputs.Count && output.Inner.AllowStretch )
				{
					plugWidth = totalWidth;
				}

				output.Position = new Vector2( totalWidth - plugWidth + handleOffset, top );
				output.Size = output.Size.WithX( plugWidth );

				top += 24f;
				++index;
			}

			output.Layout();
		}

		Size = new Vector2( totalWidth, totalHeight );
		Style.Width = totalWidth;
		Style.Height = totalHeight;

		_thumbRect = IsThumbnailCard
			? new Rect( 0f, Size ).Shrink( 4f )
			: new Rect( inputWidth, TitleHeight > 0f ? TitleHeight - 2f : 0f, Size.x - inputWidth - outputWidth, Size.y - TitleHeight );

		if ( hasThumb && Node.HasTitleBar )
		{
			_thumbRect = _thumbRect.Shrink( 6f, 5f, 6f, 4f );

			if ( inputWidth <= 0f != outputWidth <= 0f )
			{
				_thumbRect = _thumbRect.Align( thumbnailSize - 16f, inputWidth <= 0f ? TextFlag.LeftCenter : TextFlag.RightCenter );
			}
		}
	}

	static Regex WordWrapPointRegex { get; } = new Regex( "[^A-Z][A-Z]|_[^_]" );

	/// <summary>
	/// Let <paramref name="value"/> wrap after underscores and between PascalCase words.
	/// </summary>
	static string FixWordWrapping( string value )
	{
		return WordWrapPointRegex.Replace( value, x => $"{x.Value[0]}\x200B{x.Value[1]}" );
	}

	public override void OnDraw( Painter painter )
	{
		var paint = new NodePaint( this, painter ) { HasMouseOver = HasHovered };

		var rect = new Rect( 0f, Size );
		var radius = NodeTheme.Radius;

		PrimaryColor = Node.PrimaryColor;
		PrimaryTitleColor = Node.TitleColor;

		var hasError = !string.IsNullOrWhiteSpace( Node.ErrorMessage );
		var body = BodyColor;

		// Depth, then the selection glow
		paint.DrawShadow( rect, radius, Color.Black.WithAlpha( 0.5f ), 18f, new Vector2( 0f, 6f ) );

		if ( Selected )
		{
			paint.DrawShadow( rect, radius, NodeTheme.Accent.WithAlpha( 0.45f ), 10f, default, 1f );
		}
		else if ( hasError )
		{
			paint.DrawShadow( rect, radius, ErrorOutline.WithAlpha( 0.3f ), 10f );
		}

		paint.ClearPen();
		paint.SetBrush( body );
		paint.DrawRect( rect, radius );

		var display = DisplayInfo;

		if ( Node.HasTitleBar )
		{
			DrawHeader( ref paint, rect, display );

			if ( Node.Thumbnail is { } thumb )
			{
				paint.ClearPen();
				paint.DrawImage( _thumbRect.Align( new Vector2( 72f, 72f ), TextFlag.Center ), thumb, 4f );
			}
		}
		else if ( Node.Thumbnail is { } thumb )
		{
			// The image is the card, with the icon in a corner and the name over the bottom, e.g. for resource references
			var thumbRect = _thumbRect;

			paint.SetPen( NodeTheme.Hairline );
			paint.DrawImage( thumbRect, thumb, NodeTheme.Radius - 4f );

			if ( display.Icon is { } icon )
			{
				var iconRect = thumbRect.Shrink( 4f ).Align( new Vector2( 12f, 12f ), TextFlag.LeftTop );

				paint.ClearPen();
				paint.SetBrush( body.WithAlpha( 0.85f ) );
				paint.DrawRect( iconRect.Grow( 3f ), 4f );

				paint.SetPen( PrimaryTitleColor.Lighten( 0.3f ) );
				paint.DrawIcon( iconRect, icon, 12f );
			}

			if ( display.Name is { } name )
			{
				name = FixWordWrapping( name );

				paint.SetDefaultFont( 7f, 600 );

				var backgroundRect = thumbRect.Shrink( 4f ).Align( NodePaint.Measure( name, 7f, 600 ), TextFlag.CenterBottom ).Grow( 6f, 2f, 6f, 2f );

				paint.ClearPen();
				paint.SetBrush( body.WithAlpha( 0.85f ) );
				paint.DrawRect( backgroundRect, 4f );

				paint.SetPen( NodeTheme.Text );
				paint.DrawText( thumbRect.Shrink( 4f ), name, TextFlag.CenterBottom | TextFlag.WordWrap );
			}
		}
		else if ( display.Icon is { } icon )
		{
			// An icon without text, e.g. for operators
			var scale = icon.Length == 2 && !char.IsLetterOrDigit( icon[0] ) ? 0.5f : icon == "|" ? 0.75f : 1f;

			// Plain + and - sit on the text's math axis, below centre; the icon glyphs are centred
			icon = icon switch { "+" => "add", "-" => "remove", _ => icon };

			paint.SetPen( NodeTheme.Text.WithAlpha( 0.85f ) );
			paint.DrawIcon( _thumbRect, icon, (Math.Min( _thumbRect.Width, _thumbRect.Height ) - 8f) * scale );
		}

		Node.OnPaint( paint, rect );

		paint.ClearBrush();

		if ( Selected )
		{
			paint.SetPen( NodeTheme.Accent, 1.5f );
		}
		else if ( hasError )
		{
			paint.SetPen( paint.HasMouseOver ? ErrorOutline : ErrorOutline.Darken( 0.2f ), 1.5f );
		}
		else
		{
			paint.SetPen( paint.HasMouseOver ? NodeTheme.HairlineHover : NodeTheme.Hairline, 1f );
		}

		paint.DrawRect( rect, radius );

		if ( hasError )
		{
			DrawErrorBadge( ref paint, rect );
		}
	}

	/// <summary>
	/// A header in a darkened title colour, with an accent line, icon, title and subtitle.
	/// </summary>
	void DrawHeader( ref NodePaint paint, Rect rect, DisplayInfo display )
	{
		var radius = NodeTheme.Radius;
		var titleRect = new Rect( rect.Position, new Vector2( rect.Width, TitleHeight ) );
		var hasBody = Node.Thumbnail is not null || Inputs.Any( x => !x.Inner.InTitleBar ) || Outputs.Any( x => !x.Inner.InTitleBar );

		paint.ClearPen();

		if ( SimpleNodeHeader )
		{
			paint.SetBrush( PrimaryTitleColor.Darken( 0.55f ) );
		}
		else
		{
			paint.SetBrushLinear( PrimaryTitleColor.Darken( 0.42f ), PrimaryTitleColor.Darken( 0.74f ) );
		}

		paint.DrawRect( hasBody ? titleRect : rect, radius );

		if ( hasBody )
		{
			// Square off the bottom of the header, then the accent line under it
			paint.DrawRect( titleRect.Shrink( 0f, radius, 0f, 0f ), 0f );

			paint.SetBrush( PrimaryTitleColor.WithAlpha( 0.9f ) );
			paint.DrawRect( new Rect( 0f, TitleHeight - 1.5f, rect.Width, 1.5f ), 0f );
		}

		// With a subtitle the two lines are one block, centred with the icon in the header
		var center = (TitleHeight - 1.5f) * 0.5f;
		var twoLines = Node.HasSubtitle;
		var titleCenter = twoLines ? center - 7f : center;
		var row = new Rect( HeaderPadLeft, 0f, rect.Width - HeaderPadLeft - HeaderPadRight, TitleHeight );

		if ( display.Icon is { } icon )
		{
			paint.SetPen( PrimaryTitleColor.Lighten( 0.35f ) );
			paint.DrawIcon( row with { Top = center - 12f, Height = 24f }, icon, twoLines ? 20f : 15f, TextFlag.LeftCenter );
			row.Left += HeaderIconWidth( twoLines );
		}

		paint.SetDefaultFont( 8f, 600 );
		paint.SetPen( NodeTheme.Text );
		paint.DrawText( row with { Top = titleCenter - 12f, Height = 24f }, display.Name, TextFlag.LeftCenter );

		if ( twoLines )
		{
			paint.SetDefaultFont( 7f );
			paint.SetPen( NodeTheme.Text.WithAlpha( 0.55f ) );
			paint.DrawText( row with { Top = center + 7.5f - 12f, Height = 24f }, Node.Subtitle, TextFlag.LeftCenter );
		}
	}

	/// <summary>
	/// A red pill over the bottom edge. The message itself is in the tooltip.
	/// </summary>
	void DrawErrorBadge( ref NodePaint paint, Rect rect )
	{
		const string text = "Error";

		paint.SetDefaultFont( 7f, 600 );
		var size = paint.MeasureText( text ) + new Vector2( 22f, 4f );
		var badge = new Rect( rect.Center.x - size.x * 0.5f, rect.Bottom - size.y * 0.5f, size.x, size.y );

		paint.ClearPen();
		paint.SetBrush( ErrorOutline );
		paint.DrawRect( badge, size.y * 0.5f );

		paint.SetPen( Color.White );
		paint.DrawIcon( badge.Shrink( 5f, 0f, 0f, 0f ), "error", 9f, TextFlag.LeftCenter );
		paint.DrawText( badge.Shrink( 15f, 0f, 5f, 0f ), text, TextFlag.LeftCenter );
	}
}
