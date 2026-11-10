namespace Sandbox.Diagnostics;

/// <summary>
/// Tracks sustained driver-budget overruns, the point at which GPU allocations can be paged to RAM.
/// This is a pressure signal, not a measurement of page transfers.
/// </summary>
internal struct VideoMemoryPressure
{
	bool _overBudget;
	double _overBudgetSince;

	internal bool IsActive { get; private set; }

	internal void Update( double now, ulong budget, ulong usage )
	{
		if ( budget == 0 )
		{
			this = default;
			return;
		}

		if ( IsActive )
		{
			// Require 5% headroom before clearing, so small budget fluctuations don't flicker.
			if ( usage <= budget - budget / 20 )
				this = default;
			return;
		}

		if ( usage <= budget )
		{
			_overBudget = false;
			return;
		}

		if ( !_overBudget )
		{
			_overBudget = true;
			_overBudgetSince = now;
		}

		IsActive = now - _overBudgetSince >= 2.0;
	}
}
