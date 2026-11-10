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
	public void FootprintIncludesPublishedDependenciesWithoutChangingAdditionalSize()
	{
		var manifest = new ProjectPublisher.PackageManifest();
		var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
		var graph = (Dictionary<string, ReviewSizeNode>)typeof( ProjectPublisher.PackageManifest ).GetField( "reviewSizeGraph", flags ).GetValue( manifest );
		graph["a"] = new( Files( "a_c", 10 ), ["b", "shared"] );
		graph["b"] = new( Files( "b_c", 20 ), ["a", "shared"] );
		graph["shared"] = new( Files( "shared_c", 30 ), [] );
		var included = (HashSet<string>)typeof( ProjectPublisher.PackageManifest ).GetField( "reviewIncludedFiles", flags ).GetValue( manifest );
		included.UnionWith( ["a_c", "b_c", "shared_c"] );

		Assert.AreEqual( new PublishImpact( 0, 0, false, false ), manifest.GetPublishImpact( ["a", "b"] ) );
		Assert.AreEqual( new PublishImpact( 30, 30, false, false ), manifest.GetPublishFootprint( ["a", "b", "A"] ) );
		Assert.AreEqual( new PublishImpact( 0, 0, false, false ), manifest.GetPublishImpact( ["a", "b"] ) );
		typeof( ProjectPublisher.PackageManifest ).GetMethod( "ClearAssets", flags ).Invoke( manifest, null );
		Assert.IsNull( manifest.GetPublishFootprint( ["a", "b"] ).TotalBytes );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void IncludedSoundFootprintCountsRawSourceOnlyWhenPublished( bool includeRawSource )
	{
		var manifest = new ProjectPublisher.PackageManifest();
		var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
		var graph = (Dictionary<string, ReviewSizeNode>)typeof( ProjectPublisher.PackageManifest ).GetField( "reviewSizeGraph", flags ).GetValue( manifest );
		graph["music.vsnd"] = new( Files( "music.vsnd_c", 20 ), [], RootFiles: Files( "music.ogg", 50 ) );
		var aliases = (Dictionary<string, string>)typeof( ProjectPublisher.PackageManifest ).GetField( "reviewAssetAliases", flags ).GetValue( manifest );
		aliases["music.vsnd_c"] = "music.vsnd";
		aliases["music.ogg"] = "music.vsnd";
		var included = (HashSet<string>)typeof( ProjectPublisher.PackageManifest ).GetField( "reviewIncludedFiles", flags ).GetValue( manifest );
		included.Add( "music.vsnd_c" );
		if ( includeRawSource ) included.Add( "music.ogg" );

		Assert.AreEqual( new PublishImpact( includeRawSource ? 70 : 20, 0, false, false ), manifest.GetPublishFootprint( ["music.vsnd_c", "music.ogg"] ) );
		Assert.AreEqual( new PublishImpact( includeRawSource ? 0 : 50, 0, false, false ), manifest.GetPublishImpact( ["music.ogg"] ) );
	}

	[TestMethod]
	public void ImpactCacheIsNotUploaded()
	{
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.GetPublishImpact( ["private/unused.vmdl"] );
		manifest.GetPublishFootprint( ["private/unused.vmdl"] );
		Assert.IsFalse( manifest.ToJson().Contains( "private/unused.vmdl" ) );
	}
}
