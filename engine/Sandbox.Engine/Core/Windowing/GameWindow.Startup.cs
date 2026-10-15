using Sandbox.Utility;

namespace Sandbox.Engine;

internal sealed partial class GameWindow
{
	float startupProgress = -1;
	string startupStatus = string.Empty;
	bool startupFinished;
	internal void UpdateStartupProgress( float progress, string status )
	{
		ThreadSafe.AssertIsMainThread();
		if ( startupFinished || CommandLine.HasSwitch( "-nowindow" ) ) return;

		progress = Math.Clamp( progress, 0, 1 );
		status ??= startupStatus;
		if ( progress < startupProgress || (progress == startupProgress && status == startupStatus) ) return;

		startupProgress = progress;
		startupStatus = status;
		startupWindow?.UpdateProgress( progress, status );
	}
}
