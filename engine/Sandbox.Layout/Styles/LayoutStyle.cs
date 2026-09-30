using System.Runtime.CompilerServices;

namespace Sandbox.Layout;

[InlineArray( EdgeValues.Count )]
internal struct EdgeValues
{
	public const int Count = 9;

	private StyleLength _element0;
}

[InlineArray( GutterValues.Count )]
internal struct GutterValues
{
	public const int Count = 3;

	private StyleLength _element0;
}

[InlineArray( DimensionValues.Count )]
internal struct DimensionValues
{
	public const int Count = 2;

	private StyleLength _element0;
}

/// <summary>
/// The layout-relevant style of a node. Every setter compares against the current value and marks the
/// owning node dirty when it changes, so callers can push styles unconditionally each frame.
/// </summary>
internal sealed class LayoutStyle
{
	public bool IsOutOfFlow => PositionType is PositionType.Absolute or PositionType.Fixed;

	public const float DefaultFlexGrow = 0.0f;
	public const float DefaultFlexShrink = 1.0f;

	private readonly LayoutNode _node;

	internal LayoutStyle( LayoutNode node )
	{
		_node = node;

		_flexBasis = StyleLength.Auto;
		_dimensions[0] = StyleLength.Auto;
		_dimensions[1] = StyleLength.Auto;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private void Dirty() => _node?.MarkDirtyAndPropagate();

	// -----------------------------------------------------------------------------------------------
	// Enum properties
	// -----------------------------------------------------------------------------------------------

	private Direction _direction = Direction.Inherit;
	public Direction Direction
	{
		get => _direction;
		set
		{
			if ( _direction == value )
			{
				return;
			}

			_direction = value;
			Dirty();
		}
	}

	private FlexDirection _flexDirection = FlexDirection.Row;
	public FlexDirection FlexDirection
	{
		get => _flexDirection;
		set
		{
			if ( _flexDirection == value )
			{
				return;
			}

			_flexDirection = value;
			Dirty();
		}
	}

	// CSS `justify-content: normal`: behaves as flex-start in flex containers and as stretch in grid.
	private Justify _justifyContent = Justify.Stretch;
	public Justify JustifyContent
	{
		get => _justifyContent;
		set
		{
			if ( _justifyContent == value )
			{
				return;
			}

			_justifyContent = value;
			Dirty();
		}
	}

	private Align _alignContent = Align.Stretch;
	public Align AlignContent
	{
		get => _alignContent;
		set
		{
			if ( _alignContent == value )
			{
				return;
			}

			_alignContent = value;
			Dirty();
		}
	}

	private Align _alignItems = Align.Stretch;
	public Align AlignItems
	{
		get => _alignItems;
		set
		{
			if ( _alignItems == value )
			{
				return;
			}

			_alignItems = value;
			_node?.OnStyleFlagsChanged();
			Dirty();
		}
	}

	private Align _alignSelf = Align.Auto;
	public Align AlignSelf
	{
		get => _alignSelf;
		set
		{
			if ( _alignSelf == value )
			{
				return;
			}

			_alignSelf = value;
			_node?.OnStyleFlagsChanged();
			Dirty();
		}
	}

	/// <summary>Whether this node's alignment can make a container compute baselines (see <see cref="LayoutNode.SubtreeUsesBaseline"/>).</summary>
	internal bool UsesBaselineAlignment => _alignItems == Align.Baseline
		|| _alignSelf == Align.Baseline
		|| JustifyItems == Align.Baseline
		|| JustifySelf == Align.Baseline;

	/// <summary>Whether this node aligns itself by baseline, which makes its owner a baseline container.</summary>
	internal bool AlignsSelfByBaseline => _alignSelf == Align.Baseline
		|| JustifySelf == Align.Baseline;

	/// <summary>Whether this style has a percentage length (dimensions, min/max, flex-basis, margins, paddings, borders, gaps).</summary>
	internal bool UsesPercentages => _percentLengths > 0;

	/// <summary>
	/// Whether this node can make a fit-content measurement depend on the available size even when the
	/// content fits in it - see <see cref="LayoutAlgorithm.CanReuseContainerMeasurement"/> and
	/// <see cref="LayoutNode.SubtreeBlocksMeasureReuse"/>.
	/// </summary>
	internal bool BlocksMeasureReuse()
	{
		return _percentLengths > 0
			|| _display == Display.Grid
			|| _display == Display.Block
			|| _flexWrap != Wrap.NoWrap
			|| Num.IsDefined( _aspectRatio )
			|| _maxDimensions[0].IsDefined
			|| _maxDimensions[1].IsDefined;
	}

	// Number of percentage lengths among the dimensions, min/max, flex-basis, margins, paddings, borders
	// and gaps (at most 37, so a byte); kept up to date by their setters so the predicates above are O(1).
	private byte _percentLengths;

	private void TrackPercent( StyleLength previous, StyleLength value )
	{
		if ( previous.IsPercent == value.IsPercent )
		{
			return;
		}

		_percentLengths = (byte)(_percentLengths + (value.IsPercent ? 1 : -1));
		if ( _percentLengths == (value.IsPercent ? 1 : 0) )
		{
			_node?.OnStyleFlagsChanged();
		}
	}

	private PositionType _positionType = PositionType.Relative;
	public PositionType PositionType
	{
		get => _positionType;
		set
		{
			if ( _positionType == value )
			{
				return;
			}

			_positionType = value;
			_node?.OnStyleFlagsChanged();
			Dirty();
		}
	}

	private Wrap _flexWrap = Wrap.NoWrap;
	public Wrap FlexWrap
	{
		get => _flexWrap;
		set
		{
			if ( _flexWrap == value )
			{
				return;
			}

			_flexWrap = value;
			_node?.OnStyleFlagsChanged();
			Dirty();
		}
	}

	private Overflow _overflow = Overflow.Visible;
	public Overflow Overflow
	{
		get => _overflow;
		set
		{
			if ( _overflow == value )
			{
				return;
			}

			_overflow = value;
			Dirty();
		}
	}

	private Display _display = Display.Flex;
	public Display Display
	{
		get => _display;
		set
		{
			if ( _display == value )
			{
				return;
			}

			bool wasContents = _display == Display.Contents;
			_display = value;
			_node?.OnChildDisplayChanged( wasContents, value == Display.Contents );
			_node?.OnStyleFlagsChanged();
			Dirty();
		}
	}

	private BoxSizing _boxSizing = BoxSizing.BorderBox;
	public BoxSizing BoxSizing
	{
		get => _boxSizing;
		set
		{
			if ( _boxSizing == value )
			{
				return;
			}

			_boxSizing = value;
			Dirty();
		}
	}

	// -----------------------------------------------------------------------------------------------
	// Numbers
	// -----------------------------------------------------------------------------------------------

	private float _flex = Num.Undefined;
	/// <summary>
	/// Positive fallback for an unset <see cref="FlexGrow"/> on non-root nodes. Does not affect
	/// <see cref="FlexShrink"/> or <see cref="FlexBasis"/>. Undefined (NaN) when unset.
	/// </summary>
	public float Flex
	{
		get => _flex;
		set
		{
			if ( Num.OptionalEquals( _flex, value ) )
			{
				return;
			}

			_flex = value;
			Dirty();
		}
	}

	private float _flexGrow = Num.Undefined;
	/// <summary>Undefined (NaN) when unset; resolved via <see cref="LayoutNode.ResolveFlexGrow"/>.</summary>
	public float FlexGrow
	{
		get => _flexGrow;
		set
		{
			if ( Num.OptionalEquals( _flexGrow, value ) )
			{
				return;
			}

			_flexGrow = value;
			Dirty();
		}
	}

	private float _flexShrink = Num.Undefined;
	/// <summary>Undefined (NaN) when unset; resolved via <see cref="LayoutNode.ResolveFlexShrink"/>.</summary>
	public float FlexShrink
	{
		get => _flexShrink;
		set
		{
			if ( Num.OptionalEquals( _flexShrink, value ) )
			{
				return;
			}

			_flexShrink = value;
			Dirty();
		}
	}

	private float _aspectRatio = Num.Undefined;
	/// <summary>Width / height. Undefined (NaN), zero or infinite all mean auto.</summary>
	public float AspectRatio
	{
		get => _aspectRatio;
		set
		{
			// degenerate aspect ratios act as auto: https://drafts.csswg.org/css-sizing-4/#valdef-aspect-ratio-ratio
			if ( value == 0.0f || float.IsInfinity( value ) )
			{
				value = Num.Undefined;
			}

			if ( Num.OptionalEquals( _aspectRatio, value ) )
			{
				return;
			}

			_aspectRatio = value;
			_node?.OnStyleFlagsChanged();
			Dirty();
		}
	}

	// -----------------------------------------------------------------------------------------------
	// Lengths
	// -----------------------------------------------------------------------------------------------

	private StyleLength _flexBasis;
	public StyleLength FlexBasis
	{
		get => _flexBasis;
		set
		{
			if ( _flexBasis == value )
			{
				return;
			}

			TrackPercent( _flexBasis, value );
			_flexBasis = value;
			Dirty();
		}
	}

	private EdgeValues _margin;
	private EdgeValues _position;
	private EdgeValues _padding;
	private EdgeValues _border;
	private GutterValues _gap;
	private DimensionValues _dimensions;
	private DimensionValues _minDimensions;
	private DimensionValues _maxDimensions;

	// Bit i is set when the entry for Edge i is defined. The edge resolution chains (ComputeEdge) test these
	// instead of loading each entry, and an all-clear mask lets the hot margin / padding / border sums return 0
	// without resolving anything - most nodes set none of them. Same idea for min/max: bit (axis) is the min,
	// bit (2 + axis) the max, so BoundAxis can skip the min/max resolution for the common unconstrained node.
	private uint _marginMask;
	private uint _positionMask;
	private uint _paddingMask;
	private uint _borderMask;
	private uint _minMaxMask;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static uint WithBit( uint mask, int bit, bool set )
	{
		return set ? mask | (1u << bit) : mask & ~(1u << bit);
	}

	public StyleLength GetMargin( Edge edge ) => _margin[(int)edge];

	public void SetMargin( Edge edge, StyleLength value )
	{
		if ( _margin[(int)edge] == value )
		{
			return;
		}

		TrackPercent( _margin[(int)edge], value );
		_margin[(int)edge] = value;
		_marginMask = WithBit( _marginMask, (int)edge, value.IsDefined );
		Dirty();
	}

	public StyleLength GetPosition( Edge edge ) => _position[(int)edge];

	public void SetPosition( Edge edge, StyleLength value )
	{
		if ( _position[(int)edge] == value )
		{
			return;
		}

		_position[(int)edge] = value;
		_positionMask = WithBit( _positionMask, (int)edge, value.IsDefined );
		Dirty();
	}

	public StyleLength GetPadding( Edge edge ) => _padding[(int)edge];

	public void SetPadding( Edge edge, StyleLength value )
	{
		if ( _padding[(int)edge] == value )
		{
			return;
		}

		TrackPercent( _padding[(int)edge], value );
		_padding[(int)edge] = value;
		_paddingMask = WithBit( _paddingMask, (int)edge, value.IsDefined );
		Dirty();
	}

	public StyleLength GetBorder( Edge edge ) => _border[(int)edge];

	public void SetBorder( Edge edge, StyleLength value )
	{
		if ( _border[(int)edge] == value )
		{
			return;
		}

		TrackPercent( _border[(int)edge], value );
		_border[(int)edge] = value;
		_borderMask = WithBit( _borderMask, (int)edge, value.IsDefined );
		Dirty();
	}

	public StyleLength GetGap( Gutter gutter ) => _gap[(int)gutter];

	public void SetGap( Gutter gutter, StyleLength value )
	{
		if ( _gap[(int)gutter] == value )
		{
			return;
		}

		TrackPercent( _gap[(int)gutter], value );
		_gap[(int)gutter] = value;
		Dirty();
	}

	public StyleLength GetDimension( Dimension axis ) => _dimensions[(int)axis];

	public void SetDimension( Dimension axis, StyleLength value )
	{
		if ( _dimensions[(int)axis] == value )
		{
			return;
		}

		TrackPercent( _dimensions[(int)axis], value );
		_dimensions[(int)axis] = value;
		Dirty();
	}

	public StyleLength GetMinDimension( Dimension axis ) => _minDimensions[(int)axis];

	public void SetMinDimension( Dimension axis, StyleLength value )
	{
		if ( _minDimensions[(int)axis] == value )
		{
			return;
		}

		TrackPercent( _minDimensions[(int)axis], value );
		_minDimensions[(int)axis] = value;
		_minMaxMask = WithBit( _minMaxMask, (int)axis, value.IsDefined );
		Dirty();
	}

	public StyleLength GetMaxDimension( Dimension axis ) => _maxDimensions[(int)axis];

	public void SetMaxDimension( Dimension axis, StyleLength value )
	{
		if ( _maxDimensions[(int)axis] == value )
		{
			return;
		}

		TrackPercent( _maxDimensions[(int)axis], value );
		_maxDimensions[(int)axis] = value;
		_minMaxMask = WithBit( _minMaxMask, DimensionValues.Count + (int)axis, value.IsDefined );
		_node?.OnStyleFlagsChanged();
		Dirty();
	}

	/// <summary>False when neither min nor max is set on the axis, so bounding to them is the identity.</summary>
	internal bool HasMinOrMaxDimension( Dimension axis ) => (_minMaxMask & (0b101u << (int)axis)) != 0;

	/// <summary>False when no padding or border edge is set, so every padding + border sum is 0.</summary>
	internal bool HasPaddingOrBorder => (_paddingMask | _borderMask) != 0;

	public StyleLength Width
	{
		get => GetDimension( Dimension.Width );
		set => SetDimension( Dimension.Width, value );
	}

	public StyleLength Height
	{
		get => GetDimension( Dimension.Height );
		set => SetDimension( Dimension.Height, value );
	}

	public StyleLength MinWidth
	{
		get => GetMinDimension( Dimension.Width );
		set => SetMinDimension( Dimension.Width, value );
	}

	public StyleLength MinHeight
	{
		get => GetMinDimension( Dimension.Height );
		set => SetMinDimension( Dimension.Height, value );
	}

	public StyleLength MaxWidth
	{
		get => GetMaxDimension( Dimension.Width );
		set => SetMaxDimension( Dimension.Width, value );
	}

	public StyleLength MaxHeight
	{
		get => GetMaxDimension( Dimension.Height );
		set => SetMaxDimension( Dimension.Height, value );
	}

	// -----------------------------------------------------------------------------------------------
	// Grid
	// -----------------------------------------------------------------------------------------------

	/// <summary>
	/// The grid-only properties. Few nodes are grid containers or set a grid placement, so these live in a
	/// side object created on the first non-default assignment; the getters return the defaults while it is absent.
	/// </summary>
	private sealed class GridStyle
	{
		public TrackList TemplateColumns = TrackList.None;
		public TrackList TemplateRows = TrackList.None;
		public TrackSizingFunction[] AutoColumns = Array.Empty<TrackSizingFunction>();
		public TrackSizingFunction[] AutoRows = Array.Empty<TrackSizingFunction>();
		public GridAutoFlow AutoFlow = GridAutoFlow.Row;
		public GridPlacement ColumnStart = GridPlacement.Auto;
		public GridPlacement ColumnEnd = GridPlacement.Auto;
		public GridPlacement RowStart = GridPlacement.Auto;
		public GridPlacement RowEnd = GridPlacement.Auto;
		public Align JustifyItems = Align.Auto;
		public Align JustifySelf = Align.Auto;
	}

	private GridStyle _grid;

	private GridStyle Grid() => _grid ??= new GridStyle();

	public TrackList GridTemplateColumns
	{
		get => _grid?.TemplateColumns ?? TrackList.None;
		set
		{
			value ??= TrackList.None;
			if ( GridTemplateColumns.Equals( value ) )
			{
				return;
			}

			Grid().TemplateColumns = value;
			Dirty();
		}
	}

	public TrackList GridTemplateRows
	{
		get => _grid?.TemplateRows ?? TrackList.None;
		set
		{
			value ??= TrackList.None;
			if ( GridTemplateRows.Equals( value ) )
			{
				return;
			}

			Grid().TemplateRows = value;
			Dirty();
		}
	}

	/// <summary>Sizing of implicitly created columns. Empty means <c>auto</c>.</summary>
	public TrackSizingFunction[] GridAutoColumns
	{
		get => CopyTracks( GridAutoColumnItems );
		set
		{
			value ??= Array.Empty<TrackSizingFunction>();
			if ( TracksEqual( GridAutoColumnItems, value ) )
			{
				return;
			}

			Grid().AutoColumns = CopyTracks( value );
			Dirty();
		}
	}
	internal TrackSizingFunction[] GridAutoColumnItems => _grid?.AutoColumns ?? Array.Empty<TrackSizingFunction>();

	/// <summary>Sizing of implicitly created rows. Empty means <c>auto</c>.</summary>
	public TrackSizingFunction[] GridAutoRows
	{
		get => CopyTracks( GridAutoRowItems );
		set
		{
			value ??= Array.Empty<TrackSizingFunction>();
			if ( TracksEqual( GridAutoRowItems, value ) )
			{
				return;
			}

			Grid().AutoRows = CopyTracks( value );
			Dirty();
		}
	}
	internal TrackSizingFunction[] GridAutoRowItems => _grid?.AutoRows ?? Array.Empty<TrackSizingFunction>();

	public GridAutoFlow GridAutoFlow
	{
		get => _grid?.AutoFlow ?? GridAutoFlow.Row;
		set
		{
			if ( GridAutoFlow == value )
			{
				return;
			}

			Grid().AutoFlow = value;
			Dirty();
		}
	}

	public GridPlacement GridColumnStart
	{
		get => _grid?.ColumnStart ?? GridPlacement.Auto;
		set
		{
			if ( GridColumnStart == value )
			{
				return;
			}

			Grid().ColumnStart = value;
			Dirty();
		}
	}

	public GridPlacement GridColumnEnd
	{
		get => _grid?.ColumnEnd ?? GridPlacement.Auto;
		set
		{
			if ( GridColumnEnd == value )
			{
				return;
			}

			Grid().ColumnEnd = value;
			Dirty();
		}
	}

	public GridPlacement GridRowStart
	{
		get => _grid?.RowStart ?? GridPlacement.Auto;
		set
		{
			if ( GridRowStart == value )
			{
				return;
			}

			Grid().RowStart = value;
			Dirty();
		}
	}

	public GridPlacement GridRowEnd
	{
		get => _grid?.RowEnd ?? GridPlacement.Auto;
		set
		{
			if ( GridRowEnd == value )
			{
				return;
			}

			Grid().RowEnd = value;
			Dirty();
		}
	}

	/// <summary>Grid: default inline-axis alignment of items. <see cref="Align.Auto"/> behaves as stretch.</summary>
	public Align JustifyItems
	{
		get => _grid?.JustifyItems ?? Align.Auto;
		set
		{
			if ( JustifyItems == value )
			{
				return;
			}

			Grid().JustifyItems = value;
			_node?.OnStyleFlagsChanged();
			Dirty();
		}
	}

	/// <summary>Grid: inline-axis alignment of this item. <see cref="Align.Auto"/> takes the parent's <see cref="JustifyItems"/>.</summary>
	public Align JustifySelf
	{
		get => _grid?.JustifySelf ?? Align.Auto;
		set
		{
			if ( JustifySelf == value )
			{
				return;
			}

			Grid().JustifySelf = value;
			_node?.OnStyleFlagsChanged();
			Dirty();
		}
	}

	private static bool TracksEqual( TrackSizingFunction[] a, TrackSizingFunction[] b )
	{
		if ( ReferenceEquals( a, b ) )
		{
			return true;
		}

		if ( a.Length != b.Length )
		{
			return false;
		}

		for ( int i = 0; i < a.Length; i++ )
		{
			if ( !a[i].Equals( b[i] ) )
			{
				return false;
			}
		}

		return true;
	}

	private static TrackSizingFunction[] CopyTracks( TrackSizingFunction[] tracks )
	{
		return tracks.Length == 0 ? Array.Empty<TrackSizingFunction>() : (TrackSizingFunction[])tracks.Clone();
	}

	// -----------------------------------------------------------------------------------------------
	// Resolution helpers
	// -----------------------------------------------------------------------------------------------

	internal float ResolvedMinDimension( Direction direction, Dimension axis, float referenceLength, float ownerWidth )
	{
		float value = GetMinDimension( axis ).Resolve( referenceLength );
		// Undefined stays undefined whatever the padding, so only content-box with a value needs the sum.
		if ( _boxSizing == BoxSizing.BorderBox || Num.IsUndefined( value ) )
		{
			return value;
		}

		float paddingAndBorder = ComputePaddingAndBorderForDimension( direction, axis, ownerWidth );
		return value + (Num.IsDefined( paddingAndBorder ) ? paddingAndBorder : 0.0f);
	}

	internal float ResolvedMaxDimension( Direction direction, Dimension axis, float referenceLength, float ownerWidth )
	{
		float value = GetMaxDimension( axis ).Resolve( referenceLength );
		if ( _boxSizing == BoxSizing.BorderBox || Num.IsUndefined( value ) )
		{
			return value;
		}

		float paddingAndBorder = ComputePaddingAndBorderForDimension( direction, axis, ownerWidth );
		return value + (Num.IsDefined( paddingAndBorder ) ? paddingAndBorder : 0.0f);
	}

	internal bool HorizontalInsetsDefined => _position[(int)Edge.Left].IsDefined
		|| _position[(int)Edge.Right].IsDefined
		|| _position[(int)Edge.All].IsDefined
		|| _position[(int)Edge.Horizontal].IsDefined
		|| _position[(int)Edge.Start].IsDefined
		|| _position[(int)Edge.End].IsDefined;

	internal bool VerticalInsetsDefined => _position[(int)Edge.Top].IsDefined
		|| _position[(int)Edge.Bottom].IsDefined
		|| _position[(int)Edge.All].IsDefined
		|| _position[(int)Edge.Vertical].IsDefined;

	internal bool IsFlexStartPositionDefined( FlexDirection axis, Direction direction ) => ComputePosition( Axis.FlexStartEdge( axis ), direction ).IsDefined;
	internal bool IsFlexStartPositionAuto( FlexDirection axis, Direction direction ) => ComputePosition( Axis.FlexStartEdge( axis ), direction ).IsAuto;
	internal bool IsInlineStartPositionDefined( FlexDirection axis, Direction direction ) => ComputePosition( Axis.InlineStartEdge( axis, direction ), direction ).IsDefined;
	internal bool IsInlineStartPositionAuto( FlexDirection axis, Direction direction ) => ComputePosition( Axis.InlineStartEdge( axis, direction ), direction ).IsAuto;
	internal bool IsFlexEndPositionDefined( FlexDirection axis, Direction direction ) => ComputePosition( Axis.FlexEndEdge( axis ), direction ).IsDefined;
	internal bool IsFlexEndPositionAuto( FlexDirection axis, Direction direction ) => ComputePosition( Axis.FlexEndEdge( axis ), direction ).IsAuto;
	internal bool IsInlineEndPositionDefined( FlexDirection axis, Direction direction ) => ComputePosition( Axis.InlineEndEdge( axis, direction ), direction ).IsDefined;
	internal bool IsInlineEndPositionAuto( FlexDirection axis, Direction direction ) => ComputePosition( Axis.InlineEndEdge( axis, direction ), direction ).IsAuto;

	internal float ComputeFlexStartPosition( FlexDirection axis, Direction direction, float axisSize ) => Num.UnwrapOrDefault( ComputePosition( Axis.FlexStartEdge( axis ), direction ).Resolve( axisSize ), 0.0f );
	internal float ComputeInlineStartPosition( FlexDirection axis, Direction direction, float axisSize ) => Num.UnwrapOrDefault( ComputePosition( Axis.InlineStartEdge( axis, direction ), direction ).Resolve( axisSize ), 0.0f );
	internal float ComputeFlexEndPosition( FlexDirection axis, Direction direction, float axisSize ) => Num.UnwrapOrDefault( ComputePosition( Axis.FlexEndEdge( axis ), direction ).Resolve( axisSize ), 0.0f );
	internal float ComputeInlineEndPosition( FlexDirection axis, Direction direction, float axisSize ) => Num.UnwrapOrDefault( ComputePosition( Axis.InlineEndEdge( axis, direction ), direction ).Resolve( axisSize ), 0.0f );

	internal float ComputeFlexStartMargin( FlexDirection axis, Direction direction, float widthSize ) => Num.UnwrapOrDefault( ComputeMargin( Axis.FlexStartEdge( axis ), direction ).Resolve( widthSize ), 0.0f );
	internal float ComputeInlineStartMargin( FlexDirection axis, Direction direction, float widthSize ) => Num.UnwrapOrDefault( ComputeMargin( Axis.InlineStartEdge( axis, direction ), direction ).Resolve( widthSize ), 0.0f );
	internal float ComputeFlexEndMargin( FlexDirection axis, Direction direction, float widthSize ) => Num.UnwrapOrDefault( ComputeMargin( Axis.FlexEndEdge( axis ), direction ).Resolve( widthSize ), 0.0f );
	internal float ComputeInlineEndMargin( FlexDirection axis, Direction direction, float widthSize ) => Num.UnwrapOrDefault( ComputeMargin( Axis.InlineEndEdge( axis, direction ), direction ).Resolve( widthSize ), 0.0f );

	internal float ComputeFlexStartBorder( FlexDirection axis, Direction direction ) => Num.MaxOrDefined( ComputeBorder( Axis.FlexStartEdge( axis ), direction ).Resolve( 0.0f ), 0.0f );
	internal float ComputeInlineStartBorder( FlexDirection axis, Direction direction ) => Num.MaxOrDefined( ComputeBorder( Axis.InlineStartEdge( axis, direction ), direction ).Resolve( 0.0f ), 0.0f );
	internal float ComputeFlexEndBorder( FlexDirection axis, Direction direction ) => Num.MaxOrDefined( ComputeBorder( Axis.FlexEndEdge( axis ), direction ).Resolve( 0.0f ), 0.0f );
	internal float ComputeInlineEndBorder( FlexDirection axis, Direction direction ) => Num.MaxOrDefined( ComputeBorder( Axis.InlineEndEdge( axis, direction ), direction ).Resolve( 0.0f ), 0.0f );

	internal float ComputeFlexStartPadding( FlexDirection axis, Direction direction, float widthSize ) => Num.MaxOrDefined( ComputePadding( Axis.FlexStartEdge( axis ), direction ).Resolve( widthSize ), 0.0f );
	internal float ComputeInlineStartPadding( FlexDirection axis, Direction direction, float widthSize ) => Num.MaxOrDefined( ComputePadding( Axis.InlineStartEdge( axis, direction ), direction ).Resolve( widthSize ), 0.0f );
	internal float ComputeFlexEndPadding( FlexDirection axis, Direction direction, float widthSize ) => Num.MaxOrDefined( ComputePadding( Axis.FlexEndEdge( axis ), direction ).Resolve( widthSize ), 0.0f );
	internal float ComputeInlineEndPadding( FlexDirection axis, Direction direction, float widthSize ) => Num.MaxOrDefined( ComputePadding( Axis.InlineEndEdge( axis, direction ), direction ).Resolve( widthSize ), 0.0f );

	// Undefined padding and border resolve to 0, so a node with none set has a 0 sum on every edge.
	internal float ComputeInlineStartPaddingAndBorder( FlexDirection axis, Direction direction, float widthSize ) => HasPaddingOrBorder ? ComputeInlineStartPadding( axis, direction, widthSize ) + ComputeInlineStartBorder( axis, direction ) : 0.0f;
	internal float ComputeFlexStartPaddingAndBorder( FlexDirection axis, Direction direction, float widthSize ) => HasPaddingOrBorder ? ComputeFlexStartPadding( axis, direction, widthSize ) + ComputeFlexStartBorder( axis, direction ) : 0.0f;
	internal float ComputeInlineEndPaddingAndBorder( FlexDirection axis, Direction direction, float widthSize ) => HasPaddingOrBorder ? ComputeInlineEndPadding( axis, direction, widthSize ) + ComputeInlineEndBorder( axis, direction ) : 0.0f;
	internal float ComputeFlexEndPaddingAndBorder( FlexDirection axis, Direction direction, float widthSize ) => HasPaddingOrBorder ? ComputeFlexEndPadding( axis, direction, widthSize ) + ComputeFlexEndBorder( axis, direction ) : 0.0f;

	internal float ComputePaddingAndBorderForDimension( Direction direction, Dimension dimension, float widthSize )
	{
		if ( !HasPaddingOrBorder )
		{
			return 0.0f;
		}

		FlexDirection flexDirectionForDimension = dimension == Dimension.Width ? FlexDirection.Row : FlexDirection.Column;
		return ComputeFlexStartPaddingAndBorder( flexDirectionForDimension, direction, widthSize )
			+ ComputeFlexEndPaddingAndBorder( flexDirectionForDimension, direction, widthSize );
	}

	internal float ComputeBorderForAxis( FlexDirection axis ) => _borderMask == 0
		? 0.0f
		: ComputeInlineStartBorder( axis, Direction.LTR ) + ComputeInlineEndBorder( axis, Direction.LTR );

	/// <summary>Total margin along an axis. Direction-independent, so LTR is hardcoded. Undefined margins are 0.</summary>
	internal float ComputeMarginForAxis( FlexDirection axis, float widthSize ) => _marginMask == 0
		? 0.0f
		: ComputeInlineStartMargin( axis, Direction.LTR, widthSize )
			+ ComputeInlineEndMargin( axis, Direction.LTR, widthSize );

	internal float ComputeGapForAxis( FlexDirection axis, float ownerSize )
	{
		StyleLength gap = Axis.IsRow( axis ) ? ComputeColumnGap() : ComputeRowGap();
		return Num.MaxOrDefined( gap.Resolve( ownerSize ), 0.0f );
	}

	internal bool FlexStartMarginIsAuto( FlexDirection axis, Direction direction ) => _marginMask != 0 && ComputeMargin( Axis.FlexStartEdge( axis ), direction ).IsAuto;
	internal bool FlexEndMarginIsAuto( FlexDirection axis, Direction direction ) => _marginMask != 0 && ComputeMargin( Axis.FlexEndEdge( axis ), direction ).IsAuto;

	internal StyleLength ComputeColumnGap() => _gap[(int)Gutter.Column].IsDefined ? _gap[(int)Gutter.Column] : _gap[(int)Gutter.All];
	internal StyleLength ComputeRowGap() => _gap[(int)Gutter.Row].IsDefined ? _gap[(int)Gutter.Row] : _gap[(int)Gutter.All];

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool Has( uint mask, Edge edge ) => (mask & (1u << (int)edge)) != 0;

	private static StyleLength ComputeLeftEdge( ref EdgeValues edges, uint mask, Direction layoutDirection )
	{
		if ( layoutDirection == Direction.LTR && Has( mask, Edge.Start ) )
		{
			return edges[(int)Edge.Start];
		}

		if ( layoutDirection == Direction.RTL && Has( mask, Edge.End ) )
		{
			return edges[(int)Edge.End];
		}

		if ( Has( mask, Edge.Left ) )
		{
			return edges[(int)Edge.Left];
		}

		if ( Has( mask, Edge.Horizontal ) )
		{
			return edges[(int)Edge.Horizontal];
		}

		return edges[(int)Edge.All];
	}

	private static StyleLength ComputeTopEdge( ref EdgeValues edges, uint mask )
	{
		if ( Has( mask, Edge.Top ) )
		{
			return edges[(int)Edge.Top];
		}

		if ( Has( mask, Edge.Vertical ) )
		{
			return edges[(int)Edge.Vertical];
		}

		return edges[(int)Edge.All];
	}

	private static StyleLength ComputeRightEdge( ref EdgeValues edges, uint mask, Direction layoutDirection )
	{
		if ( layoutDirection == Direction.LTR && Has( mask, Edge.End ) )
		{
			return edges[(int)Edge.End];
		}

		if ( layoutDirection == Direction.RTL && Has( mask, Edge.Start ) )
		{
			return edges[(int)Edge.Start];
		}

		if ( Has( mask, Edge.Right ) )
		{
			return edges[(int)Edge.Right];
		}

		if ( Has( mask, Edge.Horizontal ) )
		{
			return edges[(int)Edge.Horizontal];
		}

		return edges[(int)Edge.All];
	}

	private static StyleLength ComputeBottomEdge( ref EdgeValues edges, uint mask )
	{
		if ( Has( mask, Edge.Bottom ) )
		{
			return edges[(int)Edge.Bottom];
		}

		if ( Has( mask, Edge.Vertical ) )
		{
			return edges[(int)Edge.Vertical];
		}

		return edges[(int)Edge.All];
	}

	/// <summary>The value that applies to a physical edge: the most specific defined entry, per the mask of defined entries.</summary>
	private static StyleLength ComputeEdge( ref EdgeValues edges, uint mask, PhysicalEdge edge, Direction direction )
	{
		if ( mask == 0 )
		{
			return StyleLength.Undefined;
		}

		return edge switch
		{
			PhysicalEdge.Left => ComputeLeftEdge( ref edges, mask, direction ),
			PhysicalEdge.Top => ComputeTopEdge( ref edges, mask ),
			PhysicalEdge.Right => ComputeRightEdge( ref edges, mask, direction ),
			_ => ComputeBottomEdge( ref edges, mask ),
		};
	}

	internal StyleLength ComputePosition( PhysicalEdge edge, Direction direction ) => ComputeEdge( ref _position, _positionMask, edge, direction );
	internal StyleLength ComputeMargin( PhysicalEdge edge, Direction direction ) => ComputeEdge( ref _margin, _marginMask, edge, direction );
	internal StyleLength ComputePadding( PhysicalEdge edge, Direction direction ) => ComputeEdge( ref _padding, _paddingMask, edge, direction );
	internal StyleLength ComputeBorder( PhysicalEdge edge, Direction direction ) => ComputeEdge( ref _border, _borderMask, edge, direction );

	/// <summary>
	/// Copy all values from another style. Marks dirty if anything differed.
	/// </summary>
	public void CopyFrom( LayoutStyle other )
	{
		Direction = other._direction;
		FlexDirection = other._flexDirection;
		JustifyContent = other._justifyContent;
		AlignContent = other._alignContent;
		AlignItems = other._alignItems;
		AlignSelf = other._alignSelf;
		PositionType = other._positionType;
		FlexWrap = other._flexWrap;
		Overflow = other._overflow;
		Display = other._display;
		BoxSizing = other._boxSizing;
		Flex = other._flex;
		FlexGrow = other._flexGrow;
		FlexShrink = other._flexShrink;
		FlexBasis = other._flexBasis;
		AspectRatio = other._aspectRatio;
		for ( int i = 0; i < EdgeValues.Count; i++ )
		{
			SetMargin( (Edge)i, other._margin[i] );
			SetPosition( (Edge)i, other._position[i] );
			SetPadding( (Edge)i, other._padding[i] );
			SetBorder( (Edge)i, other._border[i] );
		}
		for ( int i = 0; i < GutterValues.Count; i++ )
		{
			SetGap( (Gutter)i, other._gap[i] );
		}

		for ( int i = 0; i < DimensionValues.Count; i++ )
		{
			SetDimension( (Dimension)i, other._dimensions[i] );
			SetMinDimension( (Dimension)i, other._minDimensions[i] );
			SetMaxDimension( (Dimension)i, other._maxDimensions[i] );
		}
		// The setters no-op on defaults, so copying from a style without grid properties allocates nothing.
		GridTemplateColumns = other.GridTemplateColumns;
		GridTemplateRows = other.GridTemplateRows;
		GridAutoColumns = other.GridAutoColumnItems;
		GridAutoRows = other.GridAutoRowItems;
		GridAutoFlow = other.GridAutoFlow;
		GridColumnStart = other.GridColumnStart;
		GridColumnEnd = other.GridColumnEnd;
		GridRowStart = other.GridRowStart;
		GridRowEnd = other.GridRowEnd;
		JustifyItems = other.JustifyItems;
		JustifySelf = other.JustifySelf;
	}
}
