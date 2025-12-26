using Sandbox.Engine;
using Sandbox.Internal;

namespace Sandbox.UI;

internal static class TooltipSystem
{
	static IPanel lastHovered;
	static IPanel lastTooltip;

	internal static void SetHovered( IPanel current )
	{
		if ( lastHovered == current )
			return;

		if ( current == null )
		{
			lastHovered = null;
			lastTooltip?.Delete( false );
			lastTooltip = null;
			return;
		}

		if ( !current.HasTooltip )
		{
			SetHovered( current.Parent );
			return;
		}

		lastHovered = current;

		if ( current != null )
		{
			lastTooltip?.Delete( false );
			lastTooltip = current.CreateTooltip();
			Frame();
		}
	}

	internal static void Frame()
	{
		if ( !lastTooltip.IsValid() )
			return;

		if ( !InputRouter.MouseCursorVisible )
		{
			lastTooltip?.Delete( false );
			lastTooltip = null;
			lastHovered = null;
			return;
		}

		if ( lastHovered.IsValid() )
		{
			lastHovered.UpdateTooltip( lastTooltip );
		}

		//
		// Position the tooltip relative to the mouse cursor,
		// respecting screen boundaries with a 20px margin
		//

		if ( lastTooltip is not Panel tooltipPanel )
			return;

		var pos = InputRouter.MouseCursorPosition;
		var tooltipSize = lastTooltip.OuterRect;
		var screenSize = Screen.Size;
		const float margin = 20f;
		const float offset = 20f;

		// Use tooltip size if available, otherwise use a default estimate
		var tooltipWidth = tooltipSize.Width > 0 ? tooltipSize.Width : 200f;
		var tooltipHeight = tooltipSize.Height > 0 ? tooltipSize.Height : 50f;

		var scaleFromScreen = tooltipPanel.ScaleFromScreen;

		// Reset all position properties
		tooltipPanel.Style.Left = null;
		tooltipPanel.Style.Right = null;
		tooltipPanel.Style.Top = null;
		tooltipPanel.Style.Bottom = null;

		// Calculate horizontal position
		// Try to place tooltip to the right of cursor
		float leftPos = pos.x + offset;

		if ( leftPos + tooltipWidth + margin > screenSize.x )
		{
			// Doesn't fit on right, try left
			float rightPos = screenSize.x - (pos.x - offset);

			if ( pos.x - offset - tooltipWidth - margin >= 0 )
			{
				// Fits on left
				tooltipPanel.Style.Right = rightPos * scaleFromScreen;
			}
			else
			{
				// Doesn't fit on either side, clamp to right edge
				leftPos = screenSize.x - tooltipWidth - margin;
				tooltipPanel.Style.Left = leftPos * scaleFromScreen;
			}
		}
		else
		{
			// Fits on right
			tooltipPanel.Style.Left = leftPos * scaleFromScreen;
		}

		// Calculate vertical position
		// Try to place tooltip above cursor
		float bottomPos = screenSize.y - (pos.y - offset);

		if ( pos.y - offset - tooltipHeight - margin < 0 )
		{
			// Doesn't fit above, try below
			float topPos = pos.y + offset;

			if ( topPos + tooltipHeight + margin <= screenSize.y )
			{
				// Fits below
				tooltipPanel.Style.Top = topPos * scaleFromScreen;
			}
			else
			{
				// Doesn't fit on either side, clamp to bottom edge
				topPos = screenSize.y - tooltipHeight - margin;
				tooltipPanel.Style.Top = topPos * scaleFromScreen;
			}
		}
		else
		{
			// Fits above
			tooltipPanel.Style.Bottom = bottomPos * scaleFromScreen;
		}

	}
}
