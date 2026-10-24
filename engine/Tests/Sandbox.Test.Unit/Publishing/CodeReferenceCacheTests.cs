using System;
using System.Collections.Generic;
using System.Threading;
using static Editor.ProjectPublisher.PackageManifest;

[TestClass]
public class CodeReferenceCacheTests
{
	[TestMethod]
	public void UnchangedSourceReusesParseButSameLengthEditsInvalidateIt()
	{
		var cache = new CodeReferenceCache();
		var before = cache.Analyze( "a.cs", "Model.Load(\"models/a.vmdl\");", "", null );
		Assert.AreSame( before, cache.Analyze( "a.cs", "Model.Load(\"models/a.vmdl\");", "", null ) );
		var after = cache.Analyze( "a.cs", "Model.Load(\"models/b.vmdl\");", "", null );
		Assert.AreNotSame( before, after );
		Assert.AreEqual( "models/b.vmdl", after.Single().Pattern );
		Assert.AreEqual( "models/a.vmdl", before.Single().Pattern );
	}

	[TestMethod]
	public void ResourceTypeChangesAndDifferentFilesCannotReuseStaleResults()
	{
		var cache = new CodeReferenceCache();
		var code = "ResourceLibrary.GetAll<Resource>();";
		var before = cache.Analyze( "a.cs", code, "old", new Dictionary<string, string[]> { ["Resource"] = ["*.item"] } );
		var after = cache.Analyze( "a.cs", code, "new", new Dictionary<string, string[]> { ["Resource"] = ["*.item", "*.bdef"] } );
		Assert.AreEqual( 1, before.Length );
		Assert.AreEqual( 2, after.Length );
		Assert.IsTrue( after.All( x => !x.Recommended ) );
		var other = cache.Analyze( "b.cs", "Model.Load(\"models/a.vmdl\");", "", null );
		Assert.AreEqual( "b.cs", other.Single().SourceFile );
	}

	[TestMethod]
	public void CancelledAnalysisCannotReplaceCachedResult()
	{
		var cache = new CodeReferenceCache();
		var code = "Model.Load(\"models/a.vmdl\");";
		var before = cache.Analyze( "a.cs", code, "", null );
		Assert.ThrowsException<OperationCanceledException>( () => cache.Analyze( "a.cs", "", "", null, new CancellationToken( true ) ) );
		Assert.AreSame( before, cache.Analyze( "a.cs", code, "", null ) );
	}

	[TestMethod]
	public void CachedReferencesStillMatchTheCurrentPublishSelection()
	{
		var cache = new CodeReferenceCache();
		var references = cache.Analyze( "a.cs", "Model.Load(\"models/a.vmdl\");", "", null );
		Assert.AreEqual( 1, FindCodeAssetWarnings( references, new Dictionary<string, string[]> { ["models/a.vmdl"] = ["models/a.vmdl"] } ).Count() );
		Assert.AreEqual( 0, FindCodeAssetWarnings( references, new Dictionary<string, string[]>() ).Count() );
	}
}
