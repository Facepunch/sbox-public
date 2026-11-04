using System;

namespace Editor;

static class SceneCompileProgress
{
	internal static Rect Fill( Rect rect, float fraction )
	{
		if ( fraction >= 0 )
		{
			rect.Width *= fraction.Clamp( 0, 1 );
			return rect;
		}

		var width = rect.Width * 0.25f;
		var offset = (MathF.Sin( RealTime.Now * 3 ) + 1) * 0.5f * (rect.Width - width);
		return new Rect( rect.Left + offset, rect.Top, width, rect.Height );
	}
}
