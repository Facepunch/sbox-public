using System.Collections.ObjectModel;
using System.Threading;

namespace Sandbox.Layout;

/// <summary>
/// Measures a leaf node's content under the given constraints. Return the content size, excluding the
/// node's own padding and border. See <see cref="MeasureMode"/> for what each mode asks for.
/// </summary>
internal delegate LayoutSize MeasureFunc(
	LayoutNode node,
	float width,
	MeasureMode widthMode,
	float height,
	MeasureMode heightMode );

/// <summary>
/// Returns the distance from the top of the node to its first baseline, for <c>align-items: baseline</c>.
/// </summary>
internal delegate float BaselineFunc( LayoutNode node, float width, float height );

/// <summary>
/// A node in a layout tree. Set <see cref="Style"/>, build the tree with <see cref="InsertChild"/>, call
/// <see cref="CalculateLayout"/> on the root, then read the <c>Layout*</c> results. Styles and structure
/// changes mark the path to the root dirty; a clean root means the whole tree is clean.
/// </summary>
internal sealed class LayoutNode
{
	private static uint s_currentGenerationCount;

	internal static uint NextGeneration() => Interlocked.Increment( ref s_currentGenerationCount );
	internal static uint CurrentGeneration => Volatile.Read( ref s_currentGenerationCount );

	// Most nodes are leaves, so the child list is created on the first insert. Readers go through
	// ChildrenOrEmpty, which hands out a shared empty list that must never be mutated.
	private static readonly List<LayoutNode> EmptyChildren = new();
	private List<LayoutNode> _children;
	private ReadOnlyCollection<LayoutNode> _readOnlyChildren;
	private LayoutResults _layout = LayoutResults.Create();
	private LayoutNode _owner;
	private MeasureFunc _measureFunc;
	private IInlineContent _inlineContent;
	private BaselineFunc _baselineFunc;
	private bool _isReferenceBaseline;
	private bool _alwaysFormsContainingBlock;
	private bool _isDirty = true;
	private int _contentsChildrenCount;
	private int _baselineUsers;
	private int _reuseBlockers;
	private int _fixedNodes;
	private bool _isFixed;
	private bool _usesBaseline;
	private bool _blocksReuse;
	private bool _alignsSelfByBaseline;
	private bool _alignsItemsByBaseline;
	private bool _usesPercent;
	private int _baselineSelfChildren;
	private StyleLength _processedWidth = StyleLength.Undefined;
	private StyleLength _processedHeight = StyleLength.Undefined;

	public LayoutNode()
	{
		Style = new LayoutStyle( this );
	}

	/// <summary>Style inputs. Setting any property marks this node (and its ancestors) dirty.</summary>
	public LayoutStyle Style { get; }

	/// <summary>Layout output and caches, stored inline in the node. Returned by reference; hold it as <c>ref var</c>.</summary>
	internal ref LayoutResults Layout => ref _layout;

	/// <summary>Arbitrary user data, typically the owning UI element.</summary>
	public object Context { get; set; }

	/// <summary>
	/// True if the last <see cref="CalculateLayout"/> produced new results for this node. Consumers clear it
	/// after reading the layout.
	/// </summary>
	public bool HasNewLayout { get; set; } = true;

	/// <summary>Whether this node needs to be laid out again.</summary>
	public bool IsDirty => _isDirty;

	/// <summary>Use this node as the baseline reference for its parent, instead of the first child.</summary>
	public bool IsReferenceBaseline
	{
		get => _isReferenceBaseline;
		set
		{
			if ( _isReferenceBaseline == value )
			{
				return;
			}

			_isReferenceBaseline = value;
			MarkDirtyAndPropagate();
		}
	}

	/// <summary>Treat this node as the containing block for absolutely positioned descendants even when static.</summary>
	public bool AlwaysFormsContainingBlock
	{
		get => _alwaysFormsContainingBlock;
		set
		{
			if ( _alwaysFormsContainingBlock == value )
			{
				return;
			}

			_alwaysFormsContainingBlock = value;
			MarkDirtyAndPropagate();
		}
	}

	/// <summary>
	/// Called when this node transitions from clean to dirty. It is not called for repeated changes while
	/// the node is already dirty.
	/// </summary>
	public Action<LayoutNode> DirtiedCallback { get; set; }

	internal int LineIndex { get; set; }

	// -----------------------------------------------------------------------------------------------
	// Tree
	// -----------------------------------------------------------------------------------------------

	/// <summary>The node this node is a child of, or null for a root.</summary>
	public LayoutNode Owner => _owner;

	/// <summary>A read-only, live view of this node's direct children.</summary>
	public IReadOnlyList<LayoutNode> Children => _children is null
		? Array.Empty<LayoutNode>()
		: _readOnlyChildren ??= _children.AsReadOnly();

	public int ChildCount => _children?.Count ?? 0;
	public LayoutNode GetChild( int index ) => ChildrenOrEmpty[index];

	/// <summary>The live child list, or a shared empty list for leaves. Read only.</summary>
	internal List<LayoutNode> ChildList => ChildrenOrEmpty;

	private List<LayoutNode> ChildrenOrEmpty => _children ?? EmptyChildren;

	public void AddChild( LayoutNode child ) => InsertChild( child, ChildCount );

	public void InsertChild( LayoutNode child, int index )
	{
		ArgumentNullException.ThrowIfNull( child );

		for ( LayoutNode ancestor = this; ancestor is not null; ancestor = ancestor._owner )
		{
			if ( ReferenceEquals( ancestor, child ) )
			{
				throw new InvalidOperationException( "Cannot create a cycle in the layout tree." );
			}
		}

		if ( child._owner is not null )
		{
			throw new InvalidOperationException( "Child already has an owner; it must be removed first." );
		}

		if ( HasMeasureFunc )
		{
			throw new InvalidOperationException( "Cannot add child: Nodes with measure functions cannot have children." );
		}

		_children ??= new List<LayoutNode>();
		if ( (uint)index > (uint)_children.Count )
		{
			throw new ArgumentOutOfRangeException( nameof( index ) );
		}

		if ( child.Style.Display == Display.Contents )
		{
			_contentsChildrenCount++;
		}

		_children.Insert( index, child );
		child._owner = this;
		AddSubtreeCounts( child._baselineUsers, child._reuseBlockers, child._fixedNodes );
		if ( child._alignsSelfByBaseline )
		{
			_baselineSelfChildren++;
		}

		MarkDirtyAndPropagate();
	}

	public bool RemoveChild( LayoutNode child )
	{
		if ( child is null || _children is null )
		{
			return false;
		}

		int index = _children.IndexOf( child );
		if ( index < 0 )
		{
			return false;
		}

		RemoveChildAt( index );
		return true;
	}

	public void RemoveChildAt( int index )
	{
		LayoutNode child = ChildrenOrEmpty[index];
		if ( child.Style.Display == Display.Contents )
		{
			_contentsChildrenCount--;
		}

		_children.RemoveAt( index );

		child.Layout.Reset();
		child._owner = null;
		AddSubtreeCounts( -child._baselineUsers, -child._reuseBlockers, -child._fixedNodes );
		if ( child._alignsSelfByBaseline )
		{
			_baselineSelfChildren--;
		}

		MarkDirtyAndPropagate();
	}

	public void RemoveAllChildren()
	{
		if ( ChildCount == 0 )
		{
			return;
		}

		int baselineUsers = 0;
		int reuseBlockers = 0;
		int fixedNodes = 0;
		foreach ( LayoutNode child in _children )
		{
			child.Layout.Reset();
			child._owner = null;
			baselineUsers += child._baselineUsers;
			reuseBlockers += child._reuseBlockers;
			fixedNodes += child._fixedNodes;
		}

		_children.Clear();
		_contentsChildrenCount = 0;
		_baselineSelfChildren = 0;
		AddSubtreeCounts( -baselineUsers, -reuseBlockers, -fixedNodes );
		MarkDirtyAndPropagate();
	}

	/// <summary>
	/// True if this node or any descendant aligns by baseline. Baseline alignment reads measured sizes across
	/// a whole subtree, so the layout algorithm takes none of its measurement shortcuts where this is set.
	/// Maintained incrementally on style changes and tree edits, like <see cref="HasContentsChildren"/>.
	/// </summary>
	internal bool SubtreeUsesBaseline => _baselineUsers > 0;

	/// <summary>
	/// True if this node or any descendant can make a fit-content measurement depend on the available size
	/// even when the content fits (<see cref="LayoutStyle.BlocksMeasureReuse"/>), in which case cached
	/// container measurements are only reused on an exact constraint match.
	/// </summary>
	internal bool SubtreeBlocksMeasureReuse => _reuseBlockers > 0;

	// Includes hidden/contents nodes so visibility changes cannot lose a fixed descendant.
	internal bool SubtreeHasFixed => _fixedNodes > 0;

	/// <summary>
	/// Whether a baseline computation may walk this node's children: it aligns its items by baseline, a
	/// direct child aligns itself by baseline, or a <c>display: contents</c> child hides such a child.
	/// Maintained incrementally, so the common negative answer is O(1).
	/// </summary>
	internal bool IsBaselineContainer
	{
		get
		{
			if ( _baselineSelfChildren > 0 || _alignsItemsByBaseline )
			{
				return true;
			}

			if ( _contentsChildrenCount == 0 )
			{
				return false;
			}

			foreach ( LayoutNode child in ChildrenOrEmpty )
			{
				if ( child.Style.Display == Display.Contents && child.SubtreeUsesBaseline )
				{
					return true;
				}
			}

			return false;
		}
	}

	/// <summary>
	/// True if this flex container has exactly one flexible child and that child has an explicit
	/// flex-basis (the single-flexible-child shortcut in <c>ComputeFlexBasisForChildren</c> then zeroes a
	/// basis it keeps across passes, so the algorithm must not skip such a pass). Conservatively true when a
	/// <c>display: contents</c> child hides the real children. Only consulted for single-dimension
	/// measurements of a container (<see cref="LayoutAlgorithm.IsAnsweredByAvailableSize"/>), so rather
	/// than maintaining it on every flex style change it is computed on first use per layout generation
	/// (styles and the tree cannot change during a layout) and kept in <see cref="LayoutResults"/>.
	/// </summary>
	internal bool HasSingleStickyFlexChild( uint generationCount )
	{
		ref LayoutResults layout = ref _layout;
		if ( layout.StickyFlexGeneration == generationCount )
		{
			return layout.HasSingleStickyFlexChild;
		}

		int flexible = 0;
		int sticky = 0;
		foreach ( LayoutNode child in ChildrenOrEmpty )
		{
			if ( !child.IsNodeFlexible() )
			{
				continue;
			}

			flexible++;
			if ( !Num.InexactEquals( child.ResolveFlexGrow(), 0.0f )
				&& !Num.InexactEquals( child.ResolveFlexShrink(), 0.0f )
				&& !child.ProcessFlexBasis().IsAuto )
			{
				sticky++;
			}
		}

		layout.StickyFlexGeneration = generationCount;
		return layout.HasSingleStickyFlexChild = _contentsChildrenCount > 0
			|| (flexible == 1 && sticky == 1);
	}

	private void AddSubtreeCounts( int baselineUsers, int reuseBlockers, int fixedNodes = 0 )
	{
		if ( baselineUsers == 0 && reuseBlockers == 0 && fixedNodes == 0 )
		{
			return;
		}

		for ( LayoutNode node = this; node is not null; node = node._owner )
		{
			node._baselineUsers += baselineUsers;
			node._reuseBlockers += reuseBlockers;
			node._fixedNodes += fixedNodes;
		}
	}

	/// <summary>
	/// True if the layout algorithm must run the complete pass sequence on this node (a single-dimension
	/// request in <see cref="LayoutAlgorithm.CalculateLayoutInternal"/> becomes a full measurement): baseline
	/// alignment in the subtree, or a percentage in the node's own style.
	/// </summary>
	internal bool NeedsExactPasses => _baselineUsers > 0 || _usesPercent;

	/// <summary>Called by the style setters that affect the layout algorithm's shortcut decisions.</summary>
	internal void OnStyleFlagsChanged()
	{
		bool isFixed = Style.PositionType == PositionType.Fixed;
		if ( isFixed != _isFixed )
		{
			AddSubtreeCounts( 0, 0, isFixed ? 1 : -1 );
			_isFixed = isFixed;
		}

		_alignsItemsByBaseline = Style.AlignItems == Align.Baseline
			|| Style.JustifyItems == Align.Baseline;
		_usesPercent = Style.UsesPercentages;

		bool alignsSelf = Style.AlignsSelfByBaseline;
		if ( alignsSelf != _alignsSelfByBaseline )
		{
			_alignsSelfByBaseline = alignsSelf;
			if ( _owner is not null )
			{
				_owner._baselineSelfChildren += alignsSelf ? 1 : -1;
			}
		}

		bool usesBaseline = Style.UsesBaselineAlignment;
		bool blocksReuse = Style.BlocksMeasureReuse();
		if ( usesBaseline == _usesBaseline && blocksReuse == _blocksReuse )
		{
			return;
		}

		AddSubtreeCounts(
			usesBaseline == _usesBaseline ? 0 : usesBaseline ? 1 : -1,
			blocksReuse == _blocksReuse ? 0 : blocksReuse ? 1 : -1 );
		_usesBaseline = usesBaseline;
		_blocksReuse = blocksReuse;
	}


	/// <summary>Whether any direct child is <c>display: contents</c>.</summary>
	internal bool HasContentsChildren => _contentsChildrenCount > 0;

	internal void OnChildDisplayChanged( bool wasContents, bool isContents )
	{
		if ( _owner is null || wasContents == isContents )
		{
			return;
		}

		_owner._contentsChildrenCount += isContents ? 1 : -1;
	}

	/// <summary>
	/// Children that participate in layout: <c>display: contents</c> nodes are replaced by their own
	/// children. Returns the live child list when no contents nodes are present, otherwise fills
	/// <paramref name="buffer"/>.
	/// </summary>
	internal List<LayoutNode> GetLayoutChildren( List<LayoutNode> buffer )
	{
		if ( _contentsChildrenCount == 0 )
		{
			return ChildrenOrEmpty;
		}

		buffer.Clear();
		CollectLayoutChildren( this, buffer );
		return buffer;
	}

	private static void CollectLayoutChildren( LayoutNode node, List<LayoutNode> into )
	{
		foreach ( LayoutNode child in node.ChildrenOrEmpty )
		{
			if ( child.Style.Display == Display.Contents )
			{
				if ( child.ChildCount > 0 )
				{
					CollectLayoutChildren( child, into );
				}
			}
			else
			{
				into.Add( child );
			}
		}
	}

	internal int LayoutChildCount
	{
		get
		{
			if ( _contentsChildrenCount == 0 )
			{
				return ChildCount;
			}

			int count = 0;
			foreach ( LayoutNode child in ChildrenOrEmpty )
			{
				if ( child.Style.Display == Display.Contents )
				{
					count += child.LayoutChildCount;
				}
				else
				{
					count++;
				}
			}

			return count;
		}
	}

	// -----------------------------------------------------------------------------------------------
	// Measure / baseline
	// -----------------------------------------------------------------------------------------------

	/// <summary>
	/// Custom content measurement for leaf nodes (text, images). A node with a measure function cannot
	/// have children.
	/// </summary>
	public MeasureFunc MeasureFunc
	{
		get => _measureFunc;
		set
		{
			if ( _measureFunc == value )
			{
				return;
			}

			if ( value is not null )
			{
				if ( InlineContent is not null )
					throw new InvalidOperationException( "Inline content and leaf measurement are mutually exclusive." );
				if ( ChildCount > 0 )
				{
					throw new InvalidOperationException( "Cannot set measure function: Nodes with measure functions cannot have children." );
				}
			}

			_measureFunc = value;
			MarkDirtyAndPropagate();
		}
	}

	public bool HasMeasureFunc => _measureFunc is not null;

	/// <summary>Non-leaf, text-only formatting context used by block dispatch. Mutually exclusive with leaf measurement.</summary>
	public IInlineContent InlineContent
	{
		get => _inlineContent;
		set
		{
			if ( ReferenceEquals( _inlineContent, value ) ) return;
			if ( value is not null && HasMeasureFunc )
				throw new InvalidOperationException( "Inline content and leaf measurement are mutually exclusive." );
			_inlineContent = value;
			MarkDirtyAndPropagate();
		}
	}

	/// <summary>Final fragments in paragraph content coordinates, never a union hit box.</summary>
	public IReadOnlyList<InlineFragment> InlineFragments { get; internal set; } = Array.Empty<InlineFragment>();

	public BaselineFunc BaselineFunc
	{
		get => _baselineFunc;
		set
		{
			if ( _baselineFunc == value )
			{
				return;
			}

			_baselineFunc = value;
			MarkDirtyAndPropagate();
		}
	}

	public bool HasBaselineFunc => _baselineFunc is not null;

	internal LayoutSize Measure( float availableWidth, MeasureMode widthMode, float availableHeight, MeasureMode heightMode )
	{
		LayoutSize size = _measureFunc( this, availableWidth, widthMode, availableHeight, heightMode );

		if ( Num.IsUndefined( size.Height )
			|| size.Height < 0
			|| Num.IsUndefined( size.Width )
			|| size.Width < 0 )
		{
			return new LayoutSize( Num.MaxOrDefined( 0.0f, size.Width ), Num.MaxOrDefined( 0.0f, size.Height ) );
		}

		return size;
	}

	internal float Baseline( float width, float height ) => _baselineFunc( this, width, height );

	// -----------------------------------------------------------------------------------------------
	// Dirtiness
	// -----------------------------------------------------------------------------------------------

	/// <summary>
	/// Mark this node as needing layout, e.g. because the content a measure function reports has changed.
	/// Propagates to the root.
	/// </summary>
	public void MarkDirty() => MarkDirtyAndPropagate();

	internal void MarkDirtyAndPropagate()
	{
		if ( _isDirty )
		{
			return;
		}

		SetDirty( true );
		Layout.ComputedFlexBasis = Num.Undefined;
		_owner?.MarkDirtyAndPropagate();
	}

	internal void SetDirty( bool isDirty )
	{
		if ( isDirty == _isDirty )
		{
			return;
		}

		_isDirty = isDirty;
		if ( isDirty )
		{
			DirtiedCallback?.Invoke( this );
		}
	}

	/// <summary>
	/// Reset the node to a freshly constructed state. It must have no children and no owner.
	/// </summary>
	public void Reset()
	{
		if ( ChildCount > 0 )
		{
			throw new InvalidOperationException( "Cannot reset a node which still has children attached" );
		}

		if ( _owner is not null )
		{
			throw new InvalidOperationException( "Cannot reset a node still attached to an owner" );
		}

		DirtiedCallback = null;
		Style.CopyFrom( new LayoutStyle( null ) );
		Layout.Reset();
		_children = null;
		_readOnlyChildren = null;
		_measureFunc = null;
		_inlineContent = null;
		InlineFragments = Array.Empty<InlineFragment>();
		_baselineFunc = null;
		Context = null;
		HasNewLayout = true;
		_isDirty = true;
		_isReferenceBaseline = false;
		_alwaysFormsContainingBlock = false;
		LineIndex = 0;
		_processedWidth = StyleLength.Undefined;
		_processedHeight = StyleLength.Undefined;
	}

	// -----------------------------------------------------------------------------------------------
	// Layout entry point and outputs
	// -----------------------------------------------------------------------------------------------

	/// <summary>
	/// Lay out this node and its subtree inside the given owner size (undefined for unconstrained).
	/// </summary>
	public void CalculateLayout( float ownerWidth = float.NaN, float ownerHeight = float.NaN, Direction ownerDirection = Direction.LTR )
	{
		LayoutAlgorithm.CalculateLayout( this, ownerWidth, ownerHeight, ownerDirection );
	}

	public float LayoutLeft => Layout.Position( PhysicalEdge.Left );
	public float LayoutTop => Layout.Position( PhysicalEdge.Top );
	public float LayoutRight => Layout.Position( PhysicalEdge.Right );
	public float LayoutBottom => Layout.Position( PhysicalEdge.Bottom );
	public float LayoutWidth => Layout.Dimension( Dimension.Width );
	public float LayoutHeight => Layout.Dimension( Dimension.Height );
	public Direction LayoutDirection => Layout.Direction;
	public bool LayoutHadOverflow => Layout.HadOverflow;

	public float LayoutMargin( PhysicalEdge edge ) => Layout.Margin( edge );
	public float LayoutBorder( PhysicalEdge edge ) => Layout.Border( edge );
	public float LayoutPadding( PhysicalEdge edge ) => Layout.Padding( edge );

	// -----------------------------------------------------------------------------------------------
	// Internal helpers
	// -----------------------------------------------------------------------------------------------

	internal float DimensionWithMargin( FlexDirection axis, float widthSize )
	{
		return Layout.MeasuredDimension( Axis.DimensionOf( axis ) )
			+ Style.ComputeMarginForAxis( axis, widthSize );
	}

	internal bool IsLayoutDimensionDefined( FlexDirection axis )
	{
		float value = Layout.MeasuredDimension( Axis.DimensionOf( axis ) );
		return Num.IsDefined( value ) && value >= 0.0f;
	}

	/// <summary>
	/// Whether the node has a "definite length" along the given axis. https://www.w3.org/TR/css-sizing-3/#definite
	/// </summary>
	internal bool HasDefiniteLength( Dimension dimension, float ownerSize )
	{
		float usedValue = GetProcessedDimension( dimension ).Resolve( ownerSize );
		return Num.IsDefined( usedValue ) && usedValue >= 0.0f;
	}

	internal StyleLength GetProcessedDimension( Dimension dimension ) => dimension == Dimension.Width ? _processedWidth : _processedHeight;

	internal float GetResolvedDimension( Direction direction, Dimension dimension, float referenceLength, float ownerWidth )
	{
		float value = GetProcessedDimension( dimension ).Resolve( referenceLength );
		if ( Style.BoxSizing == BoxSizing.BorderBox )
		{
			return value;
		}

		float paddingAndBorder = Style.ComputePaddingAndBorderForDimension( direction, dimension, ownerWidth );
		return value + (Num.IsDefined( paddingAndBorder ) ? paddingAndBorder : 0.0f);
	}

	internal StyleLength ProcessFlexBasis()
	{
		StyleLength flexBasis = Style.FlexBasis;
		if ( flexBasis.Unit != Unit.Auto && flexBasis.Unit != Unit.Undefined )
		{
			return flexBasis;
		}

		return StyleLength.Auto;
	}

	internal float ResolveFlexBasis( Direction direction, FlexDirection flexDirection, float referenceLength, float ownerWidth )
	{
		float value = ProcessFlexBasis().Resolve( referenceLength );
		if ( Style.BoxSizing == BoxSizing.BorderBox )
		{
			return value;
		}

		Dimension dim = Axis.DimensionOf( flexDirection );
		float paddingAndBorder = Style.ComputePaddingAndBorderForDimension( direction, dim, ownerWidth );
		return value + (Num.IsDefined( paddingAndBorder ) ? paddingAndBorder : 0.0f);
	}

	internal void ProcessDimensions()
	{
		_processedWidth = ProcessDimension( Dimension.Width );
		_processedHeight = ProcessDimension( Dimension.Height );
	}

	private StyleLength ProcessDimension( Dimension dim )
	{
		StyleLength max = Style.GetMaxDimension( dim );
		if ( max.IsDefined && StyleLength.InexactEquals( max, Style.GetMinDimension( dim ) ) )
		{
			return max;
		}

		return Style.GetDimension( dim );
	}

	internal Direction ResolveDirection( Direction ownerDirection )
	{
		if ( Style.Direction == Direction.Inherit )
		{
			return ownerDirection != Direction.Inherit ? ownerDirection : Direction.LTR;
		}

		return Style.Direction;
	}

	internal float ResolveFlexGrow()
	{
		// Root nodes flexGrow should always be 0
		if ( _owner is null )
		{
			return 0.0f;
		}

		if ( Num.IsDefined( Style.FlexGrow ) )
		{
			return Style.FlexGrow;
		}

		if ( Num.IsDefined( Style.Flex ) && Style.Flex > 0.0f )
		{
			return Style.Flex;
		}

		return LayoutStyle.DefaultFlexGrow;
	}

	internal float ResolveFlexShrink()
	{
		if ( _owner is null )
		{
			return 0.0f;
		}

		return Num.UnwrapOrDefault( Style.FlexShrink, LayoutStyle.DefaultFlexShrink );
	}

	internal bool IsNodeFlexible()
	{
		return !Style.IsOutOfFlow
			&& (ResolveFlexGrow() != 0 || ResolveFlexShrink() != 0);
	}

	/// <summary>
	/// If both left and right are defined, then use left. Otherwise return +left or -right depending on
	/// which is defined. Ignore statically positioned nodes as insets do not apply to them.
	/// </summary>
	private float RelativePosition( FlexDirection axis, Direction direction, float axisSize )
	{
		if ( Style.PositionType == PositionType.Static )
		{
			return 0;
		}

		if ( Style.IsInlineStartPositionDefined( axis, direction )
			&& !Style.IsInlineStartPositionAuto( axis, direction ) )
		{
			return Style.ComputeInlineStartPosition( axis, direction, axisSize );
		}

		return -1 * Style.ComputeInlineEndPosition( axis, direction, axisSize );
	}

	internal void SetPosition( Direction direction, float ownerWidth, float ownerHeight )
	{
		// Root nodes should be always laid out as LTR, so we don't return negative values.
		Direction directionRespectingRoot = _owner is not null ? direction : Direction.LTR;
		FlexDirection mainAxis = Axis.ResolveDirection( Style.FlexDirection, directionRespectingRoot );
		FlexDirection crossAxis = Axis.ResolveCrossDirection( mainAxis, directionRespectingRoot );

		// In the case of position static these are just 0. See: https://www.w3.org/TR/css-position-3/#valdef-position-static
		float relativePositionMain = RelativePosition(
			mainAxis,
			directionRespectingRoot,
			Axis.IsRow( mainAxis ) ? ownerWidth : ownerHeight );
		float relativePositionCross = RelativePosition(
			crossAxis,
			directionRespectingRoot,
			Axis.IsRow( mainAxis ) ? ownerHeight : ownerWidth );

		PhysicalEdge mainAxisLeadingEdge = Axis.InlineStartEdge( mainAxis, direction );
		PhysicalEdge mainAxisTrailingEdge = Axis.InlineEndEdge( mainAxis, direction );
		PhysicalEdge crossAxisLeadingEdge = Axis.InlineStartEdge( crossAxis, direction );
		PhysicalEdge crossAxisTrailingEdge = Axis.InlineEndEdge( crossAxis, direction );

		Layout.SetPosition(
			mainAxisLeadingEdge,
			Style.ComputeInlineStartMargin( mainAxis, direction, ownerWidth ) + relativePositionMain );
		Layout.SetPosition(
			mainAxisTrailingEdge,
			Style.ComputeInlineEndMargin( mainAxis, direction, ownerWidth ) + relativePositionMain );
		Layout.SetPosition(
			crossAxisLeadingEdge,
			Style.ComputeInlineStartMargin( crossAxis, direction, ownerWidth ) + relativePositionCross );
		Layout.SetPosition(
			crossAxisTrailingEdge,
			Style.ComputeInlineEndMargin( crossAxis, direction, ownerWidth ) + relativePositionCross );
	}

	public override string ToString()
	{
		return $"LayoutNode [{LayoutLeft}, {LayoutTop}, {LayoutWidth} x {LayoutHeight}] children={ChildCount}"
			+ (Context is not null ? $" ({Context})" : "");
	}
}
