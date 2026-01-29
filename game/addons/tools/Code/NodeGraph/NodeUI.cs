using System.Text.RegularExpressions;
using DisplayInfo = Sandbox.DisplayInfo;

namespace Editor.NodeEditor;

public class RerouteUI : NodeUI
{
	public class Comment : GraphicsItem
	{
		private string _text;
		public string Text
		{
			get => _text;
			set
			{
				_text = value;
				Update();
			}
		}

		protected override void OnPaint()
		{
			if ( string.IsNullOrWhiteSpace( Text ) )
				return;

			Paint.Antialiasing = true;
			Paint.TextAntialiasing = true;

			Paint.SetDefaultFont( 10 );

			var rect = LocalRect;
			rect = Paint.MeasureText( rect, Text );
			rect.Width += 20;
			rect.Width = rect.Width.Clamp( 0, LocalRect.Width );
			rect.Position = LocalRect.Center - new Vector2( MathF.Floor( rect.Width ) * 0.5f, 0 );
			rect.Top = LocalRect.Top;
			rect.Bottom = LocalRect.Bottom - 5;

			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.8f ) );
			Paint.DrawRect( rect, 2 );

			var pos = new Vector2( LocalRect.Center.x, LocalRect.Bottom - 5 );
			Paint.DrawArrow( pos, pos + Vector2.Down * 5, 10 );

			Paint.SetPen( Theme.TextControl );
			Paint.DrawText( rect, Text );
		}

		protected override void OnMousePressed( GraphicsMouseEvent e )
		{
			base.OnMousePressed( e );

			e.Accepted = false;
		}
	}

	private Comment _comment;

	public RerouteUI( GraphView graph, INode node ) : base( graph, node )
	{
		ZIndex = 0;
		Position = node.Position;
		Size = 16;
		HandlePosition = 0.5f;
		ToolTip = null;

		if ( node is IRerouteNode reroute )
		{
			_comment = new Comment
			{
				Parent = this,
				Size = new Vector2( 500, 30 ),
				Position = new Vector2( 0, -25 ),
				HandlePosition = new Vector2( 0.5f, 0.5f )
			};

			_comment.Bind( nameof( Comment.Text ) )
				.ReadOnly()
				.From( reroute, nameof( reroute.Comment ) );
		}
	}

	protected override void OnPaint()
	{
		var color = Outputs.First().HandleConfig.Color;

		if ( !Paint.HasMouseOver )
		{
			color = color.Desaturate( 0.2f ).Darken( 0.3f );
		}

		Paint.SetPen( Theme.ControlBackground, 2 );
		Paint.SetBrush( Paint.HasSelected ? SelectionOutline : color );
		Paint.DrawRect( LocalRect, 10 );
	}

	protected override void Layout()
	{
		var preferSelectingOutput = Inputs.Any( x => x.Connection is not null );

		foreach ( var input in Inputs )
		{
			input.Size = 14;
			input.Position = 14 * -0.5f;
			input.Visible = false;
			input.ZIndex = input.DefaultZIndex = preferSelectingOutput ? 0f : 2f;
		}

		foreach ( var output in Outputs )
		{
			output.Size = 14;
			output.Position = 14 * -0.5f;
			output.Visible = false;
			output.ZIndex = output.DefaultZIndex = preferSelectingOutput ? 2f : 0f;
		}
	}
}

public partial class NodeUI : GraphicsItem
{
	public INode Node { get; protected set; }

	public GraphView Graph { get; protected set; }

	public DisplayInfo DisplayInfo => Node.DisplayInfo;

	public Color SelectionOutline = Color.Parse( "#ff99c8" ) ?? default;
	public Color PrimaryColor = Color.Parse( "#ff99c8" ) ?? default;

	public List<PlugIn> Inputs = new();
	public List<PlugOut> Outputs = new();

	protected virtual float TitleHeight => Node.HasTitleBar ? 24f : 0f;

	private Rect _thumbRect;

	public override Rect BoundingRect => base.BoundingRect.Grow( 8f, 4f, 8f, 4f );

	private class Button : GraphicsItem
	{
		public Action OnPress { get; set; }
		public string Icon { get; set; }

		public Button( NodeUI parent ) : base( parent )
		{
			HoverEvents = true;
			Cursor = CursorShape.Finger;
		}

		protected override void OnPaint()
		{
			Paint.SetPen( Theme.TextControl.WithAlpha( Paint.HasMouseOver ? 0.9f : 0.4f ) );
			Paint.DrawIcon( LocalRect, Icon, 14, TextFlag.Center );
			Paint.DrawRect( LocalRect );
		}

		protected override void OnMousePressed( GraphicsMouseEvent e )
		{
			base.OnMousePressed( e );

			if ( e.LeftMouseButton )
			{
				e.Accepted = true;
			}
		}

		protected override void OnMouseReleased( GraphicsMouseEvent e )
		{
			base.OnMouseReleased( e );

			if ( e.LeftMouseButton && LocalRect.IsInside( e.LocalPosition + Size * HandlePosition ) )
			{
				OnPress?.Invoke();
				e.Accepted = true;
			}
		}
	}

	public NodeUI( GraphView graph, INode node )
	{
		ZIndex = 1;
		Node = node;
		Graph = graph;
		Movable = true;
		Selectable = true;
		HoverEvents = true;
		Cursor = CursorShape.SizeAll;

		Size = new Vector2( 256, 512 );
		Position = node.Position;

		UpdatePlugs( true );

		Node.Changed += MarkNodeChanged;
	}

	public void Rebuild()
	{
		OnRebuild();
	}

	protected virtual void OnRebuild()
	{
		Position = Node.Position;
	}

	protected override void OnDestroy()
	{
		Node.Changed -= MarkNodeChanged;
	}

	public void MarkNodeChanged()
	{
		if ( !IsValid )
		{
			return;
		}

		UpdatePlugs( false );
		Update();

		Graph?.NodePositionChanged( this );
	}

	public static string FormatToolTip( string name, string description,
		Type type = null, string error = null )
	{
		var tooltip = name.WithColor( "#9CDCFE" );

		if ( type is not null )
		{
			tooltip += $": {type.ToRichText()}";
		}

		var desc = description ?? "No description given.";
		tooltip += desc.StartsWith( "<br/>", StringComparison.OrdinalIgnoreCase )
			? desc
			: $"<br/>{desc}";

		foreach ( var message in error?.Split( Environment.NewLine, StringSplitOptions.RemoveEmptyEntries ) ?? Array.Empty<string>() )
		{
			tooltip += $"<br/><span style=\"font-size: 11px; color: {Theme.Red.Hex};\">{message}</span>";
		}

		return tooltip;
	}

	protected override void OnHoverEnter( GraphicsHoverEvent e )
	{
		var display = DisplayInfo;
		ToolTip = FormatToolTip( display.Name, display.Description, null, Node.ErrorMessage );
		base.OnHoverEnter( e );
	}

	private void UpdatePlugs( bool firstTime )
	{
		if ( !IsValid )
		{
			return;
		}

		for ( var i = Inputs.Count - 1; i >= 0; --i )
		{
			var plugIn = Inputs[i];
			var input = Node.Inputs.FirstOrDefault( x => x == plugIn.Inner );

			if ( input is null )
			{
				Inputs.RemoveAt( i );

				var connection = plugIn.Connection;
				connection?.Disconnect();
				connection?.Destroy();

				plugIn.Destroy();
			}
		}

		for ( var i = Outputs.Count - 1; i >= 0; --i )
		{
			var plugOut = Outputs[i];
			var output = Node.Outputs.FirstOrDefault( x => x == plugOut.Inner );

			if ( output is null )
			{
				Outputs.RemoveAt( i );

				Graph.RemoveConnections( plugOut );
				plugOut.Destroy();
			}
		}

		var index = 0;
		foreach ( var plug in Node.Inputs )
		{
			var match = Inputs.FirstOrDefault( x => x.Inner == plug );

			if ( !match.IsValid() )
			{
				Inputs.Insert( index, new PlugIn( this, plug ) );
			}
			else if ( Inputs.IndexOf( match ) != index )
			{
				Inputs.Remove( match );
				Inputs.Insert( index, match );

				match.Update();
			}

			++index;
		}

		index = 0;
		foreach ( var plug in Node.Outputs )
		{
			var match = Outputs.FirstOrDefault( x => x.Inner == plug );

			if ( !match.IsValid() )
			{
				Outputs.Insert( index, new PlugOut( this, plug ) );
			}
			else if ( Outputs.IndexOf( match ) != index )
			{
				Outputs.Remove( match );
				Outputs.Insert( index, match );

				match.Update();
			}

			++index;
		}

		Layout();
		Graph?.NodePositionChanged( this );
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
			Paint.SetDefaultFont( 7, 500 );
			var titleWidth = Node.HasTitleBar ? Paint.MeasureText( DisplayInfo.Name ).x + 44f : 0f;

			if ( Inputs.Any( x => x.Inner.InTitleBar ) )
			{
				titleWidth += 24f;
			}

			if ( Outputs.Any( x => x.Inner.InTitleBar ) )
			{
				titleWidth += 24f;
			}

			Paint.SetDefaultFont();
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

		_thumbRect = new Rect( inputWidth, TitleHeight > 0f ? TitleHeight - 2f : 0f, Size.x - inputWidth - outputWidth, Size.y - TitleHeight );

		if ( hasThumb && Node.HasTitleBar )
		{
			_thumbRect = _thumbRect.Shrink( 6f, 5f, 6f, 4f );

			if ( inputWidth <= 0f != outputWidth <= 0f )
			{
				_thumbRect = _thumbRect.Align( thumbnailSize - 16f, inputWidth <= 0f ? TextFlag.LeftCenter : TextFlag.RightCenter );
			}
		}

		PrepareGeometryChange();
	}

	private static Regex WordWrapPointRegex { get; } = new Regex( "[^A-Z][A-Z]|_[^_]" );

	/// <summary>
	/// Make <paramref name="value"/> able to word wrap at more places, like after an underscore or between words in PascalCase.
	/// </summary>
	private static string FixWordWrapping( string value )
	{
		return WordWrapPointRegex.Replace( value, x => $"{x.Value[0]}\x200B{x.Value[1]}" );
	}

	protected override void OnPaint()
	{
		var rect = new Rect( 0f, Size );
		var radius = 4;

		PrimaryColor = Node.GetPrimaryColor( Graph );
		if ( Paint.HasSelected ) PrimaryColor = SelectionOutline;
		else
		{
			if ( !Node.IsReachable ) PrimaryColor = PrimaryColor.Desaturate( 0.5f ).Darken( 0.25f );
		}

		if ( Node.HasTitleBar )
		{
			Paint.ClearPen();
			Paint.SetBrush( PrimaryColor.Darken( 0.5f ) );
			Paint.DrawRect( rect, radius );
		}
		else
		{
			Paint.ClearPen();
			Paint.SetBrush( PrimaryColor.Darken( 0.6f ) );
			Paint.DrawRect( rect, radius );
		}

		Paint.ClearPen();
		Paint.ClearBrush();

		Paint.ClearPen();
		Paint.SetBrush( PrimaryColor.WithAlpha( 0.05f ) );

		var display = DisplayInfo;

		if ( Node.HasTitleBar )
		{
			// Normal node display, with a title bar and possible thumbnail

			var titleRect = new Rect( rect.Position, new Vector2( rect.Width, TitleHeight ) ).Shrink( 4f, 0f, 4f, 0f );

			if ( display.Icon != null )
			{
				Paint.SetPen( PrimaryColor.Lighten( 0.7f ).WithAlpha( 0.7f ) );
				Paint.DrawIcon( titleRect.Shrink( 4 ), display.Icon, 17, TextFlag.LeftCenter );
				titleRect.Left += 18;
			}

			var title = display.Name;

			Paint.SetDefaultFont( 7, 500 );
			Paint.SetPen( PrimaryColor.Lighten( 0.8f ) );
			Paint.DrawText( titleRect.Shrink( 5, 0 ), title, TextFlag.LeftCenter );

			if ( Node.Thumbnail is not null || Inputs.Any( x => !x.Inner.InTitleBar ) || Outputs.Any( x => !x.Inner.InTitleBar ) )
			{
				// body inner
				float borderSize = 3;
				Paint.ClearPen();
				Paint.SetBrush( PrimaryColor.Darken( 0.6f ) );
				Paint.DrawRect( rect.Shrink( borderSize, TitleHeight, borderSize, borderSize ), radius - 2 );
			}

			if ( Node.Thumbnail is { } thumb )
			{
				var thumbRect = _thumbRect.Align( new Vector2( 72f, 72f ), TextFlag.Center );

				Paint.Draw( thumbRect, thumb );
			}
		}
		else if ( Node.Thumbnail is { } thumb )
		{
			// Node is a big square thumbnail with corner icon and title at the bottom, e.g. for resource / game object references

			var thumbRect = _thumbRect.Align( new Vector2( 88f, 88f ), TextFlag.Center );

			Paint.Draw( thumbRect, thumb );

			if ( Node.DisplayInfo.Icon is { } icon )
			{
				var iconRect = thumbRect.Shrink( 2f ).Align( new Vector2( 12f, 12f ), TextFlag.LeftTop );

				Paint.ClearPen();
				Paint.SetBrush( PrimaryColor.Darken( 0.6f ) );
				Paint.DrawRect( iconRect.Grow( 4f ), 2f );

				Paint.ClearBrush();
				Paint.SetPen( Theme.TextControl );
				Paint.DrawIcon( iconRect, icon, 12f );
			}

			if ( Node.DisplayInfo.Name is { } name )
			{
				var textRect = thumbRect;

				name = FixWordWrapping( name );

				Paint.SetFont( null, 7f );

				var backgroundRect = Paint.MeasureText( textRect, name, TextFlag.CenterBottom | TextFlag.WordWrap )
					.Grow( 4f, 2f, 4f, 2f );

				Paint.SetBrush( PrimaryColor.Darken( 0.6f ) );
				Paint.ClearPen();
				Paint.DrawRect( backgroundRect, 2f );

				Paint.SetPen( Theme.TextControl );
				Paint.DrawText( textRect, name, TextFlag.CenterBottom | TextFlag.WordWrap );
			}
		}
		else if ( Node.DisplayInfo.Icon is { } icon )
		{
			// Node is an icon without text, e.g. for operators

			var scale = icon.Length == 2 && !char.IsLetterOrDigit( icon[0] ) ? 0.5f : icon == "|" ? 0.75f : 1f;

			Paint.SetPen( Theme.TextControl );
			Paint.DrawIcon( _thumbRect, icon, (Math.Min( _thumbRect.Width, _thumbRect.Height ) - 8f) * scale );
		}

		Node.OnPaint( rect );

		var hasError = !string.IsNullOrWhiteSpace( Node.ErrorMessage );
		var selectionOutline = SelectionOutline;

		if ( hasError )
		{
			selectionOutline = Color.Red;

			if ( !Paint.HasMouseOver )
			{
				selectionOutline = selectionOutline.Darken( 0.25f );
			}
		}

		if ( Paint.HasSelected )
		{
			Paint.SetPen( selectionOutline, 2.0f );
			Paint.ClearBrush();
			Paint.DrawRect( rect, radius );
		}
		else if ( Paint.HasMouseOver && !hasError )
		{
			Paint.SetPen( selectionOutline.Darken( 0.25f ), 1.0f );
			Paint.ClearBrush();
			Paint.DrawRect( rect, radius );
		}
		else if ( hasError )
		{
			Paint.SetPen( selectionOutline, 2.0f );
			Paint.ClearBrush();
			Paint.DrawRect( rect, radius );
		}

		if ( hasError )
		{
			var errorText = "ERROR!!!";
			var errorRect = Paint.MeasureText( rect.Align( Size, TextFlag.CenterBottom ), errorText, TextFlag.CenterBottom )
			.Grow( 4f, 0f, 4f, 0f );

			Paint.SetBrush( Paint.HasMouseOver ? Color.Red : Color.Red.Darken( 0.25f ) );
			Paint.ClearPen();
			Paint.DrawRect( errorRect, 1f );

			Paint.SetPen( Theme.TextControl );
			Paint.DrawText( errorRect, errorText, TextFlag.CenterBottom );
		}
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		Graph?.MoveablePressed();
	}

	protected override void OnMouseReleased( GraphicsMouseEvent e )
	{
		Graph?.MoveableReleased();
	}

	internal void DraggingPlug( Plug plug, Vector2 scenePosition, Connection source = null )
	{
		Graph?.DraggingPlug( plug, scenePosition, source );
	}

	internal void DroppedPlug( Plug plug, Vector2 scenePosition, Connection source = null )
	{
		Graph?.DroppedPlug( plug, scenePosition, source );
	}

	protected override void OnPositionChanged()
	{
		Position = Position.SnapToGrid( Graph.GridSize );

		if ( Node != null )
		{
			Graph?.MoveableMoved();
			Node.Position = Position;
		}

		Graph?.NodePositionChanged( this );
	}
}
