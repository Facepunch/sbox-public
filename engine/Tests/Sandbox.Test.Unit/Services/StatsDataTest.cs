using System.Collections.Generic;
using System.Text.Json;

namespace ServicesTests;

[TestClass]
public class StatsDataTest
{
	class Probe
	{
		public static int Reads;
		public int Value { get { Reads++; return 5; } }
	}

	[TestMethod]
	public void StatDataIsSnapshottedWhenSubmitted()
	{
		Probe.Reads = 0;
		var data = new Dictionary<string, object> { ["probe"] = new Probe() };

		var snapshot = Sandbox.Services.Stats.GetObjectDictionary( data );

		Assert.AreNotSame( data, snapshot );
		Assert.AreEqual( 1, Probe.Reads );
		Assert.IsInstanceOfType<JsonElement>( snapshot["probe"] );

		JsonSerializer.Serialize( snapshot );
		Assert.AreEqual( 1, Probe.Reads );
	}
}
