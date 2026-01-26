using System;

namespace Services;

[TestClass]
public class StatsTest
{
	[TestMethod]
	public async Task GlobalStats()
	{
		var stats = Sandbox.Services.Stats.GetGlobalStats( "facepunch.ss1" );

		await stats.Refresh();

		foreach ( var stat in stats )
		{
			Console.WriteLine( $"{stat.Name} value: {stat.Value} players: {stat.Players}" );
		}

		Assert.IsTrue( stats.Count() > 0 );
	}

	[TestMethod]
	public async Task PlayertStats()
	{
		var stats = Sandbox.Services.Stats.GetPlayerStats( "facepunch.ss1", 76561197960279927 );

		await stats.Refresh();

		foreach ( var stat in stats )
		{
			Console.WriteLine( $"{stat.Name} value: {stat.Value} last: {stat.Last}" );
		}

		Assert.IsTrue( stats.Count() > 0 );
	}

}
