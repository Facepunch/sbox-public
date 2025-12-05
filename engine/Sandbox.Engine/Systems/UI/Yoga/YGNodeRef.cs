using System.Runtime.InteropServices;

internal struct YGNodeRef
{
	IntPtr _ptr;

	public YGNodeRef()
	{
		_ptr = default;
	}

	public readonly bool IsValid => _ptr != IntPtr.Zero;
}


internal struct YGConfigRef
{
	IntPtr _ptr;

	public YGConfigRef()
	{
		_ptr = default;
	}

	public readonly bool IsValid => _ptr != IntPtr.Zero;
}

internal unsafe struct YGMeasureFunc
{
	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	internal delegate Vector2 Delegate( YGNodeRef node, float width, YGMeasureMode widthMode, float height, YGMeasureMode heightMode );

	public IntPtr _ptr;
}


internal enum YGDimension
{
	YGDimensionWidth,
	YGDimensionHeight
}

internal enum YGDirection
{
	YGDirectionInherit,
	YGDirectionLTR,
	YGDirectionRTL
}


internal enum YGEdge
{
	YGEdgeLeft,
	YGEdgeTop,
	YGEdgeRight,
	YGEdgeBottom,
	YGEdgeStart,
	YGEdgeEnd,
	YGEdgeHorizontal,
	YGEdgeVertical,
	YGEdgeAll
}

internal enum YGErrata
{
	YGErrataNone = 0,
	YGErrataStretchFlexBasis = 1,
	YGErrataAbsolutePositioningIncorrect = 2,
	YGErrataAbsolutePercentAgainstInnerSize = 4,
	YGErrataAll = 2147483647,
	YGErrataClassic = 2147483646
}

[Flags]
internal enum YGExperimentalFeature
{
	YGExperimentalFeatureWebFlexBasis
}

internal enum YGGutter
{
	YGGutterColumn,
	YGGutterRow,
	YGGutterAll
}

internal enum YGMeasureMode
{
	YGMeasureModeUndefined,
	YGMeasureModeExactly,
	YGMeasureModeAtMost
}

internal enum YGNodeType
{
	YGNodeTypeDefault,
	YGNodeTypeText
}


public enum YGUnit
{
	YGUnitUndefined,
	YGUnitPoint,
	YGUnitPercent,
	YGUnitAuto
}


public struct YGValue
{
	public float value;
	public YGUnit unit;
}
