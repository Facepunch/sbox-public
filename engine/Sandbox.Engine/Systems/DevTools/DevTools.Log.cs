namespace Sandbox.Diagnostics;

internal static partial class DevTools
{
	/// <summary>
	/// How many log lines the dev tools' console keeps.
	/// </summary>
	internal const int LogCapacity = 2000;

	static readonly Queue<LogEvent> _log = new();
	static bool _capturingLog;

	/// <summary>
	/// The most recent log lines, oldest first. Captured from the first frame, so the console has history when it opens.
	/// </summary>
	internal static IReadOnlyCollection<LogEvent> Log => _log;

	/// <summary>
	/// A line was logged. Main thread only, like the engine's own log event.
	/// </summary>
	internal static event Action<LogEvent> OnLog;

	static void EnsureLogCapture()
	{
		if ( _capturingLog )
			return;

		_capturingLog = true;
		Logging.OnMessage += CaptureLog;
	}

	static void CaptureLog( LogEvent e )
	{
		// Only the text is shown - holding what was logged would keep a closed scene alive for as long as the line stays
		e = e.WithWeakArguments();
		e.Exception = null;

		_log.Enqueue( e );
		while ( _log.Count > LogCapacity ) _log.Dequeue();

		OnLog?.Invoke( e );
	}
}
