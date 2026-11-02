using Sandbox.Internal;
using System.Text.Json.Nodes;

namespace JsonTests;

[TestClass]
[DoNotParallelize]
public class JsonUpgraderOwnershipTests
{
	[TestMethod]
	public void UpgradersOnlyRunForTypesFromTheirOwnAssembly()
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( ForeignUpgraderProbe ).Assembly, false );

		try
		{
			JsonUpgrader.UpdateUpgraders( library );
			ForeignUpgraderProbe.EngineCalls = 0;
			ForeignUpgraderProbe.OwnCalls = 0;

			JsonUpgrader.Upgrade( 0, new JsonObject(), typeof( PlatformSettings ) );
			JsonUpgrader.Upgrade( 0, new JsonObject(), typeof( OwnUpgradeTarget ) );

			Assert.AreEqual( 0, ForeignUpgraderProbe.EngineCalls );
			Assert.AreEqual( 1, ForeignUpgraderProbe.OwnCalls );
		}
		finally
		{
			JsonUpgrader.UpdateUpgraders( new TypeLibrary() );
		}
	}
}

public sealed class OwnUpgradeTarget
{
}

[Expose]
public static class ForeignUpgraderProbe
{
	public static int EngineCalls;
	public static int OwnCalls;

	[JsonUpgrader( typeof( PlatformSettings ), 100000 )]
	public static void UpgradeEngineType( JsonObject json ) => EngineCalls++;

	[JsonUpgrader( typeof( OwnUpgradeTarget ), 1 )]
	public static void UpgradeOwnType( JsonObject json ) => OwnCalls++;
}
