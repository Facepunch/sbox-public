using Sandbox.Engine;
using Sandbox.Services;
using System.Reflection;

namespace ServicesTests;

/// <summary>
/// Failed authentication refreshes preserve an established account without contacting Steam or the backend.
/// </summary>
[TestClass, DoNotParallelize]
public class AccountInformationTests
{
	/// <summary>
	/// Offline fallback belongs to initial login; a failed refresh retains credentials and credits.
	/// </summary>
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FailedLoginPreservesEstablishedSession( bool hasSession )
	{
		ThreadSafe.MarkMainThread();
		var sessionProperty = typeof( AccountInformation ).GetProperty( nameof( AccountInformation.Session ) );
		var userProperty = typeof( AccountInformation ).GetProperty( nameof( AccountInformation.SteamId ) );
		var connectedProperty = typeof( Api ).GetProperty( nameof( Api.IsConnected ), BindingFlags.Static | BindingFlags.NonPublic );
		var previousSession = AccountInformation.Session;
		var previousUser = AccountInformation.SteamId;
		var previousConnected = Api.IsConnected;
		var previousAuthentication = Api.UseSteamAuthentication;
		var previousCredits = Ai.Credits;
		var credits = new Ai.CreditBalance { Available = 42 };

		try
		{
			sessionProperty.SetValue( null, hasSession ? "expired-session" : null );
			userProperty.SetValue( null, hasSession ? 42L : 0L );
			connectedProperty.SetValue( null, true );
			Ai.Credits = credits;
			Api.UseSteamAuthentication = false;

			var update = AccountInformation.Update();
			Assert.IsTrue( update.IsCompleted, "Disabled Steam authentication must finish without network access." );
			update.GetAwaiter().GetResult();

			Assert.AreEqual( hasSession, Api.IsConnected );
			Assert.AreEqual( hasSession ? "expired-session" : null, AccountInformation.Session );
			Assert.AreEqual( hasSession ? 42L : 0L, AccountInformation.SteamId );
			Assert.AreEqual( hasSession ? credits : (Ai.CreditBalance?)null, Ai.Credits );
		}
		finally
		{
			sessionProperty.SetValue( null, previousSession );
			userProperty.SetValue( null, previousUser );
			connectedProperty.SetValue( null, previousConnected );
			Api.UseSteamAuthentication = previousAuthentication;
			Ai.Credits = previousCredits;
		}
	}
}
