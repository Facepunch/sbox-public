using System;
using Sandbox.Layout;

namespace LayoutTests;

/// <summary>Observes raw layout in upstream fixture coordinates without changing layout results or caches.</summary>
internal static class FixtureGeometry
{
	internal static (float Left, float Top, float Width, float Height) GetRect( LayoutNode node, bool round )
	{
		if ( !round )
			return (node.LayoutLeft, node.LayoutTop, node.LayoutWidth, node.LayoutHeight);

		var (absoluteLeft, absoluteTop) = GetAbsolutePosition( node );
		bool text = node.HasMeasureFunc;
		double width = node.LayoutWidth;
		double height = node.LayoutHeight;
		bool fractionalWidth = Math.Abs( width % 1 ) >= 0.0001 && Math.Abs( width % 1 - 1 ) >= 0.0001;
		bool fractionalHeight = Math.Abs( height % 1 ) >= 0.0001 && Math.Abs( height % 1 - 1 ) >= 0.0001;

		// Upstream rounds local positions, but derives sizes from absolute, unrounded edges.
		// Measured text floors its start and ceils fractional sizes to avoid truncation.
		return (
			Round( node.LayoutLeft, false, text ),
			Round( node.LayoutTop, false, text ),
			Round( absoluteLeft + width, text && fractionalWidth, text && !fractionalWidth ) - Round( absoluteLeft, false, text ),
			Round( absoluteTop + height, text && fractionalHeight, text && !fractionalHeight ) - Round( absoluteTop, false, text ));
	}

	private static (double Left, double Top) GetAbsolutePosition( LayoutNode node )
	{
		if ( node.Owner is null || node.Style.PositionType == PositionType.Fixed )
			return (node.LayoutLeft, node.LayoutTop);

		var (left, top) = GetAbsolutePosition( node.Owner );
		return (left + node.LayoutLeft, top + node.LayoutTop);
	}

	// Source fixture rounding convention, including its near-integer/half epsilon. Not used by production.
	private static float Round( double value, bool forceCeil, bool forceFloor )
	{
		double fractional = value - Math.Truncate( value );
		if ( fractional < 0 ) fractional++;

		if ( Math.Abs( fractional ) < 0.0001 )
			value -= fractional;
		else if ( Math.Abs( fractional - 1 ) < 0.0001 || forceCeil )
			value = value - fractional + 1;
		else if ( forceFloor )
			value -= fractional;
		else
			value = value - fractional + (fractional > 0.5 || Math.Abs( fractional - 0.5 ) < 0.0001 ? 1 : 0);

		return (float)value;
	}
}
