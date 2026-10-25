using Editor;
using System;
using System.IO;
using static Editor.ProjectPublisher.PackageManifest;

[TestClass]
public class PublishPreviewTests
{
	[TestMethod]
	public async Task PreviewReadsMetadataWithoutOpeningFileContents()
	{
		var path = Path.GetTempFileName();
		try
		{
			await File.WriteAllBytesAsync( path, new byte[1024] );
			using var locked = new FileStream( path, FileMode.Open, FileAccess.ReadWrite, FileShare.None );
			var preview = await ReadPublishFile( path, "asset.bin", previewOnly: true );
			Assert.AreEqual( 1024, preview.Size );
			Assert.AreEqual( 1024L, ReadReviewFileSize( path ) );
			Assert.AreEqual( "asset.bin", preview.Name );
			Assert.IsNull( preview.Hash );
			Assert.IsNull( preview.Contents );
		}
		finally
		{
			File.Delete( path );
		}
	}

	[TestMethod]
	public void UnavailableReviewSizesRemainUnknown()
	{
		Assert.IsNull( ReadReviewFileSize( null ) );
		Assert.IsNull( ReadReviewFileSize( Path.Combine( Path.GetTempPath(), Guid.NewGuid() + ".vmdl_c" ) ) );
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.ExcludedAssetSizes["secret.vmdl"] = 1024;
		Assert.IsFalse( manifest.ToJson().Contains( "secret.vmdl" ) );
	}

	[TestMethod]
	public async Task UploadStillHashesCurrentContents()
	{
		var path = Path.GetTempFileName();
		try
		{
			await File.WriteAllTextAsync( path, "first" );
			var before = await ReadPublishFile( path, "asset.bin", previewOnly: false );
			await File.WriteAllTextAsync( path, "other" );
			var after = await ReadPublishFile( path, "asset.bin", previewOnly: false );
			Assert.AreEqual( before.Size, after.Size );
			Assert.IsFalse( string.IsNullOrEmpty( before.Hash ) );
			Assert.AreNotEqual( before.Hash, after.Hash );
		}
		finally
		{
			File.Delete( path );
		}
	}

	[TestMethod]
	public void ScenesWarnWithoutCodeReferencesAndMissingConfiguredScenesComeFirst()
	{
		var warnings = FindSceneWarnings( ["scenes/start.scene", "scenes/missing.scene", "scenes/extra.scene"],
			["SCENES/START.SCENE_C"], ["scenes/extra.scene", "scenes/test.scene", "models/hat.vmdl"],
			path => path != "scenes/missing.scene" ).ToArray();
		CollectionAssert.AreEqual( new[] { "scenes/missing.scene", "scenes/extra.scene", "scenes/test.scene" }, warnings.Select( x => x.Path ).ToArray() );
		Assert.IsTrue( warnings[0].Missing );
		Assert.IsTrue( warnings.Skip( 1 ).All( x => !x.Missing ) );
	}

	[TestMethod]
	public void PublishedAndBlankScenesDoNotWarn()
	{
		var warnings = FindSceneWarnings( ["scenes/start.scene", "", null], ["scenes/start.scene"], [], _ => true );
		Assert.AreEqual( 0, warnings.Count() );
	}
}
