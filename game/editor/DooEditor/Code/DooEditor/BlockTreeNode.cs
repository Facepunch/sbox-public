using System;
using static Editor.BaseItemWidget;

namespace Editor.DooEditor;

public class BlockTreeNode : TreeNode<Doo.Block>
{
	Doo _doo;
	DisplayInfo _displayInfo;

	public BlockTreeNode( Doo doo, Doo.Block block ) : base( block )
	{
		_doo = doo;
		_displayInfo = DisplayInfo.ForType( block.GetType() );
	}

	public override bool HasChildren => Value?.HasBody() ?? false;

	protected override void BuildChildren()
	{
		if ( HasChildren && Value.Body != null )
		{
			SetChildren( Value.Body, x => new BlockTreeNode( _doo, x ) );
		}
		else
		{
			ClearChildren();
		}
	}

	public override int ValueHash => HashCode.Combine( Value, Value?.Body?.Count );

	public override void OnPaint( VirtualWidget item )
	{
		bool isBlock = Value.HasBody();
		var rect = item.Rect;

		Paint.ClearPen();

		var bgColor = TypeLibrary.GetType( Value.GetType() ).GetAttribute<IconAttribute>()?.BackgroundColor ?? Color.White;
		var fgColor = TypeLibrary.GetType( Value.GetType() ).GetAttribute<IconAttribute>()?.ForegroundColor ?? Color.White;

		if ( isBlock && item.IsOpen )
		{
			var hasChildren = item.ChildrenRect.Height > 0;

			if ( hasChildren )
				rect.Add( item.ChildrenRect );

			rect = rect.Grow( 0, 0, 0, 0 );

			Paint.SetBrush( item.Selected ? bgColor : bgColor.WithAlpha( 0.7f ) );
			Paint.DrawRect( rect, 4 );

			if ( hasChildren )
			{
				rect = item.ChildrenRect.Grow( 4, 4, 8, 4 );
				Paint.SetBrush( Theme.ControlBackground );
				Paint.DrawRect( rect, 4 );
			}
		}
		else if ( isBlock )
		{
			rect = rect.Grow( 0, 0, 0, 0 );

			Paint.SetBrush( item.Selected ? bgColor : bgColor.WithAlpha( 0.7f ) );
			Paint.DrawRect( rect, 4 );
		}
		else
		{
			Paint.SetBrush( item.Selected ? bgColor : bgColor.WithAlpha( 0.7f ) );
			Paint.DrawRect( item.Rect, 4 );
		}

		var r = item.Rect;
		r.Left += 4;

		Paint.Pen = item.Selected ? fgColor : fgColor.WithAlphaMultiplied( 0.5f );
		Paint.DrawIcon( r, _displayInfo.Icon ?? "people", 11, TextFlag.LeftCenter );

		r.Left += 18;
		r.Height -= 2;

		var text = Value.GetNodeString();

		Paint.Pen = item.Selected ? fgColor : fgColor.WithAlphaMultiplied( 0.5f );
		Paint.DrawText( r, text, TextFlag.LeftCenter );

		if ( item.Dropping )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue );

			if ( TreeView.CurrentItemDragEvent.DropEdge.HasFlag( ItemEdge.Top ) )
			{
				var droprect = item.Rect;
				droprect.Top -= 1;
				droprect.Height = 2;
				Paint.DrawRect( droprect, 2 );
			}
			else if ( TreeView.CurrentItemDragEvent.DropEdge.HasFlag( ItemEdge.Bottom ) )
			{
				var droprect = item.Rect;
				droprect.Top = droprect.Bottom - 1;
				droprect.Height = 2;
				Paint.DrawRect( droprect, 2 );
			}
			else
			{
				Paint.SetBrushAndPen( Theme.Blue.WithAlpha( 0.2f ), Theme.Blue );
				Paint.PenSize = 2;
				Paint.DrawRect( item.Rect, 4 );
			}
		}
	}

	public override bool OnContextMenu()
	{
		var menu = new ContextMenu( null );
		menu.AddOption( "Delete", "close", Menu_Delete );
		menu.OpenAtCursor();

		return true;
	}

	void Menu_Delete()
	{
		_doo.DeleteBlock( Value );
	}

	public override bool OnDragStart()
	{
		var drag = new Drag( TreeView );
		drag.Data.Object = this;
		drag.Execute();

		return true;
	}

	// Handle all drags (internal reordering and external from toolbox)
	public override DropAction OnDragDrop( BaseItemWidget.ItemDragEvent e )
	{
		// Handle dropping a block from toolbox
		if ( e.Data.Object is Doo.Block block )
		{
			if ( e.IsDrop )
			{
				InsertBlockAtEdge( block, e.DropEdge );
			}
			return DropAction.Move;
		}

		// Handle dropping an existing tree node (reordering)
		if ( e.Data.Object is BlockTreeNode node )
		{
			if ( !e.IsDrop ) return DropAction.Move;
			if ( node == this || node.Value == Value ) return DropAction.Move;
			if ( IsDescendantOf( node ) ) return DropAction.Move;

			InsertBlockAtEdge( node.Value, e.DropEdge );
			TreeView.Open( this );
			Parent?.Dirty();
			return DropAction.Move;
		}

		return DropAction.Ignore;
	}

	void InsertBlockAtEdge( Doo.Block block, ItemEdge edge )
	{
		if ( edge.HasFlag( ItemEdge.Top ) )
		{
			_doo.InsertBefore( Value, block );
		}
		else if ( edge.HasFlag( ItemEdge.Bottom ) )
		{
			_doo.InsertAfter( Value, block );
		}
		else if ( Value.HasBody() )
		{
			_doo.AddChild( Value, block );
		}
		else
		{
			_doo.InsertAfter( Value, block );
		}
	}

	bool IsDescendantOf( BlockTreeNode potentialAncestor )
	{
		var current = Parent as BlockTreeNode;
		while ( current != null )
		{
			if ( current == potentialAncestor )
				return true;
			current = current.Parent as BlockTreeNode;
		}
		return false;
	}

	public override bool CanEdit => true;

	public override void OnSelectionChanged( bool state )
	{
		base.OnSelectionChanged( state );

		if ( state )
		{
			TreeView.GetAncestor<DooEditorWidget>()?.Inspector.SetTarget( Value );
		}
	}
}
