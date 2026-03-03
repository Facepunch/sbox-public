namespace Sandbox.UI;

public partial class Panel
{
	int? LastOrder;

	internal void UpdateOrder()
	{
		if ( ComputedStyle.Order == LastOrder ) return;

		LastOrder = ComputedStyle.Order;
		Parent?.DirtyChildrenOrder();

	}

	bool NeedsOrderSort;

	internal void DirtyChildrenOrder()
	{
		NeedsOrderSort = true;
	}

	internal void SortChildrenOrder()
	{
		if ( !NeedsOrderSort ) return;

		NeedsOrderSort = false;

		if ( _children == null ) return;

		foreach ( var child in _children.OrderBy( x => x.LastOrder ?? 0 ).ThenBy( x => x.SiblingIndex ) )
		{
			if ( child.YogaNode is null )
				continue;

			YogaNode.RemoveChild( child.YogaNode );
			YogaNode.AddChild( child.YogaNode );
		}
	}

	/// <summary>
	/// Move this panel to be after the given sibling.
	/// </summary>
	public void MoveAfterSibling( Panel previousSibling )
	{
		if ( Parent == null )
			throw new ArgumentException( "Can't move after sibling if we have no parent" );

		if ( previousSibling.Parent != this.Parent )
			throw new ArgumentException( "previousSibling doesn't share a parent with us" );

		if ( Parent.IndexesDirty )
			Parent.UpdateChildrenIndexes();

		// already okay
		if ( previousSibling.SiblingIndex == SiblingIndex - 1 )
			return;

		Parent.SetChildIndex( this, previousSibling.SiblingIndex );
	}

	/// <summary>
	/// Move given child panel to be given index, where 0 is the first child.
	/// </summary>
	public void SetChildIndex( Panel child, int newIndex )
	{
		if ( child.Parent != this )
			throw new ArgumentException( "Can't set child index - it's not our child!" );

		if ( child.SiblingIndex == newIndex && newIndex < _children.Count && _children[newIndex] == child )
			return;

		newIndex = Math.Clamp( newIndex, 0, _children.Count - 1 );

		if ( child.YogaNode != null )
		{
			YogaNode?.RemoveChild( child.YogaNode );
			YogaNode?.AddChild( newIndex, child.YogaNode );
		}

		// Log.Info( $"{child.ElementName} Set Index To {newIndex} [si:{child.SiblingIndex}] [i:{_children.IndexOf( child )}]" );

		_children.Remove( child );
		_children.Insert( newIndex, child );
		UpdateChildrenIndexes();

		Assert.Equals( child.SiblingIndex, newIndex );
	}
}
