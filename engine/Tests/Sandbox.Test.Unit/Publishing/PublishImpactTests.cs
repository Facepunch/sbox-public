using System;
using System.Collections.Generic;
using Editor;
using static Editor.ProjectPublisher.PackageManifest;

[TestClass]
public class PublishImpactTests
{
	static Dictionary<string, long?> Files( string path, long? size ) => new( StringComparer.OrdinalIgnoreCase ) { [path] = size };

	[TestMethod]
	public void SharedDependenciesCyclesAndPublishedFilesCountOnce()
	{
		var graph = new Dictionary<string, ReviewSizeNode>( StringComparer.OrdinalIgnoreCase )
		{
			["a"] = new( Files( "a_c", 10 ), ["b", "c"] ),
			["b"] = new( Files( "b_c", 20 ), ["a", "c", "published"] ),
			["c"] = new( Files( "c_c", 30 ), [] ),
			["published"] = new( Files( "published_c", 100 ), [] )
		};
		var impact = CalculatePublishImpact( ["a"], graph, ["PUBLISHED_C"] );
		Assert.AreEqual( new PublishImpact( 10, 50, false, false ), impact );
		Assert.AreEqual( 60L, impact.TotalBytes );
		Assert.AreEqual( new PublishImpact( 30, 30, false, false ), CalculatePublishImpact( ["a", "B", "a"], graph, ["published_c"] ) );
	}

	[TestMethod]
	public void RootOnlySourceFilesAreNotPulledInByTransitiveReferences()
	{
		var graph = new Dictionary<string, ReviewSizeNode>
		{
			["a"] = new( Files( "a_c", 10 ), ["b"], RootFiles: Files( "a", 3 ) ),
			["b"] = new( Files( "b_c", 20 ), [], RootFiles: Files( "b", 5 ) )
		};
		Assert.AreEqual( new PublishImpact( 13, 20, false, false ), CalculatePublishImpact( ["a"], graph, [] ) );
		Assert.AreEqual( new PublishImpact( 38, 0, false, false ), CalculatePublishImpact( ["a", "b"], graph, [] ) );
	}

	[TestMethod]
	public void MissingMetadataStaysUnknownInsteadOfPretendingItIsZero()
	{
		var graph = new Dictionary<string, ReviewSizeNode>
		{
			["a"] = new( Files( "a_c", null ), ["missing"], Incomplete: true )
		};
		var impact = CalculatePublishImpact( ["a"], graph, [] );
		Assert.IsTrue( impact.DirectUnknown );
		Assert.IsTrue( impact.TransitiveUnknown );
		Assert.IsNull( impact.TotalBytes );
		Assert.AreEqual( new PublishImpact( 0, 0, false, false ), CalculatePublishImpact( [], graph, [] ) );
	}

	[TestMethod]
	public void ImpactCacheIsNotUploaded()
	{
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.GetPublishImpact( ["private/unused.vmdl"] );
		Assert.IsFalse( manifest.ToJson().Contains( "private/unused.vmdl" ) );
	}
}
