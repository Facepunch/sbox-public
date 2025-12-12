using Sandbox.UI.Construct;

namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// Quickly add common panels with certain values as children.
	/// </summary>
	[Hide]
	public PanelCreator Add => new( this );

	/// <inheritdoc cref="Children"/>
	internal List<Panel> _children;

	internal bool _renderChildrenDirty;
	internal List<Panel> _renderChildren;
	internal HashSet<Panel> _childrenHash;

	/// <inheritdoc cref="Parent"/>
	internal Panel _parent;

	/// <summary>
	/// List of panels that are attached/<see cref="Parent">parented</see> directly to this one.
	/// </summary>
	[Hide]
	public IEnumerable<Panel> Children => _children == null ? Enumerable.Empty<Panel>() : _children.Where( x => x is not null );

	/// <summary>
	/// Whether this panel has any <see cref="Children">child panels</see> at all.
	/// </summary>
	[Hide]
	public bool HasChildren => _children is not null && _children.Count > 0;

	/// <summary>
	/// The panel we are directly attached to. This panel will be positioned relative to the given parent, and therefore move with it, typically also be hidden by the parents bounds.
	/// </summary>
	[Hide]
	public Panel Parent
	{
		get => _parent;
		set
		{
			if ( this is RootPanel && value != null )
				throw new Exception( "Can't parent a RootPanel" );

			if ( value == this )
				throw new Exception( "Can't parent a panel to itself" );

			if ( _parent == value )
				return;

			//
			// These types can't have children, so set them as 
			// siblings of us.
			//
			if ( value is Label || value is Image )
			{
				Parent = value.Parent;
				Parent.SetChildIndex( this, Parent.GetChildIndex( value ) + 1 );
				return;
			}

			var oldParent = _parent;
			_parent = null;

			if ( oldParent != null )
			{
				oldParent.RemoveChild( this );
			}

			_parent = value;

			if ( _parent != null )
			{
				_parent.InternalAddChild( this );

				if ( oldParent == null )
				{
					AddToLists();
				}

				//
				// We can set some inherited stuff here to get us started
				//
				ScaleToScreen = _parent.ScaleToScreen;
			}

			ParentHasChanged = true;
			// Dirty
		}
	}

	bool ParentHasChanged;
	bool IndexesDirty;

	/// <summary>
	/// Called internally when a child is removed, to remove from our Children list
	/// </summary>
	private void RemoveChild( Panel p )
	{
		if ( IsDeleted )
			return;

		if ( _children == null )
			throw new System.Exception( "RemoveChild but no children!" );

		if ( _childrenHash.Remove( p ) )
		{
			_children.Remove( p );
			_renderChildren.Remove( p );
			_renderChildrenDirty = true;

			if ( p.YogaNode is not null )
			{
				YogaNode?.RemoveChild( p.YogaNode );
			}

			OnChildRemoved( p );
			IndexesDirty = true;
			SetNeedsPreLayout();
		}
		else
		{
			throw new Exception( "Removed Child but didn't have child!" );
		}
	}

	/// <summary>
	/// A child panel has been removed from this panel.
	/// </summary>
	protected virtual void OnChildRemoved( Panel child )
	{

	}

	/// <summary>
	/// Deletes all child panels via <see cref="Delete"/>.
	/// </summary>
	/// <inheritdoc cref="Delete"/>
	public void DeleteChildren( bool immediate = false )
	{
		foreach ( var child in Children.ToArray() )
		{
			child.Delete( immediate );
		}
	}

	/// <summary>
	/// Add given panel as a child to this panel.
	/// </summary>
	public T AddChild<T>( T p ) where T : Panel
	{
		Assert.False( IsDeleted );

		p.Parent = this;
		return p;
	}

	/// <summary>
	/// Called internally when a child is added, to add to our <see cref="Children">children</see> list.
	/// </summary>
	private void InternalAddChild( Panel child )
	{
		if ( YogaNode?.IsMeasureDefined == true )
			throw new Exception( $"{this} can not have children." );

		_children ??= new( 4 );
		_renderChildren ??= new( 4 );
		_childrenHash ??= new( 4 );

		if ( _childrenHash.Contains( child ) )
			throw new Exception( "AddChild but already have child!" );

		YogaNode?.AddChild( child.YogaNode );

		_childrenHash.Add( child );
		_children.Add( child );
		_renderChildren.Add( child );
		_renderChildrenDirty = true;

		child.UpdateSiblingIndex( _children.Count - 1, _children.Count );
		OnChildAdded( child );
		SetNeedsPreLayout();

		IndexesDirty = true;
	}

	/// <summary>
	/// A child panel has been added to this panel.
	/// </summary>
	protected virtual void OnChildAdded( Panel child )
	{

	}

	/// <summary>
	/// Sort the <see cref="Children">children</see> using given comparison function.
	/// </summary>
	public void SortChildren( Comparison<Panel> sorter )
	{
		if ( _children == null || _children.Count <= 0 )
			return;

		_children.RemoveAll( x => x is null );
		_children.Sort( sorter );

		int i = 0;
		foreach ( var child in _children )
		{
			child.UpdateSiblingIndex( i++, _children.Count );
			YogaNode.RemoveChild( child.YogaNode );
			YogaNode.AddChild( child.YogaNode );
		}

		IndexesDirty = true;
	}

	/// <summary>
	/// Sort the <see cref="Children">children</see> using given comparison function.
	/// </summary>
	public void SortChildren<TargetType>( Func<TargetType, int> sorter )
	{
		if ( _children == null || _children.Count <= 0 )
			return;

		_children.RemoveAll( x => x is null );

		var sorted = _children.OrderBy( x => { if ( x is TargetType tt ) { return sorter( tt ); } return 0; } ).ToArray();
		_children.Clear();
		_children.AddRange( sorted );

		foreach ( var child in _children )
		{
			YogaNode.RemoveChild( child.YogaNode );
			YogaNode.AddChild( child.YogaNode );
		}

		IndexesDirty = true;
	}

	/// <summary>
	/// Sort the <see cref="Children">children</see> using given comparison function.
	/// </summary>
	public void SortChildren( Func<Panel, int> sorter ) => SortChildren<Panel>( sorter );

	/// <summary>
	/// Can be overridden by children to determine whether the panel is empty, and the :empty pseudo-class should be applied.
	/// </summary>
	protected virtual bool IsPanelEmpty()
	{
		return ChildrenCount == 0;
	}

	/// <summary>
	/// Should be called if overriding IsEmpty to notify the panel that its empty state has changed.
	/// </summary>
	protected void EmptyStateChanged()
	{
		UpdateChildrenIndexes();
	}

	void UpdateChildrenIndexes()
	{
		IndexesDirty = false;

		Switch( PseudoClass.Empty, IsPanelEmpty() );

		var count = ChildrenCount;
		if ( count == 0 )
			return;

		for ( int i = 0; i < count; i++ )
		{
			_children[i].UpdateSiblingIndex( i, count );
		}
	}

	internal void UpdateSiblingIndex( int index, int siblings )
	{
		Switch( PseudoClass.FirstChild, index == 0 );
		Switch( PseudoClass.LastChild, index == siblings - 1 );
		Switch( PseudoClass.OnlyChild, index == 0 && siblings == 1 );

		SiblingIndex = index;
	}

	/// <summary>
	/// The index of this panel in its parent's child list.
	/// </summary>
	[Hide]
	public int SiblingIndex { get; internal set; } = -1;


	/// <summary>
	/// Creates a panel of given type and makes it our child.
	/// </summary>
	/// <typeparam name="T">The panel to create.</typeparam>
	/// <param name="classnames">Optional CSS class names to apply to the newly created panel.</param>
	/// <returns>The created panel.</returns>
	public T AddChild<T>( string classnames = null ) where T : Panel, new()
	{
		var t = new T();
		t.Parent = this;

		if ( classnames != null )
			t.AddClass( classnames );

		return t;
	}

	/// <summary>
	/// Creates a panel of given type and makes it our child, returning it as an out argument.
	/// </summary>
	/// <typeparam name="T">The panel to create.</typeparam>
	/// <param name="outPanel">The created panel.</param>
	/// <param name="classnames">Optional CSS class names to apply to the newly created panel.</param>
	/// <returns>Always returns <see langword="true"/>.</returns>
	public bool AddChild<T>( out T outPanel, string classnames = null ) where T : Panel, new()
	{
		var t = new T();
		t.Parent = this;

		if ( classnames != null )
			t.AddClass( classnames );

		outPanel = t;
		return true;
	}

	/// <summary>
	/// Returns this panel and all its <see cref="Ancestors">ancestors</see>, i.e. the <see cref="Parent">Parent</see>, parent of its parent, etc.
	/// </summary>
	[Hide]
	public IEnumerable<Panel> AncestorsAndSelf
	{
		get
		{
			var p = this;

			while ( p != null )
			{
				yield return p;
				p = p.Parent;
			}
		}
	}

	/// <summary>
	/// Returns all ancestors, i.e. the parent, parent of our parent, etc.
	/// </summary>
	[Hide]
	public IEnumerable<Panel> Ancestors
	{
		get
		{
			var p = this.Parent;

			while ( p != null )
			{
				yield return p;
				p = p.Parent;
			}
		}
	}

	/// <summary>
	/// List of all panels that are attached to this panel, recursively, i.e. all <see cref="Children">children</see> of this panel, children of those children, etc.
	/// </summary>
	[Hide]
	public IEnumerable<Panel> Descendants
	{
		get
		{
			foreach ( var child in Children )
			{
				yield return child;

				foreach ( var descendant in child.Descendants )
				{
					yield return descendant;
				}
			}
		}
	}

	/// <summary>
	/// Is the given panel a parent, grandparent, etc.
	/// </summary>
	public bool IsAncestor( Panel panel )
	{
		if ( panel == this ) return true;
		if ( Parent != null ) return Parent.IsAncestor( panel );
		return false;
	}

	/// <summary>
	/// Returns the <see cref="RootPanel"/> we are ultimately attached to, if any.
	/// </summary>
	public RootPanel FindRootPanel()
	{
		if ( this is RootPanel rp ) return rp;
		return Parent?.FindRootPanel();
	}

	/// <summary>
	/// Returns the first <see cref="Ancestors">ancestor</see> panel that has no parent.
	/// </summary>
	public virtual Panel FindPopupPanel()
	{
		if ( Parent == null ) return this;
		return Parent?.FindPopupPanel();
	}

	/// <summary>
	/// Returns the scene that this panel belongs to
	/// </summary>
	public Scene Scene
	{
		get => GameObject?.Scene;

		[Obsolete( "Setting Scene is no longer supported. This should be done automatically, internally" )]
		set => GameObject = value;
	}

	/// <summary>
	/// Returns the GameObject that this panel belongs to
	/// </summary>
	public GameObject GameObject
	{
		get
		{
			if ( field is not null )
				return field;

			return Parent?.GameObject;
		}

		internal set;
	}

	/// <summary>
	/// Returns the index at which the given panel is <see cref="Parent">parented</see> to this panel, or -1 if it is not.
	/// </summary>
	public int GetChildIndex( Panel panel )
	{
		if ( panel == null || panel.Parent != this )
			return -1;

		if ( _children == null || _children.Count == 0 )
			return -1;

		return _children.IndexOf( panel );
	}

	/// <summary>
	/// Return a child at given index.
	/// </summary>
	/// <param name="index">Index at which to look.</param>
	/// <param name="loop">Whether to loop indices when out of bounds, i.e. -1 becomes last child, 11 becomes second child in a list of 10, etc.</param>
	/// <returns>Returns the requested child, or <see langword="null"/> if it was not found.</returns>
	public Panel GetChild( int index, bool loop = false )
	{
		if ( _children == null || _children.Count == 0 )
			return null;

		if ( loop )
		{
			index = index.UnsignedMod( Children.Count() );
		}
		else
		{
			if ( index < 0 ) return null;
			if ( index >= _children.Count ) return null;
		}

		return _children[index];
	}

	/// <summary>
	/// Amount of panels directly <see cref="Parent">parented</see> to this panel.
	/// </summary>
	[Hide]
	public int ChildrenCount => _children?.Count ?? 0;

	/// <summary>
	/// Returns a list of <see cref="Children">child panels</see> of given type.
	/// </summary>
	/// <typeparam name="T">The type of panels to retrieve.</typeparam>
	public IEnumerable<T> ChildrenOfType<T>() where T : Panel
	{
		if ( _children == null || _children.Count == 0 )
			yield break;

		var c = _children.Count;

		for ( int i = c - 1; i >= 0; i-- )
		{
			var child = _children[i];
			if ( child is T t )
			{
				yield return t;
			}
		}
	}
}
