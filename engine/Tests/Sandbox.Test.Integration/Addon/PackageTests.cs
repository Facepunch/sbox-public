namespace AddonTests
{
	[TestClass]
	public partial class PackageIdentTest
	{
		[TestMethod]
		public void ParseIdent()
		{
			Assert.IsFalse( Package.TryParseIdent( "assetparty", out var _ ) );
			Assert.IsFalse( Package.TryParseIdent( "asset_party", out var _ ) );
			Assert.IsFalse( Package.TryParseIdent( "asset-party", out var _ ) );
			Assert.IsFalse( Package.TryParseIdent( ".", out var _ ) );
			Assert.IsFalse( Package.TryParseIdent( "@", out var _ ) );
			Assert.IsFalse( Package.TryParseIdent( "#", out var _ ) );
			Assert.IsFalse( Package.TryParseIdent( "/", out var _ ) );

			{
				Assert.IsTrue( Package.TryParseIdent( "asset.party", out var p ) );
				Assert.AreEqual( "asset", p.org );
				Assert.AreEqual( "party", p.package );
				Assert.IsNull( p.version );
			}

			{
				Assert.IsTrue( Package.TryParseIdent( "asset/party", out var p ) );
				Assert.AreEqual( "asset", p.org );
				Assert.AreEqual( "party", p.package );
				Assert.IsNull( p.version );
			}

		}

		[TestMethod]
		public void ParseIdentWithVersion()
		{
			Assert.IsFalse( Package.TryParseIdent( "asset.party#poop", out var _ ) );
			Assert.IsFalse( Package.TryParseIdent( "asset.party#!!", out var _ ) );
			Assert.IsFalse( Package.TryParseIdent( "asset.party#0.234", out var _ ) );
			Assert.IsFalse( Package.TryParseIdent( "asset.party#", out var _ ) );

			{
				Assert.IsTrue( Package.TryParseIdent( "asset.party#45366", out var p ) );
				Assert.AreEqual( "asset", p.org );
				Assert.AreEqual( "party", p.package );
				Assert.AreEqual( 45366, p.version );
			}

			{
				Assert.IsTrue( Package.TryParseIdent( "asset/party#45366", out var p ) );
				Assert.AreEqual( "asset", p.org );
				Assert.AreEqual( "party", p.package );
				Assert.AreEqual( 45366, p.version );
			}
		}

		[TestMethod]
		public void ParseIdentWithUrl()
		{
			Assert.IsFalse( Package.TryParseIdent( "https://www.google.com/", out var _ ) );

			{
				Assert.IsTrue( Package.TryParseIdent( "https://asset.party/facepunch/sandbox", out var p ) );
				Assert.AreEqual( "facepunch", p.org );
				Assert.AreEqual( "sandbox", p.package );
				Assert.IsNull( p.version );
			}

			{
				Assert.IsTrue( Package.TryParseIdent( "https://sbox.game/facepunch/sandbox", out var p ) );
				Assert.AreEqual( "facepunch", p.org );
				Assert.AreEqual( "sandbox", p.package );
				Assert.IsNull( p.version );
			}

			{
				Assert.IsTrue( Package.TryParseIdent( "https://asset.party/facepunch/sandbox/", out var p ) );
				Assert.AreEqual( "facepunch", p.org );
				Assert.AreEqual( "sandbox", p.package );
				Assert.IsNull( p.version );
			}

			{
				Assert.IsTrue( Package.TryParseIdent( "https://asset.party/facepunch/sandbox#45366", out var p ) );
				Assert.AreEqual( "facepunch", p.org );
				Assert.AreEqual( "sandbox", p.package );
				Assert.AreEqual( 45366, p.version );
			}


			{
				Assert.IsTrue( Package.TryParseIdent( "https://asset.party/facepunch/sandbox/#45366", out var p ) );
				Assert.AreEqual( "facepunch", p.org );
				Assert.AreEqual( "sandbox", p.package );
				Assert.AreEqual( 45366, p.version );
			}
		}

		[TestMethod]
		public void ParseIdentWithLocal()
		{
			Assert.IsTrue( Package.TryParseIdent( "asset.party#local", out var p ) );

			Assert.IsTrue( p.local );
			Assert.IsNull( p.version );
		}

		[TestMethod]
		[TestCategory( "LiveBackend" )]
		public async Task PackageFindAsync()
		{
			var result = await Package.FindAsync( "type:game", 200, 0 );

			Assert.IsNotNull( result.Packages );
			Assert.IsTrue( result.Packages.Length > 0 );
		}
	}
}
