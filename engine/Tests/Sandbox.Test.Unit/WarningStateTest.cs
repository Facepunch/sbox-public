using Sandbox.Diagnostics;

/// <summary>
/// The debouncing behind the warning icons: they need a sustained problem to show and a sustained recovery to clear.
/// </summary>
[TestClass]
public class WarningStateTest
{
	const double Raise = WarningState.RaiseDelay, Clear = WarningState.ClearDelay;

	static WarningState Hold( WarningState state, WarningLevel level, double from, double to )
	{
		for ( var t = from; t <= to; t += 0.5 )
			state.Update( t, level );
		return state;
	}

	[TestMethod]
	public void BriefSpikeNeverShows()
	{
		var state = Hold( default, WarningLevel.Critical, 0, Raise - 0.5 );
		state = Hold( state, WarningLevel.None, Raise, Raise + 10 );
		Assert.AreEqual( WarningLevel.None, state.Level );
	}

	[TestMethod]
	public void ShowsAfterRaiseDelay()
	{
		var state = Hold( default, WarningLevel.Warning, 0, Raise );
		Assert.AreEqual( WarningLevel.Warning, state.Level );
	}

	[TestMethod]
	public void ClearsOnlyAfterClearDelay()
	{
		var state = Hold( default, WarningLevel.Warning, 0, Raise );
		state = Hold( state, WarningLevel.None, 10, 10 + Clear - 0.5 );
		Assert.AreEqual( WarningLevel.Warning, state.Level );

		state.Update( 10 + Clear, WarningLevel.None );
		Assert.AreEqual( WarningLevel.None, state.Level );
	}

	[TestMethod]
	public void EscalatesAndDeescalates()
	{
		var state = Hold( default, WarningLevel.Warning, 0, Raise );
		state = Hold( state, WarningLevel.Critical, 10, 10 + Raise );
		Assert.AreEqual( WarningLevel.Critical, state.Level );

		state = Hold( state, WarningLevel.Warning, 20, 20 + Clear );
		Assert.AreEqual( WarningLevel.Warning, state.Level );
	}

	[TestMethod]
	public void FlickeringAtThresholdHoldsLevel()
	{
		var state = Hold( default, WarningLevel.Warning, 0, Raise );

		// Bouncing between warning and fine never stays fine long enough to clear
		for ( var t = 10.0; t < 30; t += 1 )
			state.Update( t, t % 2 == 0 ? WarningLevel.None : WarningLevel.Warning );

		Assert.AreEqual( WarningLevel.Warning, state.Level );
	}

	[TestMethod]
	public void QualityThresholds()
	{
		Assert.AreEqual( WarningLevel.None, DebugOverlay.Warnings.Quality( -1 ), "no measurement" );
		Assert.AreEqual( WarningLevel.None, DebugOverlay.Warnings.Quality( 0 ), "no measurement" );
		Assert.AreEqual( WarningLevel.None, DebugOverlay.Warnings.Quality( 1 ) );
		Assert.AreEqual( WarningLevel.Warning, DebugOverlay.Warnings.Quality( 0.9f ) );
		Assert.AreEqual( WarningLevel.Critical, DebugOverlay.Warnings.Quality( 0.5f ) );
	}
}
