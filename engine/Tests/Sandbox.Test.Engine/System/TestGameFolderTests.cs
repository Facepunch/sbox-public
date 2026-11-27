using System;
using System.IO;

namespace SystemTests;

[TestClass]
[DoNotParallelize]
public class TestGameFolderTests
{
	/// <summary>
	/// The tests load the native engine of the checkout they were built in, whatever FACEPUNCH_ENGINE says: it's a user-wide
	/// value another checkout's launcher can have written, and that build's native dlls crash this build's interop.
	/// </summary>
	[TestMethod]
	public void TestsLoadTheirCheckoutsGameFolder()
	{
		var checkout = TestAppSystem.CheckoutGameFolder( AppContext.BaseDirectory );
		Assert.IsNotNull( checkout );
		Assert.AreEqual( "game", Path.GetFileName( checkout ) );
		Assert.AreEqual( Path.GetFullPath( checkout ), Path.GetFullPath( Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ) ) );

		var before = Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" );
		try
		{
			Environment.SetEnvironmentVariable( "FACEPUNCH_ENGINE", Path.Combine( Path.GetTempPath(), "another-checkout", "game" ) );
			Assert.AreEqual( checkout, TestAppSystem.GameFolder() );
			Assert.AreEqual( checkout, Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ) );
		}
		finally
		{
			Environment.SetEnvironmentVariable( "FACEPUNCH_ENGINE", before );
		}
	}

	/// <summary>Outside a checkout with a built engine there's no checkout folder, and FACEPUNCH_ENGINE is used as it is.</summary>
	[TestMethod]
	public void NoCheckoutOutsideOne()
	{
		Assert.IsNull( TestAppSystem.CheckoutGameFolder( Path.GetPathRoot( Path.GetTempPath() ) ) );
	}
}
