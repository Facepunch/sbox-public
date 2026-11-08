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

		// Basalt's tests record through the Source 2 backend, which is the host's
		Basalt.RenderHost.Current = new UnitTestRenderHost();
	}
}

/// <summary>
/// Basalt's default host with s&amp;box's Source 2 device, which the tests record through without the engine running.
/// </summary>
internal sealed class UnitTestRenderHost : Basalt.RenderHost
{
	public override Basalt.GpuDevice Device => Basalt.Gpu.Source2.Source2Device.Instance;
}
