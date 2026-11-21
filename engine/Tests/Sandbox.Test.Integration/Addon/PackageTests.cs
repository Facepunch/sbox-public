namespace AddonTests
{
	[TestClass]
	public partial class PackageIdentTest
	{
		[TestMethod]
		public void ParseIdent()
		{
			Assert.IsFalse( Package.TryParseIdentParts( "assetparty", out var _ ) );
			Assert.IsFalse( Package.TryParseIdentParts( "asset_party", out var _ ) );
			Assert.IsFalse( Package.TryParseIdentParts( "asset-party", out var _ ) );
			Assert.IsFalse( Package.TryParseIdentParts( ".", out var _ ) );
			Assert.IsFalse( Package.TryParseIdentParts( "@", out var _ ) );
			Assert.IsFalse( Package.TryParseIdentParts( "#", out var _ ) );
			Assert.IsFalse( Package.TryParseIdentParts( "/", out var _ ) );

			{
				Assert.IsTrue( Package.TryParseIdentParts( "asset.party", out var p ) );
				Assert.AreEqual( "asset", p.org );
				Assert.AreEqual( "party", p.package );
				Assert.IsNull( p.version );
			}

			{
				Assert.IsTrue( Package.TryParseIdentParts( "asset/party", out var p ) );
				Assert.AreEqual( "asset", p.org );
				Assert.AreEqual( "party", p.package );
				Assert.IsNull( p.version );
			}

		}

		[TestMethod]
		public void ParseIdentWithVersion()
		{
			Assert.IsFalse( Package.TryParseIdentParts( "asset.party#poop", out var _ ) );
			Assert.IsFalse( Package.TryParseIdentParts( "asset.party#!!", out var _ ) );
			Assert.IsFalse( Package.TryParseIdentParts( "asset.party#0.234", out var _ ) );
			Assert.IsFalse( Package.TryParseIdentParts( "asset.party#", out var _ ) );

			{
				Assert.IsTrue( Package.TryParseIdentParts( "asset.party#45366", out var p ) );
				Assert.AreEqual( "asset", p.org );
				Assert.AreEqual( "party", p.package );
				Assert.AreEqual( 45366, p.version );
			}

			{
				Assert.IsTrue( Package.TryParseIdentParts( "asset/party#45366", out var p ) );
				Assert.AreEqual( "asset", p.org );
				Assert.AreEqual( "party", p.package );
				Assert.AreEqual( 45366, p.version );
			}
		}

		[TestMethod]
		public void ParseIdentWithUrl()
		{
			Assert.IsFalse( Package.TryParseIdentParts( "https://www.google.com/", out var _ ) );

			{
				Assert.IsTrue( Package.TryParseIdentParts( "https://asset.party/facepunch/sandbox", out var p ) );
				Assert.AreEqual( "facepunch", p.org );
				Assert.AreEqual( "sandbox", p.package );
				Assert.IsNull( p.version );
			}

			{
				Assert.IsTrue( Package.TryParseIdentParts( "https://sbox.game/facepunch/sandbox", out var p ) );
				Assert.AreEqual( "facepunch", p.org );
				Assert.AreEqual( "sandbox", p.package );
				Assert.IsNull( p.version );
			}

			{
				Assert.IsTrue( Package.TryParseIdentParts( "https://asset.party/facepunch/sandbox/", out var p ) );
				Assert.AreEqual( "facepunch", p.org );
				Assert.AreEqual( "sandbox", p.package );
				Assert.IsNull( p.version );
			}

			{
				Assert.IsTrue( Package.TryParseIdentParts( "https://asset.party/facepunch/sandbox#45366", out var p ) );
				Assert.AreEqual( "facepunch", p.org );
				Assert.AreEqual( "sandbox", p.package );
				Assert.AreEqual( 45366, p.version );
			}


			{
				Assert.IsTrue( Package.TryParseIdentParts( "https://asset.party/facepunch/sandbox/#45366", out var p ) );
				Assert.AreEqual( "facepunch", p.org );
				Assert.AreEqual( "sandbox", p.package );
				Assert.AreEqual( 45366, p.version );
			}
		}

		/// <summary>
		/// Existing addons can read the local flag from an inferred tuple.
		/// </summary>
		[TestMethod]
		public void TryParseIdentPreservesInferredLocalFlag()
		{
			const string ident = "asset.party#42";

#pragma warning disable CS0618 // This is how existing addon source calls the parser.
			Assert.IsTrue( Package.TryParseIdent( ident, out var p ) );
			Assert.IsFalse( Package.TryParseIdent( "asset.party#local", out _ ) );
#pragma warning restore CS0618

			Assert.AreEqual( 42, p.version );
			Assert.IsFalse( p.local );
		}

		/// <summary>
		/// The obsolete overloads still accept ordinary package identifiers.
		/// </summary>
		[TestMethod]
		public void ObsoleteOverloadsUsePackageIdent()
		{
#pragma warning disable CS0618 // Existing compiled addons can still call these overloads.
			Assert.IsTrue( Package.TryParseIdent( "asset.party", out (string org, string package, int? version, bool local) legacy ) );
			Assert.IsFalse( legacy.local );
			Assert.AreEqual( "asset.party", Package.FormatIdent( legacy.org, legacy.package, local: true ) );
#pragma warning restore CS0618
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
