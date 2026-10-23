global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Sandbox;
global using System.Linq;
global using System.Threading.Tasks;
global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

[TestClass]
public class TestInit
{
	public static Sandbox.AppSystem TestAppSystem;

	[AssemblyInitialize]
	public static void AssemblyInitialize( TestContext context )
	{
		// Integration tier: boots the full native engine for the whole assembly, so tests
		// here run serially against real engine state. Lighter tests belong in
		// Sandbox.Test.Engine (interop only) or Sandbox.Test.Unit (pure managed).
		var appSystem = new TestAppSystem();
		appSystem.Init();

		// Only assign it if we managed to Init()

		TestAppSystem = appSystem;

		// Trace results resolve the hit Surface by index, and nothing mounts base content
		// in headless tests - install a fallback surface so any test that traces works.
		if ( Surface.All.Count == 0 )
		{
			Surface.All[0] = new Surface();
		}
	}

	[AssemblyCleanup]
	public static void AssemblyCleanup()
	{
		TestAppSystem?.Shutdown();
	}
}
