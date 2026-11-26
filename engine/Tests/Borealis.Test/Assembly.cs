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

	/// <summary>
	/// Volumetric fog, for the test of the setting, which is [DoNotParallelize] and turns it back on after.
	/// </summary>
	public static bool VolumetricFogOn { get; set; } = true;

	public override bool VolumetricFog => VolumetricFogOn;

	/// <summary>The fog's froxels (<see cref="VolumetricFogSize"/>), for the tests of the fog quality.</summary>
	public static Vector3Int FogSize { get; set; } = Borealis.VolumetricFog.DefaultSize;

	public override Vector3Int VolumetricFogSize => FogSize;

	/// <summary>
	/// Whether frames drawn into a window describe themselves (<see cref="WantsFramePlan"/>), for the tests of it, which are
	/// [DoNotParallelize] and turn it off after.
	/// </summary>
	public static bool WantsPlans { get; set; }

	/// <summary>The last frame plan and debug views' report a frame drawn into a window published.</summary>
	public static Borealis.FramePlan LastPlan { get; set; }

	public static Borealis.DebugViewReport LastReport { get; set; }

	public override bool WantsFramePlan => WantsPlans;

	public override void PublishFramePlan( Borealis.FramePlan plan ) => LastPlan = plan;

	public override void PublishDebugViews( Borealis.DebugViewReport report ) => LastReport = report;
}
