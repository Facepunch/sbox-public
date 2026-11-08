global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Sandbox;
global using System.Linq;
global using System.Threading.Tasks;
global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

using BasaltTests.Headless;

// Basalt with nothing of the engine: no native, no FACEPUNCH_ENGINE. Tests that turn the device's async compute on
// are [DoNotParallelize], as every render system reads it.
[assembly: Parallelize( Workers = 0, Scope = ExecutionScope.MethodLevel )]

[TestClass]
public class TestInit
{
	[AssemblyInitialize]
	public static void AssemblyInitialize( TestContext context )
	{
		Basalt.RenderHost.Current = new HeadlessRenderHost();
	}
}

/// <summary>
/// Basalt's default host, rendering with the headless device.
/// </summary>
internal sealed class HeadlessRenderHost : Basalt.RenderHost
{
	/// <summary>
	/// The device every test renders with.
	/// </summary>
	public static HeadlessDevice Headless { get; } = new();

	public override Basalt.GpuDevice Device => Headless;
}
