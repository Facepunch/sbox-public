using System;
using System.Reflection;

namespace Editor.NodeEditor;

public enum CommentColor
{
	White,
	Red,
	Green,
	Blue,
	Yellow,
	Purple,
	Orange
}

public interface ICommentNode : INode
{
	int Layer { get; set; }
	Vector2 Size { get; set; }
	CommentColor Color { get; set; }
	string Title { get; set; }
	string Description { get; set; }
}

public class CommentUI : NodeUI
{
	private ICommentNode Comment => Node as ICommentNode;

	protected override float TitleHeight => 40.0f;

	[Flags]
	private enum SizeDirection
	{
		None = 0,
		Top = 1 << 0,
		Bottom = 1 << 1,
		Left = 1 << 2,
		Right = 1 << 3
	}

	private bool _dragging;
	private bool _resizing;
	private Vector2 _offset;
	private Vector2 _minSize => new( 64.0f, 64.0f );
	private Vector2 _maxSize => new( 10000.0f, 10000.0f );
	private Vector2 _dragSize => new( 16.0f, 16.0f );
	private SizeDirection _direction;
	private RealTimeSince _lastMouseDown;
	private string _lastTitle;
	private string _lastDescription;
	private bool _didSelectDrag;

	public CommentUI( GraphView graph, ICommentNode node ) : base( graph, node )
	{
		HoverEvents = true;
		Selectable = true;
		Movable = true;
		ZIndex = -node.Layer;
	}

	protected override void OnRebuild()
	{
		base.OnRebuild();

		ZIndex = -Comment.Layer;
		Size = Comment.Size;

		ForceUpdate();
	}

	public void UpdateLayer()
	{
		var changedLayer = false;
		var lowestLayer = Comment.Layer;
		var nodes = Graph.Items.OfType<CommentUI>();

		foreach ( var n in nodes )
		{
			if ( n == this )
				continue;

			if ( SceneRect.IsInside( n.SceneRect, true ) )
			{
				n.UpdateLayer();
				continue;
			}

			if ( !n.SceneRect.IsInside( SceneRect ) )
				continue;

			if ( n.Comment.Layer <= lowestLayer )
			{
				changedLayer = true;
				lowestLayer = n.Comment.Layer;
			}
		}

		if ( changedLayer )
		{
			Comment.Layer = lowestLayer - 1;
			ZIndex = -Comment.Layer;
		}
	}


	private static Color GetColor( CommentColor color ) => color switch
	{
		CommentColor.Red => "#d60000",
		CommentColor.Green => "#33b679",
		CommentColor.Blue => "#039be5",
		CommentColor.Yellow => "#f6c026",
		CommentColor.Purple => "#8e24aa",
		CommentColor.Orange => "#f5511d",
		_ => "#C2B5B5"
	};

	protected override void OnPaint()
	{
		var alphaMultiplier = 0.6f;
		var comment = Comment;
		var color = GetColor( comment.Color );
		var rect = new Rect( 0, Size );

		UpdateTooltip();

		PrimaryColor = color.Darken( 0.1f ).Desaturate( 0.4f );

		if ( Paint.HasSelected )
			PrimaryColor = color.Darken( 0.1f ).Desaturate( 0.3f );
		else if ( Paint.HasMouseOver )
			PrimaryColor = color.Darken( 0.1f ).Desaturate( 0.35f );

		Paint.SetPen( PrimaryColor.WithAlpha( 0.5f * alphaMultiplier ), 1f );
		Paint.SetBrush( PrimaryColor.Darken( 0.7f ).WithAlpha( 0.8f * alphaMultiplier ) );
		Paint.DrawRect( rect, 5f );

		Paint.ClearPen();
		Paint.SetBrush( PrimaryColor.WithAlpha( 0.05f * alphaMultiplier ) );
		Paint.DrawRect( new Rect( 0f, TitleHeight, rect.Width - 1, rect.Height - TitleHeight ), 2 );

		if ( Paint.HasSelected )
		{
			Paint.SetPen( color.Lighten( 0.1f ).Desaturate( 0.3f ).WithAlpha( 1f * alphaMultiplier ), 2f );
			Paint.ClearBrush();
			Paint.DrawRect( rect.Shrink( 1f ), 4.0f );
		}
		else if ( Paint.HasMouseOver )
		{
			Paint.SetPen( color.Lighten( 0.1f ).Desaturate( 0.3f ).WithAlpha( 0.4f * alphaMultiplier ), 2f );
			Paint.ClearBrush();
			Paint.DrawRect( rect.Shrink( 1f ), 4.0f );
		}

		{
			rect = new Rect( rect.Position, new Vector2( rect.Width, TitleHeight ) ).Shrink( 3f );

			Paint.ClearPen();
			Paint.SetBrush( PrimaryColor.WithAlpha( 0.2f * alphaMultiplier ) );
			Paint.DrawRect( rect, 3f );

			if ( DisplayInfo.Icon != null )
			{
				Paint.SetPen( PrimaryColor.WithAlpha( 0.7f ) );
				Paint.DrawIcon( rect.Shrink( 4f ), DisplayInfo.Icon, 19f, TextFlag.LeftCenter );
				rect.Left += 24f;
			}

			// Title
			var title = DisplayInfo.Name;
			Paint.SetDefaultFont( 11f, 500 );
			Paint.SetPen( PrimaryColor );
			Paint.DrawText( rect.Shrink( 5f, 0f ), title, TextFlag.LeftCenter );

			// Description
			if ( !string.IsNullOrEmpty( comment.Description ) )
			{
				rect.Left = 0f;
				rect.Top += TitleHeight + 8f;

				var trimmedDesc = comment.Description.Substring( 0, Math.Min( comment.Description.Length, 300 ) );
				Paint.SetDefaultFont( 11f, 400 );
				Paint.SetPen( PrimaryColor.WithAlpha( 0.7f ) );
				Paint.DrawText( rect.Shrink( 16f, 0f ), trimmedDesc, TextFlag.WordWrap | TextFlag.DontClip | TextFlag.LeftTop );
			}
		}
	}

	protected override void OnMousePressed( GraphicsMouseEvent e )
	{
		if ( e.LeftMouseButton && !_resizing )
		{
			UpdateDirection( e.LocalPosition );

			if ( _direction != SizeDirection.None )
			{
				_resizing = true;
				e.Accepted = true;
			}
		}

		if ( _resizing )
		{
			e.Accepted = true;
		}

		if ( e.LeftMouseButton && !Selected && !_resizing )
		{
			var nodes = Graph.Items.OfType<NodeUI>();

			foreach ( var n in nodes )
			{
				// Only select this node if it is fully inside our box.
				if ( SceneRect.IsInside( n.SceneRect, true ) )
					n.Selected = true;
				else if ( !e.HasCtrl )
					n.Selected = false;
			}

			_didSelectDrag = true;
			e.Accepted = true;
			Selected = true;
		}

		_lastMouseDown = 0f;
		_dragging = true;

		base.OnMousePressed( e );
	}

	protected override void OnMouseMove( GraphicsMouseEvent e )
	{
		if ( _resizing )
		{
			UpdateResize( e );
			e.Accepted = true;
		}

		if ( _dragging )
		{
			UpdateLayer();
			NormalizeLayers();
		}

		base.OnMouseMove( e );
	}

	protected override void OnMouseReleased( GraphicsMouseEvent e )
	{
		var wasResizing = _resizing;

		if ( _resizing )
		{
			e.Accepted = true;

			Graph?.PushLayoutUndo( "Resize Comment" );

			UpdateResize( e );
			_resizing = false;

			Comment.Size = Size;
			Comment.Position = Position;

			ForceUpdate();

			Graph?.PushRedo();
		}

		if ( e.LeftMouseButton && _didSelectDrag && _lastMouseDown < 0.1f && !e.HasCtrl && !wasResizing )
		{
			e.Accepted = true;
		}

		_didSelectDrag = false;
		_dragging = false;

		base.OnMouseReleased( e );
	}

	protected override void OnHoverMove( GraphicsHoverEvent e )
	{
		base.OnHoverMove( e );

		UpdateDirection( e.LocalPosition );
	}

	protected override void OnHoverEnter( GraphicsHoverEvent e )
	{
		base.OnHoverEnter( e );

		UpdateDirection( e.LocalPosition );
	}

	protected override void OnHoverLeave( GraphicsHoverEvent e )
	{
		base.OnHoverLeave( e );

		_direction = SizeDirection.None;
		Cursor = CursorShape.SizeAll;
	}

	protected override void Layout()
	{
		Size = Comment.Size.Clamp( _minSize, _maxSize );
	}

	protected override void OnPositionChanged()
	{
		if ( _resizing ) return;
		base.OnPositionChanged();
	}

	private void NormalizeLayers()
	{
		var groups = Graph.Items.OfType<CommentUI>()
			.OrderBy( n => n.Comment.Layer )
			.GroupBy( n => n.Comment.Layer );

		var currentLayer = 1;

		foreach ( var g in groups )
		{
			foreach ( var n in g )
			{
				n.Comment.Layer = currentLayer;
				n.ZIndex = -n.Comment.Layer;
			}

			currentLayer++;
		}
	}

	public void ForceUpdate()
	{
		PrepareGeometryChange();
		Update();
	}

	private void UpdateDirection( Vector2 position )
	{
		_direction = SizeDirection.None;

		Cursor = CursorShape.SizeAll;

		if ( position.x <= _dragSize.x )
		{
			_direction |= SizeDirection.Left;
			_offset.x = position.x;
		}
		else if ( position.x >= Size.x - _dragSize.x )
		{
			_direction |= SizeDirection.Right;
			_offset.x = position.x - Size.x;
		}

		if ( position.y <= _dragSize.y )
		{
			_direction |= SizeDirection.Top;
			_offset.y = position.y;
			Cursor = _direction.HasFlag( SizeDirection.Left ) ? CursorShape.SizeFDiag :
				_direction.HasFlag( SizeDirection.Right ) ? CursorShape.SizeBDiag : CursorShape.SizeV;
		}
		else if ( position.y >= Size.y - _dragSize.y )
		{
			_direction |= SizeDirection.Bottom;
			_offset.y = position.y - Size.y;
			Cursor = _direction.HasFlag( SizeDirection.Left ) ? CursorShape.SizeBDiag :
				_direction.HasFlag( SizeDirection.Right ) ? CursorShape.SizeFDiag : CursorShape.SizeV;
		}
		else if ( _direction.HasFlag( SizeDirection.Left ) || _direction.HasFlag( SizeDirection.Right ) )
		{
			Cursor = CursorShape.SizeH;
		}
		else
		{
			Cursor = CursorShape.SizeAll;
		}
	}

	private Rect ResizeTop( Rect rect, float position )
	{
		rect.Top = position;
		var size = rect.Bottom - rect.Top;
		size -= size.Clamp( _minSize.y, _maxSize.y );
		rect.Top += size;
		return rect;
	}

	private Rect ResizeLeft( Rect rect, float position )
	{
		rect.Left = position;
		var size = rect.Right - rect.Left;
		size -= size.Clamp( _minSize.x, _maxSize.x );
		rect.Left += size;
		return rect;
	}

	private Rect ResizeBottom( Rect rect, float position )
	{
		rect.Bottom = position;
		var size = rect.Bottom - rect.Top;
		size -= size.Clamp( _minSize.y, _maxSize.y );
		rect.Bottom -= size;
		return rect;
	}

	private Rect ResizeRight( Rect rect, float position )
	{
		rect.Right = position;
		var size = rect.Right - rect.Left;
		size -= size.Clamp( _minSize.x, _maxSize.x );
		rect.Right -= size;
		return rect;
	}

	private void UpdateTooltip()
	{
		if ( Comment.Title == _lastTitle && Comment.Description == _lastDescription ) return;

		string name = $"<span style=\"font-size: 16px;font-weight: 900;\">{Comment.Title}</span>";
		string description = Comment.Description;
		ToolTip = $"{name}<br>{description}";

		_lastDescription = Comment.Description;
		_lastTitle = Comment.Title;
	}

	private void UpdateResize( GraphicsMouseEvent e )
	{
		if ( !_resizing ) return;

		var position = (e.ScenePosition - _offset).SnapToGrid( Graph.GridSize );
		var rect = SceneRect;

		if ( _direction.HasFlag( SizeDirection.Left ) )
			rect = ResizeLeft( rect, position.x );
		else if ( _direction.HasFlag( SizeDirection.Right ) )
			rect = ResizeRight( rect, position.x );

		if ( _direction.HasFlag( SizeDirection.Top ) )
			rect = ResizeTop( rect, position.y );
		else if ( _direction.HasFlag( SizeDirection.Bottom ) )
			rect = ResizeBottom( rect, position.y );

		SceneRect = rect;

		ForceUpdate();
	}
}
