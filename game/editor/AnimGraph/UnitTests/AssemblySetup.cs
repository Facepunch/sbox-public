using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;

namespace Editor.AnimGraph.Test;

[TestClass]
public class AssemblySetup
{
	private static readonly TestAppSystem AppSystem = new();

	[AssemblyInitialize]
	public static void Initialize( TestContext context ) => AppSystem.Init();

	[AssemblyCleanup]
	public static void Cleanup() => AppSystem.Shutdown();
}
