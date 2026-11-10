using Sandbox.Network;
using Steamworks;

namespace NetworkTests;

[TestClass]
public class SteamConnectionErrorTests
{
	[TestMethod]
	public void ConnectedTransportReportsMissingHandshakeRatherThanRouteFailure()
	{
		var error = new SteamSessionStatus( ConnectionState.Connected, 0, "" ).DescribeTimeout( 30 );
		StringAssert.Contains( error, "host did not send the initial game handshake" );
		StringAssert.Contains( error, "30 seconds" );
	}

	[TestMethod]
	public void RoutingTimeoutDoesNotClaimTheHostAcceptedTheConnection()
	{
		var error = new SteamSessionStatus( ConnectionState.FindingRoute, 0, "" ).DescribeTimeout( 30 );
		StringAssert.Contains( error, "still finding a route" );
		StringAssert.Contains( error, "FindingRoute" );
	}

	[TestMethod]
	public void SteamFailureCodeDetailAndSendFailureArePreserved()
	{
		var error = new SteamSessionStatus( ConnectionState.ProblemDetectedLocally, 5003, "Relay negotiation failed" )
			.DescribeTimeout( 30, "Steam rejected the send: LimitExceeded" );
		StringAssert.Contains( error, "5003" );
		StringAssert.Contains( error, "Relay negotiation failed" );
		StringAssert.Contains( error, "LimitExceeded" );
	}
}
