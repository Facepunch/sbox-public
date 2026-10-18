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

	public Color SelectionOutline = Color.White;
	public Color PrimaryColor = Color.Parse( "#ff99c8" ) ?? default;
	public Color PrimaryTitleColor = Color.Gray;

	public Color ErrorOutline => Color.Red;
	public Color WarningOutline => Color.Yellow.Lighten( .5f );

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

	protected virtual void Layout()
	{
		var hasThumb = !Node.HasTitleBar && Node.DisplayInfo.Icon is not null || Node.Thumbnail is not null;
		var inputHeight = Inputs.Sum( x => x.Inner.InTitleBar ? 0f : 24f );
		var outputHeight = Outputs.Sum( x => x.Inner.InTitleBar ? 0f : 24f );
		var thumbnailSize = hasThumb ? Node.Thumbnail is not null ? 88f : 24f : 0f;
		var bodyHeight = MathF.Max( MathF.Max( inputHeight, outputHeight ), thumbnailSize );

		var totalWidth = 160f;
		var inputWidth = 80f;
		var outputWidth = 80f;

		if ( Node.AutoSize )
		{
			var titleWidth = Node.HasTitleBar ? NodePaint.Measure( DisplayInfo.Name, 7, 500 ).x + 44f : 0f;

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

		totalWidth = totalWidth.SnapToGrid( Graph.GridSize );
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

		_thumbRect = new Rect( inputWidth, TitleHeight > 0f ? TitleHeight - 2f : 0f, Size.x - inputWidth - outputWidth, Size.y - TitleHeight );

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
		var titleRect = new Rect( rect.Position, new Vector2( rect.Width, TitleHeight ) );
		var radius = 4;

		PrimaryColor = Node.PrimaryColor;
		PrimaryTitleColor = Node.TitleColor;

		paint.ClearPen();

		if ( Node.HasTitleBar )
		{
			if ( SimpleNodeHeader )
			{
				paint.SetBrush( PrimaryTitleColor );
			}
			else
			{
				paint.SetBrushLinear( PrimaryTitleColor, PrimaryTitleColor.Darken( 0.6f ) );
			}
		}
		else
		{
			paint.SetBrush( PrimaryColor.Darken( 0.6f ) );
		}

		paint.DrawRect( rect, radius );

		var display = DisplayInfo;

		if ( Node.HasTitleBar )
		{
			var titleRect2 = titleRect.Shrink( 4f, 0f, 4f, 0f );

			if ( display.Icon != null )
			{
				paint.SetPen( PrimaryColor.Lighten( 0.7f ).WithAlpha( 0.7f ) );
				paint.DrawIcon( titleRect2.Shrink( 4 ), display.Icon, 17, TextFlag.LeftCenter );
				titleRect2.Left += 18;
			}

			paint.SetDefaultFont( 7, 500 );
			paint.SetPen( PrimaryColor.Lighten( 0.8f ) );
			paint.DrawText( titleRect2.Shrink( 5, 0 ), display.Name, TextFlag.LeftCenter );

			if ( Node.HasSubtitle )
			{
				paint.DrawText( titleRect, $"( {Node.Subtitle} )", TextFlag.CenterBottom );
			}

			if ( Node.Thumbnail is not null || Inputs.Any( x => !x.Inner.InTitleBar ) || Outputs.Any( x => !x.Inner.InTitleBar ) )
			{
				// Square off the bottom of the title, then the body below it
				paint.ClearPen();
				paint.SetBrush( PrimaryColor.Darken( 0.6f ) );
				paint.DrawRect( new Rect( rect.Shrink( 0, TitleHeight, 0, 0 ).Position, new Vector2( rect.Width, TitleHeight - 12 ) ), 0 );
				paint.DrawRect( rect.Shrink( 0, TitleHeight, 0, 0 ), radius );
			}

			if ( Node.Thumbnail is { } thumb )
			{
				paint.Draw( _thumbRect.Align( new Vector2( 72f, 72f ), TextFlag.Center ), thumb );
			}
		}
		else if ( Node.Thumbnail is { } thumb )
		{
			// A big square thumbnail with a corner icon and the title at the bottom, e.g. for resource references
			var thumbRect = _thumbRect.Align( new Vector2( 88f, 88f ), TextFlag.Center );

			paint.Draw( thumbRect, thumb );

			if ( display.Icon is { } icon )
			{
				var iconRect = thumbRect.Shrink( 2f ).Align( new Vector2( 12f, 12f ), TextFlag.LeftTop );

				paint.ClearPen();
				paint.SetBrush( PrimaryColor.Darken( 0.6f ) );
				paint.DrawRect( iconRect.Grow( 4f ), 2f );

				paint.ClearBrush();
				paint.SetPen( NodeTheme.TextControl );
				paint.DrawIcon( iconRect, icon, 12f );
			}

			if ( display.Name is { } name )
			{
				name = FixWordWrapping( name );

				paint.SetDefaultFont( 7f );

				var backgroundRect = thumbRect.Align( NodePaint.Measure( name, 7f ), TextFlag.CenterBottom ).Grow( 4f, 2f, 4f, 2f );

				paint.SetBrush( PrimaryColor.Darken( 0.6f ) );
				paint.ClearPen();
				paint.DrawRect( backgroundRect, 2f );

				paint.SetPen( NodeTheme.TextControl );
				paint.DrawText( thumbRect, name, TextFlag.CenterBottom | TextFlag.WordWrap );
			}
		}
		else if ( display.Icon is { } icon )
		{
			// An icon without text, e.g. for operators
			var scale = icon.Length == 2 && !char.IsLetterOrDigit( icon[0] ) ? 0.5f : icon == "|" ? 0.75f : 1f;

			paint.SetPen( NodeTheme.TextControl );
			paint.DrawIcon( _thumbRect, icon, (Math.Min( _thumbRect.Width, _thumbRect.Height ) - 8f) * scale );
		}

		Node.OnPaint( paint, rect );

		var hasError = !string.IsNullOrWhiteSpace( Node.ErrorMessage );
		var selectionOutline = SelectionOutline;

		if ( hasError )
		{
			selectionOutline = ErrorOutline;

			if ( !paint.HasMouseOver )
			{
				selectionOutline = selectionOutline.Darken( 0.25f );
			}
		}

		paint.ClearBrush();

		if ( Selected )
		{
			paint.SetPen( selectionOutline, 2.0f );
		}
		else if ( paint.HasMouseOver && !hasError )
		{
			paint.SetPen( selectionOutline.Darken( 0.25f ), 1.0f );
		}
		else if ( hasError )
		{
			paint.SetPen( selectionOutline, 2.0f );
		}
		else
		{
			paint.SetPen( Color.Gray.Darken( 0.4f ), 1.0f );
		}

		paint.DrawRect( rect, radius );

		if ( hasError )
		{
			var errorText = "ERROR!!!";
			paint.SetDefaultFont();
			var errorRect = rect.Align( paint.MeasureText( errorText ), TextFlag.CenterBottom ).Grow( 4f, 0f, 4f, 0f );

			paint.SetBrush( paint.HasMouseOver ? ErrorOutline : ErrorOutline.Darken( 0.25f ) );
			paint.ClearPen();
			paint.DrawRect( errorRect, 1f );

			paint.SetPen( NodeTheme.TextControl );
			paint.DrawText( errorRect, errorText, TextFlag.CenterBottom );
		}
	}
}
