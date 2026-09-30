using System.Runtime.CompilerServices;

namespace Sandbox.Layout;

internal struct CachedMeasurement
{
	// Keep scalar keys and results together; every node stores one layout entry and eight measurement entries.
	public float AvailableWidth;
	public float AvailableHeight;
	public float OwnerWidth;
	public float OwnerHeight;
	public float ComputedWidth;
	public float ComputedHeight;
	public SizingMode WidthSizingMode;
	public SizingMode HeightSizingMode;

	/// <summary>Which of the computed dimensions are valid; a partial entry only answers requests for that dimension.</summary>
	public MeasureScope Scope;

	/// <summary>
	/// False if this entry only serves an exact constraint match: some node in the subtree was sized from
	/// its available space rather than its content (<see cref="LayoutAlgorithm.MeasureNodeWithFixedSize"/>
	/// with no room), or an explicit flex basis below has been rewritten since
	/// (<see cref="LayoutAlgorithm.NoteFlexBasisChange"/>).
	/// </summary>
	public bool ContentBased;
	public bool HadOverflow;

	public static CachedMeasurement Empty => new()
	{
		AvailableWidth = -1,
		AvailableHeight = -1,
		OwnerWidth = -1,
		OwnerHeight = -1,
		WidthSizingMode = SizingMode.MaxContent,
		HeightSizingMode = SizingMode.MaxContent,
		ComputedWidth = -1,
		ComputedHeight = -1,
		Scope = MeasureScope.Both,
		ContentBased = true,
	};
}

/// <summary>The measurement cache: a fixed inline buffer of <see cref="LayoutResults.MaxCachedMeasurements"/> entries.</summary>
[InlineArray( LayoutResults.MaxCachedMeasurements )]
internal struct CachedMeasurementBuffer
{
	private CachedMeasurement _element0;
}

/// <summary>
/// Per-node layout output plus the caches the algorithm uses to skip redundant work. Lives inline in
/// <see cref="LayoutNode"/> (see <see cref="LayoutNode.Layout"/>, which returns it by reference) so a node
/// is a single allocation; take it as <c>ref var</c>, never by value.
/// </summary>
internal struct LayoutResults
{
	// Eight entries cover nearly all measured layout trees while keeping the cache inline.
	public const int MaxCachedMeasurements = 8;

	public uint ComputedFlexBasisGeneration;
	public float ComputedFlexBasis;

	public uint GenerationCount;
	public Direction LastOwnerDirection;
	public int NextCachedMeasurementsIndex;
	public CachedMeasurementBuffer CachedMeasurements;
	public CachedMeasurement CachedLayout;

	public Direction Direction;
	public bool HadOverflow;

	// Set by the owner before this node is measured: an ancestor aligns its items by baseline and may walk
	// this node's subtree, so every pass here has to run the complete sequence (see CalculateLayoutInternal).
	public bool BaselineSensitive;

	// Whether exactly one child is flexible and has an explicit flex basis: computed on first use per layout
	// generation (LayoutNode.HasSingleStickyFlexChild). The bool sits with the other byte fields above.
	public bool HasSingleStickyFlexChild;
	public uint StickyFlexGeneration;

	// Block layout: margins that escape this box to collapse with siblings / ancestors, and whether the
	// box is empty enough for its top and bottom margins to collapse through it.
	public CollapsibleMargin MarginTopSet;
	public CollapsibleMargin MarginBottomSet;
	public bool MarginsCanCollapseThrough;
	public float InlineBaseline;

	// Block/grid layout: where an absolutely positioned child without insets would have been placed.
	public float StaticPositionX;
	public float StaticPositionY;

	private float _width, _height;
	private float _measuredWidth, _measuredHeight;
	private float _left, _top, _right, _bottom;
	private float _marginLeft, _marginTop, _marginRight, _marginBottom;
	private float _borderLeft, _borderTop, _borderRight, _borderBottom;
	private float _paddingLeft, _paddingTop, _paddingRight, _paddingBottom;

	/// <summary>The freshly constructed state: undefined sizes, empty caches, everything else zero.</summary>
	public static LayoutResults Create()
	{
		LayoutResults results = default;
		results.ComputedFlexBasis = Num.Undefined;
		results.LastOwnerDirection = Direction.Inherit;
		for ( int i = 0; i < MaxCachedMeasurements; i++ )
		{
			results.CachedMeasurements[i] = CachedMeasurement.Empty;
		}

		results.CachedLayout = CachedMeasurement.Empty;
		results.Direction = Direction.Inherit;
		results.InlineBaseline = float.NaN;
		results._width = results._height = results._measuredWidth = results._measuredHeight = Num.Undefined;
		return results;
	}

	/// <summary>Reset to a freshly constructed state.</summary>
	public void Reset() => this = Create();

	public readonly float Dimension( Dimension axis ) => axis == Layout.Dimension.Width ? _width : _height;

	public void SetDimension( Dimension axis, float value )
	{
		if ( axis == Layout.Dimension.Width )
		{
			_width = value;
		}
		else
		{
			_height = value;
		}
	}

	public readonly float MeasuredDimension( Dimension axis ) => axis == Layout.Dimension.Width ? _measuredWidth : _measuredHeight;

	public void SetMeasuredDimension( Dimension axis, float value )
	{
		if ( axis == Layout.Dimension.Width )
		{
			_measuredWidth = value;
		}
		else
		{
			_measuredHeight = value;
		}
	}

	public readonly float Position( PhysicalEdge edge )
	{
		return edge switch
		{
			PhysicalEdge.Left => _left,
			PhysicalEdge.Top => _top,
			PhysicalEdge.Right => _right,
			_ => _bottom,
		};
	}

	public void SetPosition( PhysicalEdge edge, float value )
	{
		switch ( edge )
		{
			case PhysicalEdge.Left:
				_left = value;
				break;
			case PhysicalEdge.Top:
				_top = value;
				break;
			case PhysicalEdge.Right:
				_right = value;
				break;
			default:
				_bottom = value;
				break;
		}
	}

	public readonly float Margin( PhysicalEdge edge )
	{
		return edge switch
		{
			PhysicalEdge.Left => _marginLeft,
			PhysicalEdge.Top => _marginTop,
			PhysicalEdge.Right => _marginRight,
			_ => _marginBottom,
		};
	}

	public void SetMargin( PhysicalEdge edge, float value )
	{
		switch ( edge )
		{
			case PhysicalEdge.Left:
				_marginLeft = value;
				break;
			case PhysicalEdge.Top:
				_marginTop = value;
				break;
			case PhysicalEdge.Right:
				_marginRight = value;
				break;
			default:
				_marginBottom = value;
				break;
		}
	}

	public readonly float Border( PhysicalEdge edge )
	{
		return edge switch
		{
			PhysicalEdge.Left => _borderLeft,
			PhysicalEdge.Top => _borderTop,
			PhysicalEdge.Right => _borderRight,
			_ => _borderBottom,
		};
	}

	public void SetBorder( PhysicalEdge edge, float value )
	{
		switch ( edge )
		{
			case PhysicalEdge.Left:
				_borderLeft = value;
				break;
			case PhysicalEdge.Top:
				_borderTop = value;
				break;
			case PhysicalEdge.Right:
				_borderRight = value;
				break;
			default:
				_borderBottom = value;
				break;
		}
	}

	public readonly float Padding( PhysicalEdge edge )
	{
		return edge switch
		{
			PhysicalEdge.Left => _paddingLeft,
			PhysicalEdge.Top => _paddingTop,
			PhysicalEdge.Right => _paddingRight,
			_ => _paddingBottom,
		};
	}

	public void SetPadding( PhysicalEdge edge, float value )
	{
		switch ( edge )
		{
			case PhysicalEdge.Left:
				_paddingLeft = value;
				break;
			case PhysicalEdge.Top:
				_paddingTop = value;
				break;
			case PhysicalEdge.Right:
				_paddingRight = value;
				break;
			default:
				_paddingBottom = value;
				break;
		}
	}
}
