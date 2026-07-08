using System;

namespace Editor.Mcp;

/// <summary>
/// A ring of recent log events kept for the read_console tool. Captures from editor start,
/// independently of the console window, so agents can always see what the editor logged.
/// </summary>
internal static class LogBuffer
{
	const int MaxEvents = 2000;

	static readonly object sync = new();
	static readonly Queue<LogEvent> events = new();
	static bool capturing;

	public static void StartCapture()
	{
		if ( capturing )
			return;

		capturing = true;

		EditorUtility.AddLogger( OnLog );
	}

	static void OnLog( LogEvent e )
	{
		lock ( sync )
		{
			events.Enqueue( e );

			while ( events.Count > MaxEvents )
			{
				events.Dequeue();
			}
		}
	}

	/// <summary>
	/// Everything buffered so far, oldest first.
	/// </summary>
	public static LogEvent[] Snapshot()
	{
		lock ( sync )
		{
			return events.ToArray();
		}
	}
}
