using Borealis.Gpu;
using Sandbox.Engine;

namespace BorealisTests;

/// <summary>
/// On Borealis.Gpu (-borealisgpu) Source 2's render system is the empty one: <see cref="SystemInfo"/> reports the
/// Borealis.Gpu device, as games pick settings by GPU (Mow the Lawn turns volumetric fog off on AMD).
/// </summary>
[TestClass]
[DoNotParallelize]
public class SystemInfoGpuTests
{
	[TestMethod]
	public void BorealisGpuDeviceIsTheReportedGpu()
	{
		var flag = typeof( SystemInfo ).GetField( "_borealisGpu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static );
		var (gpu, version, memory, integrated) = (SystemInfo.Gpu, SystemInfo.GpuVersion, SystemInfo.GpuMemory, SystemInfo.GpuIntegrated);
		try
		{
			SystemInfo.SetBorealisGpu( "AMD Radeon RX 7900 XTX", "2.0.279", 24ul << 30, false );

			// Source 2's empty render system reports its adapter after the device is made: it doesn't replace it
			SystemInfo.SetGpu( "Empty Driver", "Empty Version", 256ul << 30, false );
			Assert.AreEqual( "AMD Radeon RX 7900 XTX", SystemInfo.Gpu );
			Assert.AreEqual( "2.0.279", SystemInfo.GpuVersion );
			Assert.AreEqual( 24ul << 30, SystemInfo.GpuMemory );
		}
		finally
		{
			flag.SetValue( null, false );
			SystemInfo.SetGpu( gpu, version, memory, integrated );
		}
	}

	[TestMethod]
	public void DriverVersionsDecodeAsSource2Does()
	{
		Assert.AreEqual( "2.0.279", AdapterInfo.DecodeDriverVersion( 0x1002, (2u << 22) | 279u ) );
		Assert.AreEqual( "551.86.0.0", AdapterInfo.DecodeDriverVersion( 0x10DE, (551u << 22) | (86u << 14) ) );
	}
}
