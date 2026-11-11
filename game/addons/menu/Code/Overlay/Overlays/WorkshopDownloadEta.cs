using System;

namespace MenuProject.Overlay.Overlays;

internal static class WorkshopDownloadEta
{
	internal static TimeSpan? Calculate( long bytesLeft, double mbps )
	{
		if ( bytesLeft <= 0 || !double.IsFinite( mbps ) || mbps <= 0 ) return null;

		var seconds = Math.Max( 1, Math.Round( bytesLeft / (mbps * 1024 * 1024 / 8) ) );

		// The estimate is rounded to whole seconds, so use the exact integer limit.
		if ( !double.IsFinite( seconds ) || seconds > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond ) return null;

		return TimeSpan.FromSeconds( (long)seconds );
	}
}
