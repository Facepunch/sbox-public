using System.Threading;

namespace Sandbox;

public partial class Scene
{
	private readonly object logSync = new();
	private readonly Queue<LogEvent> pendingLogs = new();
	private static readonly AsyncLocal<bool> deliveringLogs = new();

	internal static void CaptureLogEvent( LogEvent entry )
	{
		if ( deliveringLogs.Value )
		{
			return;
		}

		// Capture the originating scene before background messages are forwarded to the main thread.
		var scene = Game.ActiveScene;
		if ( scene is null || !scene.IsValid || scene.IsEditor )
		{
			return;
		}

		entry = entry.WithWeakArguments();
		entry.Exception = null;
		lock ( scene.logSync )
		{
			if ( !scene.IsValid )
			{
				return;
			}

			scene.pendingLogs.Enqueue( entry );
			while ( scene.pendingLogs.Count > 512 )
			{
				scene.pendingLogs.Dequeue();
			}
		}
	}

	internal void DispatchLogEvents()
	{
		LogEvent[] batch;
		lock ( logSync )
		{
			if ( pendingLogs.Count == 0 )
			{
				return;
			}

			batch = pendingLogs.ToArray();
			pendingLogs.Clear();
		}

		var listeners = Query<Component.ILogListener>().ToArray();
		deliveringLogs.Value = true;
		try
		{
			foreach ( var entry in batch )
			{
				foreach ( var listener in listeners )
				{
					if ( listener is not Component component || !component.IsValid || !component.Active )
					{
						continue;
					}

					try
					{
						listener.OnLog( entry );
					}
					catch ( Exception exception )
					{
						Log.Warning( exception, "Exception in log listener" );
					}
				}
			}
		}
		finally
		{
			deliveringLogs.Value = false;
		}
	}
}
