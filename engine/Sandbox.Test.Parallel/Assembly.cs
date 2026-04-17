global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Sandbox;
global using System.Linq;
global using System.Threading.Tasks;
global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Sandbox.Engine;
using System;

// Sandbox.Test.Parallel uses a live TestAppSystem like Sandbox.Test, but only hosts curated classes that are isolated
// enough to run in parallel across test classes.
[assembly: Parallelize( Workers = 4, Scope = ExecutionScope.ClassLevel )]

[TestClass]
public class TestInit
{
	public static Sandbox.AppSystem TestAppSystem;

	[AssemblyInitialize]
	public static void AssemblyInitialize( TestContext context )
	{
		TestAppSystem = new TestAppSystem();
		TestAppSystem.Init();
	}

	[AssemblyCleanup]
	public static void AssemblyCleanup()
	{
		// MSTest may invoke assembly cleanup on a worker thread. The engine shutdown path asserts
		// main-thread access, so align the cleanup thread with the engine's thread-safety contract.
		ThreadSafe.MarkMainThread();
		TestAppSystem.Shutdown();
	}
}
