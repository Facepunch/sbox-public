namespace Sandbox.Layout;

/// <summary>
/// Inline (text) direction of a node. <see cref="Inherit"/> takes the owner's direction.
/// </summary>
internal enum Direction : byte
{
	Inherit,
	LTR,
	RTL,
}

/// <summary>
/// Main axis of a flex container.
/// </summary>
internal enum FlexDirection : byte
{
	Column,
	ColumnReverse,
	Row,
	RowReverse,
}

/// <summary>
/// Distribution of items along the main axis (flex) or of tracks along an axis (grid).
/// </summary>
internal enum Justify : byte
{
	FlexStart,
	Center,
	FlexEnd,
	SpaceBetween,
	SpaceAround,
	SpaceEvenly,

	/// <summary>Grid only: stretch auto tracks to fill the container.</summary>
	Stretch,
}

/// <summary>
/// Alignment of items along the cross axis (flex), or of items / tracks along either axis (grid).
/// </summary>
internal enum Align : byte
{
	Auto,
	FlexStart,
	Center,
	FlexEnd,
	Stretch,
	Baseline,
	SpaceBetween,
	SpaceAround,
	SpaceEvenly,
}

internal enum PositionType : byte
{
	Static,
	Relative,
	Absolute,
	Fixed = 3,
}

internal enum Wrap : byte
{
	NoWrap,
	Wrap,
	WrapReverse,
}

internal enum Overflow : byte
{
	Visible,
	Hidden,
	Scroll,
}

/// <summary>
/// The layout algorithm used to lay out a node's children.
/// </summary>
internal enum Display : byte
{
	Flex,
	None,
	Contents,
	Block,
	Grid,
	Inline,
}

internal enum BoxSizing : byte
{
	BorderBox,
	ContentBox,
}

/// <summary>
/// Edges a style value can be set on. The first four are physical; the rest are shorthands
/// resolved at layout time (Start/End depend on <see cref="Direction"/>).
/// </summary>
internal enum Edge : byte
{
	Left,
	Top,
	Right,
	Bottom,
	Start,
	End,
	Horizontal,
	Vertical,
	All,
}

/// <summary>
/// The four physical edges of a box; also the layout output edge indices.
/// </summary>
internal enum PhysicalEdge : byte
{
	Left = Edge.Left,
	Top = Edge.Top,
	Right = Edge.Right,
	Bottom = Edge.Bottom,
}

internal enum Gutter : byte
{
	Column,
	Row,
	All,
}

internal enum Dimension : byte
{
	Width,
	Height,
}

internal enum Unit : byte
{
	Undefined,
	Point,
	Percent,
	Auto,
}

/// <summary>
/// Constraint passed to a <see cref="LayoutNode.MeasureFunc"/> for each axis.
/// </summary>
internal enum MeasureMode : byte
{
	/// <summary>No constraint: return the node's max-content size.</summary>
	Undefined,

	/// <summary>The node will be exactly this size in this axis.</summary>
	Exactly,

	/// <summary>The node may be at most this size in this axis.</summary>
	AtMost,

	/// <summary>
	/// Return the node's min-content size (for text: the longest unbreakable word). Intrinsic sizing
	/// requests propagate through grid, flex and block containers. Implementations that can't compute
	/// min-content should treat this like <see cref="Undefined"/>.
	/// </summary>
	MinContent,
}

/// <summary>
/// Controls how items that aren't explicitly placed are auto-placed in a grid.
/// </summary>
internal enum GridAutoFlow : byte
{
	Row,
	Column,
	RowDense,
	ColumnDense,
}

/// <summary>
/// Internal sizing constraint - maps to <see cref="MeasureMode"/> at the measure-function boundary.
/// </summary>
internal enum SizingMode : byte
{
	/// <summary>Outer size fills the available space exactly.</summary>
	StretchFit,

	/// <summary>Ideal size given infinite space.</summary>
	MaxContent,

	/// <summary>Clamp( min-content, stretch-fit, max-content ).</summary>
	FitContent,

	/// <summary>Smallest size that fits the content, propagated through intrinsic sizing passes.</summary>
	MinContent,
}

/// <summary>
/// Which measured dimensions a measure-only pass has to produce. A flex container asked for its main-axis
/// size alone (the flex-basis measurement) can skip sizing its children on the cross axis; the other
/// dimension is left undefined unless it was stretch-fit and therefore free. See
/// <see cref="LayoutAlgorithm.CalculateLayoutInternal"/>.
/// </summary>
internal enum MeasureScope : byte
{
	Both,
	Width,
	Height,
}
