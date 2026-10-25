using System;
using System.Collections.Generic;
using static Editor.ProjectPublisher.PackageManifest;

[TestClass]
public class PublishEstimateIssueTests
{
	[TestMethod]
	public void SharedGapsAreReportedOnceWithRealPaths()
	{
		var graph = new Dictionary<string, ReviewSizeNode>( StringComparer.OrdinalIgnoreCase )
		{
			["root"] = new( new(), ["missing", "leaf"] ),
			["leaf"] = new( new() { ["unknown:leaf"] = null }, ["missing"], Incomplete: true )
		};
		var issues = FindEstimateIssues( graph );
		Assert.AreEqual( 2, issues.Count );
		Assert.AreEqual( "leaf", issues[0].Path );
		Assert.AreEqual( "missing", issues[1].Path );
	}

	[TestMethod]
	public void KnownRawFilesDoNotCreateUncertainty()
	{
		var graph = new Dictionary<string, ReviewSizeNode>
		{
			["image.png"] = new( new() { ["image.png"] = 42 }, [] ),
			["input:image.png"] = new( new() { ["image.png"] = 42 }, [], IsAsset: false )
		};
		Assert.AreEqual( 0, FindEstimateIssues( graph ).Count );
	}

	[TestMethod]
	public void MissingRawFileSizesReportTheFileInsteadOfSyntheticNode()
	{
		var graph = new Dictionary<string, ReviewSizeNode>
		{
			["input:image.png"] = new( new() { ["image.png"] = null }, [], IsAsset: false )
		};
		var issues = FindEstimateIssues( graph );
		Assert.AreEqual( 1, issues.Count );
		Assert.AreEqual( new EstimateIssue( "image.png", "File size unavailable" ), issues[0] );
	}
}
