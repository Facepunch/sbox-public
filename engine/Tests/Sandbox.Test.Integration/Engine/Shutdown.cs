namespace EngineTests;

[TestClass]
public class ShutdownTest
{
	/// <summary>
	/// Shutdown clears global context state (including Surface.All), so after re-initing
	/// we must restore the fallback surface that AssemblyInitialize installed - otherwise
	/// every trace test that happens to run after this class loses all hits.
	/// </summary>
	static void Reinit()
	{
		TestInit.TestAppSystem.Init();

		if ( Surface.All.Count == 0 )
		{
			Surface.All[0] = new Surface();
		}
	}

	[TestMethod]
	public void Single()
	{
		// We already initialized the app for testing, so we can directly shutdown
		TestInit.TestAppSystem.Shutdown();

		// We need to re-init because other tests still need it
		Reinit();
	}

	[TestMethod]
	public void Multiple()
	{
		TestInit.TestAppSystem.Shutdown();
		Reinit();
		TestInit.TestAppSystem.Shutdown();
		Reinit();
	}
}
