using Sandbox.Engine;
using Sandbox.Internal;

namespace Sandbox.UI;

internal static class TooltipSystem
{
	static IPanel lastHovered;
	static IPanel lastTooltip;

	internal static void Clear()
	{
		lastTooltip?.Delete( true );
		lastTooltip = null;
		lastHovered = null;
	}

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
		// Given the mouse position, try to position the tooltip
		// so it's not hanging off the screen. Not actually doing any
		// kind of restrict to screen or anything.
		//

		var pos = InputRouter.MouseCursorPosition;

		TextFlag align = 0;

		if ( pos.x < Screen.Size.x * 0.70f )
		{
			align |= TextFlag.Right;
		}
		else
		{
			align |= TextFlag.Left;
		}

		if ( pos.y > Screen.Size.y * 0.1f )
		{
			align |= TextFlag.Top;
		}
		else
		{
			align |= TextFlag.Bottom;
		}

		lastTooltip.SetAbsolutePosition( align, pos, 20 );

	}
}
