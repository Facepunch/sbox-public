using static Editor.BaseItemWidget;

namespace Editor;

public partial class TreeNode
{
	public TreeView TreeView { get; internal set; }

	public object Value { get; set; }

	public IEnumerable<TreeNode> Children => children;
	public virtual bool HasChildren => children != null && children.Count > 0;
	public float Height { get; set; }

	public bool Enabled { get; set; } = true;

	internal int Index { get; set; }

	public TreeNode Parent => parent;

	/// <summary>
	/// If true the default expander won't be drawn
	/// </summary>
	public virtual bool ExpanderHidden => false;

	/// <summary>
	/// If a node returns true, you can expand by clicking anywhere on the node.
	/// </summary>
	public virtual bool ExpanderFills => false;

	/// <summary>
	/// If true the name of this node can be edited.
	/// </summary>
	public virtual bool CanEdit => false;

	/// <summary>
	/// The editable name of this node.
	/// </summary>
	public virtual string Name { get; set; }

	internal bool dirty = true;
	List<TreeNode> children = new List<TreeNode>();
	TreeNode parent;

	internal int _rebuildCount;

	public TreeNode( object value ) : this()
	{
		Value = value;
	}

	public TreeNode()
	{
		Height = Theme.RowHeight;
		EditorEvent.Register( this );
	}

	public void Dirty()
	{
		if ( dirty )
			return;

		dirty = true;
		TreeView?.Dirty( this );
	}

	public virtual void Clear()
	{
		children.Clear();
		Dirty();
	}

	internal void InternalBuildChildren()
	{
		BuildChildren();
	}

	protected virtual void BuildChildren()
	{

	}

	/// <summary>
	/// Called when we want to rename this item (using F2)
	/// </summary>
	/// <param name="item"></param>
	/// <param name="text">What we're wanting to rename to</param>
	/// <param name="selection">what nodes have we got selected (can be any number of nodes)</param>
	public virtual void OnRename( VirtualWidget item, string text, List<TreeNode> selection = null )
	{

	}

	public virtual void OnPaint( VirtualWidget item )
	{
		Paint.SetPen( Theme.Text );
		Paint.DrawText( item.Rect, $"{Value}", TextFlag.LeftTop );
	}

	public void OnVisible()
	{
		if ( !dirty ) return;
		dirty = false;

		_rebuildCount++;
		BuildChildren();
	}

	internal void ThinkInternal()
	{
		Think();
	}

	RealTimeSince updateTime = 0;

	protected virtual void Think()
	{
		if ( updateTime < 0.1f ) return;
		updateTime = Random.Shared.Float( 0.0f, 0.1f );

		UpdateHash();

		if ( !dirty ) return;
		dirty = false;

		RebuildOnDirty();
	}

	protected virtual void RebuildOnDirty()
	{
		_rebuildCount++;
		BuildChildren();
		TreeView?.Update();

		dirty = false;
	}

	public void AddItem( object item )
	{
		if ( item is not TreeNode tn )
		{
			tn = new TreeNode { Value = item };
		}

		if ( children.Contains( tn ) )
		{
			children.Remove( tn );
			children.Add( tn );
			TreeView?.Dirty( this );
			return;
		}

		tn.parent = this;
		children.Add( tn );

		TreeView?.Dirty( this );
	}

	public void AddItems( IEnumerable<object> items )
	{
		foreach ( var item in items )
		{
			AddItem( item );
		}
	}

	public void RemoveItem( TreeNode item )
	{
		item.parent = null;
		children.Remove( item );

		TreeView?.Dirty( this );
	}

	public void SetItems( IEnumerable<TreeNode> items )
	{
		children.Clear();
		children.AddRange( items );

		foreach ( var item in items )
		{
			item.parent = this;
		}

		TreeView?.Dirty( this );
	}

	/// <summary>
	/// Paint the standard selection/hover/press highlight background
	/// </summary>
	protected void PaintSelection( VirtualWidget item )
	{
		if ( !item.Selected && !item.Hovered && !item.Pressed && !item.Dropping && !item.Dragging ) return;

		var r = new Rect( 0, item.Rect.Top, TreeView.LocalRect.Right, item.Rect.Height );

		Paint.ClearPen();
		Paint.ClearBrush();

		if ( item.Hovered )
		{
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.1f ) );
		}

		if ( item.Selected )
		{
			Paint.SetBrush( Theme.Primary.WithAlpha( 0.3f ) );
		}

		if ( item.Dragging )
		{
			Paint.SetBrush( Theme.Pink.WithAlpha( 0.2f ) );
		}

		Paint.DrawRect( r, 1 );

		if ( item.Dropping )
		{
			Paint.SetPen( Theme.Pink, 2, PenStyle.Dot );
			Paint.DrawRect( r, 1 );
		}

		Paint.SetPen( Theme.Text );
	}

	public virtual void OnSelectionChanged( bool state )
	{

	}

	/// <summary>
	/// Fill this out in your node to allow the tree system to work out
	/// whether this object lies within your node's unexpanded children.
	/// This should return true if the passed in object is a decendant of 
	/// this node - no matter how deep.
	/// </summary>
	protected virtual bool HasDescendant( object obj )
	{
		return false;
	}

	public virtual TreeNode ResolveNode( object obj, bool createPath )
	{
		if ( Value == obj ) return this;

		//
		// Does it exist in on of our existing children?
		//
		foreach ( var child in children )
		{
			if ( child is null ) continue;

			var r = child.ResolveNode( obj, false );
			if ( r != null ) return r;
		}

		//
		// Does it exist in one of our unexpanded children?
		//
		if ( createPath && HasDescendant( obj ) )
		{
			// if so then build our children
			RebuildOnDirty();

			// and let them resolve it
			foreach ( var child in children )
			{
				var r = child.ResolveNode( obj, true );
				if ( r != null )
					return r;
			}

			Log.Warning( $"TreeNode Error: HasChild '{obj}' but not in built children" );
		}

		return null;
	}

	public IEnumerable<TreeNode> EnumeratePathTo( TreeNode node )
	{
		if ( children == null )
			yield break;

		foreach ( var child in children )
		{
			if ( child == node )
			{
				yield return this;
				yield break;
			}

			var r = child.EnumeratePathTo( node );
			if ( r.Any() )
			{
				yield return this;

				foreach ( var i in r )
					yield return i;

				yield break;
			}
		}
	}

	public override string ToString()
	{
		return $"TreeNode:{Value}";
	}

	/// <summary>
	/// The node has been right clicked. Return true to override default path
	/// </summary>
	public virtual bool OnContextMenu()
	{
		return false;
	}

	public virtual string GetTooltip()
	{
		return null;
	}

	/// <summary>
	/// If the hash code changes we'll re-evaluate this node
	/// </summary>
	public virtual int ValueHash => Value?.GetHashCode() ?? 0;

	int lastHash = 0;

	/// <summary>
	/// Called every frame for visible nodes - allows the node to update itself more organically
	/// </summary>
	internal void UpdateHash()
	{
		var hash = ValueHash;

		if ( lastHash == hash )
			return;

		lastHash = hash;
		OnHashChanged();
	}

	/// <summary>
	/// The value of ValueHash has changed
	/// </summary>
	protected virtual void OnHashChanged()
	{
		RebuildOnDirty();
	}

	/// <summary>
	/// Set so we don't have any child nodes
	/// </summary>
	public void ClearChildren()
	{
		if ( children.Count == 0 )
			return;

		children.Clear();
		Dirty();
	}


	/// <summary>
	/// Set the children to match this list.
	/// Remove any that aren't in the list.
	/// Use the function to create the node.
	/// Make sure the order matches the incoming list.
	/// </summary>
	public void SetChildren<T>( IEnumerable<T> list, Func<T, TreeNode> createNode )
	{
		var childrenLookup = children
			.Where( x => x.Value is not null )
			.ToDictionary( x => x.Value, x => x );

		var removeList = children
			.Select( x => x.Value )
			.Where( value => value is not null )
			.ToHashSet();

		bool changed = false;

		int position = 0;
		foreach ( var child in list )
		{
			if ( child is null ) continue;

			childrenLookup.TryGetValue( child, out var treeNode );

			AddChild( child, createNode, position++, treeNode );
			removeList.Remove( child );
		}

		foreach ( var missing in removeList )
		{
			if ( children.RemoveAll( x => x.Value == missing ) > 0 )
			{
				changed = true;
			}
		}

		children.Sort( SortByIndex );

		if ( changed )
		{
			Dirty();
		}
	}

	int SortByIndex( TreeNode a, TreeNode b )
	{
		return a.Index - b.Index;
	}

	void AddChild<T>( T child, Func<T, TreeNode> createNode, int position, TreeNode found )
	{
		if ( found != null )
		{
			if ( found.Index == position )
				return;

			found.Index = position;
			found.parent = this;
			TreeView?.Dirty( this );
			return;
		}

		// else just stick it where it's meant to be

		var created = createNode( child );
		created.parent = this;
		created.Index = position;
		children.Add( created );
		TreeView?.Dirty( this );
	}

	public virtual bool OnDragStart()
	{
		return false;
	}

	public virtual void OnDragHover( Widget.DragEvent ev )
	{
		ev.Action = DropAction.Ignore;
	}
	public virtual void OnDrop( Widget.DragEvent ev )
	{
		ev.Action = DropAction.Ignore;
	}

	/// <summary>
	/// Return true if this value or tree node is one of our ancestors
	/// </summary>
	public virtual bool HasAncestor( object obj )
	{
		if ( Value == obj || this == obj )
			return true;

		return Parent?.HasAncestor( obj ) ?? false;
	}

	public virtual void OnKeyPress( KeyEvent e )
	{

	}

	public virtual DropAction OnDragDrop( ItemDragEvent e )
	{
		return DropAction.Ignore;
	}

	/// <summary>
	/// Called when the item is double clicked, or selected and enter is pressed
	/// </summary>
	public virtual void OnActivated()
	{

	}
}

/// <summary>
/// A small wrapper that changes Value into the T type.
/// </summary>
public abstract class TreeNode<T> : TreeNode where T : class
{
	public new T Value
	{
		get => base.Value as T;
		set => base.Value = value;
	}

	public TreeNode( T value ) : base( value )
	{
	}

	public TreeNode()
	{
	}
}
