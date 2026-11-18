namespace Sandbox.Diagnostics;

internal enum WarningLevel
{
	None,
	Warning,
	Critical
}

/// <summary>
/// Debounces a sampled <see cref="WarningLevel"/> so brief spikes don't flash on screen. A worse level shows once
/// samples have stayed worse for <see cref="RaiseDelay"/>, a better one once they've stayed better for <see cref="ClearDelay"/>.
/// </summary>
internal struct WarningState
{
	internal const double RaiseDelay = 2.0;
	internal const double ClearDelay = 4.0;

	int _direction;
	double _since;

	internal WarningLevel Level { get; private set; }

	internal void Update( double now, WarningLevel sample )
	{
		if ( sample == Level )
		{
			_direction = 0;
			return;
		}

		var direction = sample > Level ? 1 : -1;
		if ( direction != _direction )
		{
			_direction = direction;
			_since = now;
		}

		if ( now - _since < (direction > 0 ? RaiseDelay : ClearDelay) )
			return;

		Level = sample;
		_direction = 0;
	}
}
