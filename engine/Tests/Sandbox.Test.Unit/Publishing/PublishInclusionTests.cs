using System;
using System.Collections.Generic;
using static Editor.ProjectPublisher.PackageManifest;

[TestClass]
public class PublishInclusionTests
{
	[TestMethod]
	public void ReasonsFollowIncludedParentsThroughSharedAndCyclicDependencies()
	{
		var graph = new Dictionary<string, ReviewSizeNode>( StringComparer.OrdinalIgnoreCase )
		{
			["start.scene"] = new( new() { ["start.scene_c"] = 1 }, ["prop.vmdl"] ),
			["dev.scene"] = new( new() { ["dev.scene_c"] = 1 }, ["prop.vmdl"] ),
			["prop.vmdl"] = new( new() { ["prop.vmdl_c"] = 1 }, ["surface.vmat"] ),
			["surface.vmat"] = new( new() { ["surface.vmat_c"] = 1 }, ["prop.vmdl"] )
		};
		var aliases = graph.Keys.SelectMany( x => new[] { (Path: x, Asset: x), (Path: x + "_c", Asset: x) } )
			.ToDictionary( x => x.Path, x => x.Asset, StringComparer.OrdinalIgnoreCase );
		var reasons = CalculateInclusionReasons( ["start.scene_c", "prop.vmdl_c", "surface.vmat_c"], graph, aliases, ["start.scene"], "!*.vmdl", true );
		CollectionAssert.AreEquivalent( new[] { "Referenced by start.scene", "Referenced by surface.vmat" }, reasons["prop.vmdl_c"] );
		CollectionAssert.AreEqual( new[] { "Referenced by prop.vmdl" }, reasons["surface.vmat_c"] );
		CollectionAssert.AreEqual( new[] { "Referenced by a configured scene or project setting." }, reasons["start.scene_c"] );
		Assert.IsFalse( reasons.ContainsKey( "dev.scene_c" ) );
	}

	[TestMethod]
	public void RootReasonsRespectAliasesOrderedRulesResourcesAndRuntimeSidecars()
	{
		var graph = new Dictionary<string, ReviewSizeNode>
		{
			["levels/arena.scene"] = new( new() { ["levels/arena.scene_c"] = 10, ["levels/arena.navdata"] = 3 }, [] )
		};
		var aliases = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
		{
			["levels/arena.scene"] = "levels/arena.scene",
			["levels/arena.scene_c"] = "levels/arena.scene"
		};
		var reasons = CalculateInclusionReasons( ["levels/arena.scene_c", "levels/arena.navdata", "levels/arena.scene_c.t.png", "Resources/data.json"],
			graph, aliases, [], "levels/*\n!levels/*.scene\nlevels/arena.scene_c\n!resources/*", true );
		CollectionAssert.AreEqual( new[] { "Included by rule: levels/arena.scene_c" }, reasons["levels/arena.scene_c"] );
		Assert.IsTrue( reasons["levels/arena.navdata"].Contains( "Runtime file for levels/arena.scene" ) );
		Assert.IsTrue( reasons["levels/arena.scene_c.t.png"].Contains( "Thumbnail for levels/arena.scene" ) );
		CollectionAssert.AreEqual( new[] { "Included by the Resources/ folder." }, reasons["resources/data.json"] );
	}

	[TestMethod]
	public void RuntimeImageInputsExplainLooseFilesWithoutIncludingExcludedParents()
	{
		var graph = new Dictionary<string, ReviewSizeNode>
		{
			["items/item.item"] = new( new() { ["items/item.item_c"] = 1 }, ["input:ui/icon.png"] )
		};
		var aliases = new Dictionary<string, string> { ["items/item.item_c"] = "items/item.item" };
		var reasons = CalculateInclusionReasons( ["items/item.item_c", "ui/icon.png"], graph, aliases, [], "items/*", true );
		CollectionAssert.AreEqual( new[] { "Referenced by items/item.item" }, reasons["ui/icon.png"] );
	}
}
