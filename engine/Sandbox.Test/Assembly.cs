global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Sandbox;
global using System.Linq;
global using System.Threading.Tasks;
global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Sandbox.Engine;
using Sandbox.Internal;
using System;

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
		TestAppSystem.Shutdown();
	}
}
