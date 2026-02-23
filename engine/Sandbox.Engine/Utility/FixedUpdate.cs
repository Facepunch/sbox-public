namespace Sandbox;

internal class FixedUpdate
{
	/// <summary>
	/// How many times a second FixedUpdate runs.
	/// </summary>
	public float Frequency = 16;

	public double Delta => 1d / Frequency;

	/// <summary>
	/// Accumulate frame time up until a maximum amount (maxSteps). While this value
	/// is above the <see cref="Delta"/> time we will invoke a fixed update.
	/// </summary>
	private long _step;

	internal void Run( Action fixedUpdate, double time, int maxSteps )
	{
		var delta = Delta;
		long curStep = (long)Math.Floor( time / delta );

		// Clamp the steps so we never jump too many
		_step = long.Clamp( _step, curStep - maxSteps, curStep );

		if ( _step == curStep )
			return;

		while ( _step < curStep )
		{
			_step++;
			using var timeScope = Time.Scope( (_step * delta), delta );
			fixedUpdate();
		}

		// always end up to date
		_step = curStep;
	}
}
