using Editor;
using System;
using System.IO;
using System.Text;

namespace EditorTests;

[TestClass]
public class AssetUpgraderTests
{
	[TestMethod]
	public void AppliesUpgradesInOrder()
	{
		Assert.IsTrue( AssetUpgrader.TryUpgrade( typeof( AssetUpgraderTests ).Assembly, ".PLAIN", "old", out var upgraded ) );
		Assert.AreEqual( "current", upgraded );

		Assert.IsFalse( AssetUpgrader.TryUpgrade( typeof( AssetUpgraderTests ).Assembly, "plain", upgraded, out var repeated ) );
		Assert.AreEqual( upgraded, repeated );
	}

	[TestMethod]
	public void FailedUpgradeDiscardsEarlierChanges()
	{
		const string source = "original";
		Assert.IsFalse( AssetUpgrader.TryUpgrade( typeof( AssetUpgraderTests ).Assembly, "failed", source, out var upgraded ) );
		Assert.AreEqual( source, upgraded );
	}

	[TestMethod]
	public void CachedReferencesOnlySkipUnrelatedUpgrades()
	{
		var bark = new AssetUpgraderAttribute( ".vmat" ) { SourceReference = "bark.shader" };
		var foliage = new AssetUpgraderAttribute( ".vmat" ) { SourceReference = "foliage.shader" };
		string[] references = [@"Shaders\FOLIAGE.shader_c"];

		Assert.IsTrue( AssetUpgrader.CanSkip( [bark], references ) );
		Assert.IsFalse( AssetUpgrader.CanSkip( [foliage], references ) );
		Assert.IsFalse( AssetUpgrader.CanSkip( [bark, foliage], references ) );
		Assert.IsFalse( AssetUpgrader.CanSkip( [bark, new( ".vmat" )], references ) );
		Assert.IsFalse( AssetUpgrader.CanSkip( [bark], [] ) );
		Assert.IsFalse( AssetUpgrader.CanSkip( [bark], [null] ) );
	}

	[TestMethod]
	public void FileUpgradePreservesEncodingWithoutCachedReferences()
	{
		var path = Path.Combine( Path.GetTempPath(), $"{Guid.NewGuid():N}.plain" );

		try
		{
			File.WriteAllText( path, "old", Encoding.Unicode );
			Assert.IsTrue( AssetUpgrader.UpgradeFile( typeof( AssetUpgraderTests ).Assembly, path ) );

			byte[] expected = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes( "current" )];
			CollectionAssert.AreEqual( expected, File.ReadAllBytes( path ) );
		}
		finally
		{
			File.Delete( path );
		}
	}

	static class Upgrades
	{
		[AssetUpgrader( ".plain", Order = 10, SourceReference = "intermediate.resource" )]
		public static string Finish( string source ) => source == "intermediate" ? "current" : null;

		[AssetUpgrader( ".plain", SourceReference = "old.resource" )]
		public static string Start( string source ) => source == "old" ? "intermediate" : null;

		[AssetUpgrader( ".failed", Order = -1 )]
		public static string PartialChange( string source ) => source + " changed";

		[AssetUpgrader( ".failed" )]
		public static string Fail( string source ) => throw new InvalidOperationException( "Test upgrade failure after an earlier change." );
	}
}
