using System;

namespace Editor.DooEditor;

public class BlockTree : TreeView
{
	Doo _doo;

	public BlockTree( Doo doo )
	{
		_doo = doo;
		MinimumWidth = 240;
		ItemSpacing = 4;
		BodyDropTarget = DragDropTarget.Closest;

		BuildNodes();
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( SetContentHash( ContentHash, 0.1f ) )
		{
			Clear();
			BuildNodes();
		}
	}

	int ContentHash()
	{
		if ( _doo?.Body == null )
			return 0;

		var hash = new HashCode();

		foreach ( var item in _doo.Body )
			hash.Add( item );

		return hash.ToHashCode();
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		if ( Visible )
		{
			Update();
		}
	}

	protected override DropAction OnBodyDragDrop( ItemDragEvent ev )
	{
		if ( ev.Data.Object is Doo.Block block )
		{
			if ( ev.IsDrop )
			{
				_doo.Body.Add( block );
			}

			return DropAction.Copy;
		}

		return base.OnBodyDragDrop( ev );
	}

	public void BuildNodes()
	{
		foreach ( var i in _doo.Body )
		{
			var root = new BlockTreeNode( _doo, i );
			AddItem( root );
			Open( root );
		}
	}
}
