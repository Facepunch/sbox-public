using Sandbox.Engine;
using System.Text.Json;

namespace SystemTests;

[TestClass]
public class SystemInfoTest
{
	public TestContext TestContext { get; set; }

	// The window system is not up in this tier, so monitor count and refresh rate legitimately read 0 here.
	// WinHags is null on machines without a HAGS-capable GPU driver, which includes the CI runners.
	[TestMethod]
	public void HardwareReportSerializes()
	{
		var json = JsonSerializer.Serialize( SystemInfo.AsObject() );
		TestContext.WriteLine( json );

		using var doc = JsonDocument.Parse( json );
		var root = doc.RootElement;

		Assert.IsFalse( string.IsNullOrEmpty( root.GetProperty( "OsVersion" ).GetString() ) );
		Assert.IsTrue( root.TryGetProperty( "MonitorCount", out _ ) );
		Assert.IsTrue( root.TryGetProperty( "WinHags", out _ ) );
		Assert.IsTrue( root.TryGetProperty( "WinVrr", out _ ) );
	}
}
