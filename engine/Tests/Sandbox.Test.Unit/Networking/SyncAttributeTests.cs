namespace NetworkTests;

[TestClass]
public class SyncAttributeTests
{
	[TestMethod]
	public void LegacyQueryPropertyPreservesOtherFlags()
	{
		var attribute = new SyncAttribute( SyncFlags.FromHost | SyncFlags.Interpolate );
#pragma warning disable CS0618 // Compatibility remains intentional for both obsolete APIs.
		attribute.Query = true;
		Assert.AreEqual( SyncFlags.FromHost | SyncFlags.Interpolate | SyncFlags.Query, attribute.Flags );

		attribute.Query = false;
		Assert.AreEqual( SyncFlags.FromHost | SyncFlags.Interpolate, attribute.Flags );

		var queried = new SyncAttribute( SyncFlags.Query );
		Assert.AreEqual( 2u, (uint)queried.Flags );
#pragma warning restore CS0618
	}
}
