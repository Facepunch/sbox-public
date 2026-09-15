using System;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Network;

namespace NetworkTests;

[TestClass]
public class GameNetworkSystemCompatibilityTest
{
	[TestMethod]
	public async Task SnapshotCallbacksDispatchToLegacyOverrides()
	{
		using var system = new LegacyNetworkSystem();
		var previousHost = new MockConnection( Guid.NewGuid() );
		var newHost = new MockConnection( Guid.NewGuid() );

		await system.BecomeHostAsync( previousHost, SnapshotMsg.Create() );
		Assert.AreEqual( 1, system.BecameHostCalls );
		Assert.AreSame( previousHost, system.PreviousHost );

		await system.ResyncFromHostAsync( previousHost, newHost, SnapshotMsg.Create() );
		Assert.AreEqual( 1, system.HostChangedCalls );
		Assert.AreSame( previousHost, system.PreviousHost );
		Assert.AreSame( newHost, system.NewHost );
	}

	sealed class LegacyNetworkSystem : GameNetworkSystem
	{
		public int BecameHostCalls;
		public int HostChangedCalls;
		public Connection PreviousHost;
		public Connection NewHost;

		public override void OnBecameHost( Connection previousHost )
		{
			BecameHostCalls++;
			PreviousHost = previousHost;
		}

		public override void OnHostChanged( Connection previousHost, Connection newHost )
		{
			HostChangedCalls++;
			PreviousHost = previousHost;
			NewHost = newHost;
		}
	}
}
