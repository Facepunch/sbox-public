using Borealis.Backend;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// <see cref="SurfaceShaders.Warm"/> reads the default surfaces' modules ahead (their [Material] structs reflected with Slang),
/// so the first material converted to one doesn't wait for it on the thread that asks, the main thread for a model first
/// drawn after a load (skin's took 0.4 s there).
/// </summary>
[TestClass]
[DoNotParallelize]
public class SurfaceWarmTests : GpuTest
{
	[TestMethod]
	public void WarmReadsTheDefaultSurfacesModules()
	{
		using var backend = new BackendDevice( Device );
		var core = backend.Content.Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "skin.slang" ) ) ) ?? throw new AssertInconclusiveException( "No core shaders" );
		var skin = Path.GetFullPath( Path.Combine( core, "shaders", "skin.slang" ) );
		backend.Surfaces.ConvertShaders = true;
		backend.Surfaces.CompileInBackground = true;
		backend.Surfaces.Warm();

		var clock = System.Diagnostics.Stopwatch.StartNew();
		while ( !ConvertedMaterial.HasModule( skin ) && clock.Elapsed.TotalSeconds < 120 ) System.Threading.Thread.Sleep( 50 );
		Assert.IsTrue( ConvertedMaterial.HasModule( skin ), "skin.slang's module wasn't read ahead" );
		backend.Surfaces.WarmingModules?.Wait( TimeSpan.FromSeconds( 120 ) );
		backend.Surfaces.WaitForCompiles( TimeSpan.FromSeconds( 120 ) );
	}
}
