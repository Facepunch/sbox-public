global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Sandbox;
global using System.Linq;
global using System.Threading.Tasks;
global using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

using BorealisTests.Headless;

// Borealis with nothing of the engine: no native, no FACEPUNCH_ENGINE. Tests that turn the device's async compute on
// are [DoNotParallelize], as every render system reads it.
[assembly: Parallelize( Workers = 0, Scope = ExecutionScope.MethodLevel )]

[TestClass]
public class TestInit
{
	[AssemblyInitialize]
	public static void AssemblyInitialize( TestContext context )
	{
		Borealis.RenderHost.Current = new HeadlessRenderHost();
	}
}

/// <summary>
/// Borealis's default host, rendering with the headless device.
/// </summary>
internal sealed class HeadlessRenderHost : Borealis.RenderHost
{
	/// <summary>
	/// The device every test renders with.
	/// </summary>
	public static HeadlessDevice Headless { get; } = new();

	public override Borealis.GpuDevice Device => Headless;

	/// <summary>
	/// Borealis's debug views, for the tests of them, which are [DoNotParallelize] and turn them off after.
	/// </summary>
	public static Borealis.DebugViews Views { get; set; }

	public override Borealis.DebugViews DebugViews => Views;
}
