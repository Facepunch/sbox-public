using System;
using System.Collections.Generic;
using Editor;
using static Editor.ProjectPublisher.PackageManifest;

[TestClass]
public class PublishDependencyTests
{
	[TestMethod]
	public void SharedDependenciesAndCyclesCountUniqueAssets()
	{
		var graph = new Dictionary<string, ReviewSizeNode>( StringComparer.OrdinalIgnoreCase )
		{
			["scene"] = new( new(), ["model", "material"] ),
			["model"] = new( new(), ["scene", "material"] ),
			["material"] = new( new(), ["texture"] ),
			["texture"] = new( new(), [] )
		};
		Assert.AreEqual( new DependencySummary( 3, false ), CalculateDependencySummary( ["SCENE", "scene"], graph ) );
		Assert.AreEqual( new DependencySummary( 1, false ), CalculateDependencySummary( ["material"], graph ) );
	}

	[TestMethod]
	public void GroupCountsExcludeAllSelectedRoots()
	{
		var graph = new Dictionary<string, ReviewSizeNode>
		{
			["scene"] = new( new(), ["model", "material"] ),
			["model"] = new( new(), ["material"] ),
			["material"] = new( new(), [] )
		};
		Assert.AreEqual( new DependencySummary( 1, false ), CalculateDependencySummary( ["scene", "model"], graph ) );
		Assert.AreEqual( new DependencySummary( 0, false ), CalculateDependencySummary( graph.Keys, graph ) );
	}

	[TestMethod]
	public void RawInputsAndMultipleOutputFilesDoNotInflateAssetCount()
	{
		var graph = new Dictionary<string, ReviewSizeNode>
		{
			["resource"] = new( new() { ["resource_c"] = 100 }, ["image", "input:image.png"] ),
			["image"] = new( new() { ["image.png"] = 100, ["image.vtex_c"] = 200 }, [] ),
			["input:image.png"] = new( new() { ["image.png"] = 100 }, [], IsAsset: false ),
			["loose.txt"] = new( new(), [], IsAsset: false )
		};
		Assert.AreEqual( new DependencySummary( 1, false ), CalculateDependencySummary( ["resource"], graph ) );
		Assert.AreEqual( new DependencySummary( 0, false ), CalculateDependencySummary( ["loose.txt"], graph ) );
	}

	[TestMethod]
	public void PublishedDependenciesStillContributeToCount()
	{
		var graph = new Dictionary<string, ReviewSizeNode>
		{
			["root"] = new( new() { ["root_c"] = 100 }, ["published"] ),
			["published"] = new( new() { ["published_c"] = 200 }, [] )
		};
		Assert.AreEqual( 0L, CalculatePublishImpact( ["root"], graph, ["published_c"] ).TransitiveBytes );
		Assert.AreEqual( new DependencySummary( 1, false ), CalculateDependencySummary( ["root"], graph ) );
	}

	[TestMethod]
	public void MissingMetadataIsMarkedAsALowerBound()
	{
		var graph = new Dictionary<string, ReviewSizeNode>
		{
			["root"] = new( new(), ["missing", "incomplete"] ),
			["incomplete"] = new( new(), [], Incomplete: true )
		};
		Assert.AreEqual( new DependencySummary( 2, true ), CalculateDependencySummary( ["root"], graph ) );
		Assert.AreEqual( new DependencySummary( 0, true ), CalculateDependencySummary( ["missing"], graph ) );
		Assert.AreEqual( new DependencySummary( 0, true ), CalculateDependencySummary( ["incomplete"], graph ) );
		Assert.AreEqual( new DependencySummary( 0, false ), CalculateDependencySummary( [], graph ) );
	}

	[TestMethod]
	public void DependencyCacheIsNotUploaded()
	{
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.GetDependencySummary( ["private/unused.vmdl"] );
		Assert.IsFalse( manifest.ToJson().Contains( "private/unused.vmdl" ) );
	}
}
