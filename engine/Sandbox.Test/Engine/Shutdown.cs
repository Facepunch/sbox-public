namespace Engine;

[TestClass]
public class Shutdown
{
	[TestMethod]
	public void Single()
	{
		// We already initialized the app for testing, so we can directly shutdown
		TestInit.TestAppSystem.Shutdown();

		// We need to re-init because other tests still need it
		TestInit.TestAppSystem.Init();
	}

	[TestMethod]
	public void Multiple()
	{
		TestInit.TestAppSystem.Shutdown();
		TestInit.TestAppSystem.Init();
		TestInit.TestAppSystem.Shutdown();
		TestInit.TestAppSystem.Init();
	}
}
