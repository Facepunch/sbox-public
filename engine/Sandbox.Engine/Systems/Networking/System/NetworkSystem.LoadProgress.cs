using Sandbox.Engine;
using Sandbox.Internal;
using static System.Net.Mime.MediaTypeNames;

namespace Sandbox.Network;

internal partial class NetworkSystem
{
	public void UpdateLoading( string text )
	{
		if ( !LoadingScreen.IsVisible )
		{
			// Reset media when first starting a new connection loading phase,
			// so stale media from a previous session doesn't bleed through.
			LoadingScreen.Media = null;
		}

		LoadingScreen.IsVisible = true;
		LoadingScreen.Title = text;
	}
}


