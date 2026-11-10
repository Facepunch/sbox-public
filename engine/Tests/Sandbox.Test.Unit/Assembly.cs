global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Sandbox;
global using System.Linq;
global using System.Threading.Tasks;
global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

// This assembly never loads the native engine, so these tests run anywhere dotnet runs
// and can parallelize freely. If a test needs interop it belongs in Sandbox.Test.Engine.
// The compiler toolchain tests (Compiling/, Hotload/) share state, so those classes
// are [DoNotParallelize] and run serially after the parallel batch.
[assembly: Parallelize( Workers = 0, Scope = ExecutionScope.MethodLevel )]

[TestClass]
public class TestInit
{
	[AssemblyInitialize]
	public static void AssemblyInitialize( TestContext context )
	{
		// Managed-only setup - keep anything native out of here.
		Application.IsUnitTest = true;
	}
}
