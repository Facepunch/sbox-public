using Editor;
using System;
using System.Collections.Generic;
using System.IO;
using static Editor.ProjectPublisher.PackageManifest;

[TestClass]
public class ResourceExclusionTests
{
	static string[] Select( string rules, params string[] paths ) => FindPublishRoots( paths, x => [x], [], rules ).Order().ToArray();

	[TestMethod]
	public void OrderedRulesExcludeDevelopmentScenesAndAllowSpecificExceptions()
	{
		const string rules = "scenes/*.scene\n!scenes/dev*.scene\nscenes/dev_tutorial.scene";
		CollectionAssert.AreEqual( new[] { "scenes/dev_tutorial.scene", "scenes/main.scene" },
			Select( rules, "scenes/main.scene", "scenes/dev_test.scene", "scenes/dev_tutorial.scene", "other.scene" ) );
		Assert.AreEqual( 0, Select( "!*.scene", "scenes/main.scene" ).Length );
		Assert.AreEqual( 1, Select( "!*.scene\n*.scene", "scenes/main.scene" ).Length );
	}

	[TestMethod]
	public void CommentsFoldersCaseAndBackslashesWorkForExclusions()
	{
		const string rules = " # notes\r\nscenes/\r\n // notes\r\n ! /SCENES\\TESTS/ \r\n";
		CollectionAssert.AreEqual( new[] { "scenes/main.scene" }, Select( rules, "scenes/main.scene", "scenes/tests/one.scene", "scenes/tests/nested/two.scene" ) );
	}

	[TestMethod]
	public void LastMatchAcrossAliasesPreventsCompiledPathsBypassingExclusions()
	{
		string[] aliases = ["scenes/dev.scene", "scenes/dev.scene_c"];
		Assert.AreEqual( 0, FindPublishRoots( new[] { "asset" }, _ => aliases, [], "scenes/*\n!scenes/dev.scene" ).Count );
		Assert.AreEqual( 1, FindPublishRoots( new[] { "asset" }, _ => aliases, [], "scenes/*\n!scenes/dev.scene\nscenes/dev.scene_c" ).Count );
		Assert.AreEqual( 0, FindPublishRoots( new[] { "asset" }, _ => aliases, [], "scenes/dev.scene_c\n!scenes/dev.scene" ).Count );
	}

	[TestMethod]
	public async Task ConfiguredResourcesAndTransitiveDependenciesCannotBeStrippedByRules()
	{
		string[] assets = ["scenes/start.scene", "resources/item.item", "models/required.vmdl", "scenes/dev.scene"];
		var roots = FindPublishRoots( assets, x => [x], ["scenes/start.scene"], "*\n!*" );
		CollectionAssert.AreEquivalent( new[] { "scenes/start.scene", "resources/item.item" }, roots.ToArray() );
		var used = await FindReachableAssets( roots, path => Task.FromResult<IEnumerable<string>>( path == "scenes/start.scene" ? ["models/required.vmdl"] : [] ) );
		CollectionAssert.AreEquivalent( assets.Take( 3 ).ToArray(), used.ToArray() );
	}

	[TestMethod]
	public async Task LooseFilesUseTheSameOrderedSelection()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"publish_exclusions_{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( directory, "scenes" ) );
		try
		{
			foreach ( var name in new[] { "main.scene", "dev_test.scene", "dev_tutorial.scene" } ) File.WriteAllText( Path.Combine( directory, "scenes", name ), "{}" );
			var manifest = new ProjectPublisher.PackageManifest();
			await manifest.IncludeFiles( directory, "scenes/*.scene\n!scenes/dev*.scene\nscenes/dev_tutorial.scene", default );
			CollectionAssert.AreEquivalent( new[] { "scenes/main.scene", "scenes/dev_tutorial.scene" }, manifest.Assets.Select( x => x.Name ).ToArray() );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public void ManualIncludeAppendsAnOverrideEvenWhenAnEarlierIncludeExists()
	{
		var rules = AddResourceRules( "scenes/main.scene\n!scenes/*.scene", ["SCENES/main.scene"] );
		Assert.AreEqual( "scenes/main.scene\n!scenes/*.scene\nSCENES/main.scene", rules );
		Assert.AreEqual( rules, AddResourceRules( rules, ["scenes/main.scene"] ) );
		var all = AddResourceRules( "*.scene\n!scenes/dev*.scene", ["*.scene"] );
		Assert.AreEqual( "*.scene\n!scenes/dev*.scene\n*.scene", all );
		Assert.AreEqual( 1, Select( all, "scenes/dev_test.scene" ).Length );
	}

	[TestMethod]
	public void ManualIncludeOverridesAnExclusionMatchingAnotherAssetAlias()
	{
		var rules = AddResourceRules( "scenes/main.scene\nscenes/*\n!scenes/main.scene_c", ["scenes/main.scene"] );
		Assert.AreEqual( 1, FindPublishRoots( new[] { "asset" }, _ => ["scenes/main.scene", "scenes/main.scene_c"], [], rules ).Count );
		Assert.IsTrue( rules.EndsWith( "!scenes/main.scene_c\nscenes/main.scene" ) );
	}

	[TestMethod]
	public void RuleActionsAcceptFolderAndPrefixExclusions()
	{
		var rules = AddResourceRules( "scenes/*", ["!scenes/tests/", "!scenes/dev*", "scenes/dev_tutorial.scene"] );
		CollectionAssert.AreEquivalent( new[] { "scenes/main.scene", "scenes/dev_tutorial.scene" },
			Select( rules, "scenes/main.scene", "scenes/dev_test.scene", "scenes/tests/one.scene", "scenes/dev_tutorial.scene" ) );
	}

	[TestMethod]
	public void CleanupNeverChangesOrderedSelectionAcrossMixedRules()
	{
		string[] choices = ["*.scene", "!scenes/dev*.scene", "scenes/dev_test.scene", "!scenes/dev_test.scene", "scenes/", "!scenes/", "# comment", "scenes/main.scene"];
		string[] paths = ["scenes/dev_test.scene", "scenes/main.scene", "scenes/dev_other.scene", "other.scene", "scenes/foo.json", "scenes/nested/dev_test.scene"];
		var random = new Random( 1234 );
		for ( var i = 0; i < 256; i++ )
		{
			var rules = string.Join( "\n", Enumerable.Range( 0, 8 ).Select( _ => choices[random.Next( choices.Length )] ) );
			var cleaned = CleanResourceRules( rules );
			CollectionAssert.AreEqual( Select( rules, paths ), Select( cleaned.Rules, paths ), rules );
		}
		Assert.AreEqual( "# Keep\n!scenes/dev*.scene\n*.scene", CleanResourceRules( "*.scene\n# Keep\n!scenes/dev*.scene\n*.scene" ).Rules );
	}

	[TestMethod]
	public void ExplicitExclusionsRemainWarningsButNeverBulkRecommendations()
	{
		var warnings = RespectRuleExclusions( [
			new( "scenes/dev.scene", "Code.cs", 1, ["scenes/dev.scene"], Recommended: true ),
			new( "models/hat.vmdl", "Code.cs", 2, ["models/hat.vmdl"], Recommended: true )
		], new Dictionary<string, string> { ["scenes/dev.scene"] = "!scenes/dev*.scene" } ).ToArray();
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.CodeAssetWarnings.AddRange( warnings );
		Assert.AreEqual( "!scenes/dev*.scene", warnings[0].ExclusionRule );
		Assert.IsFalse( warnings[0].Recommended );
		CollectionAssert.AreEqual( new[] { "models/hat.vmdl" }, manifest.RecommendedIncludes );
	}

	[TestMethod]
	public void OverrideReasonsDistinguishConfiguredResourcesAndDependenciesAndStayLocal()
	{
		Assert.AreEqual( "Referenced by a configured scene or project setting.", ExclusionOverrideReason( ["SCENES/start.scene", "scenes/start.scene_c"], ["scenes/start.scene"] ) );
		Assert.AreEqual( "Included by the Resources folder.", ExclusionOverrideReason( ["resources/test.item"], [] ) );
		Assert.AreEqual( "Required by included content or automatic publishing rules.", ExclusionOverrideReason( ["models/hat.vmdl"], [] ) );
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.ExclusionOverrides.Add( new( "private/file.vmdl", "!*.vmdl", "Required", 10 ) );
		manifest.ExcludedByRules["private/file.vmdl"] = "!*.vmdl";
		manifest.IncludedExclusionReasons["private/file.vmdl_c"] = "Required";
		Assert.IsFalse( manifest.ToJson().Contains( "private/file" ) );
	}

	[TestMethod]
	public void ExactLookupsStillRespectLaterWildcards()
	{
		var rules = string.Join( "\n", Enumerable.Range( 0, 800 ).Select( x => $"items/{x}.item" ) ) + "\n!items/*.item\nitems/2.item";
		var matcher = new ResourceRuleMatcher( rules );
		Assert.IsTrue( matcher.Matches( "/items/2.item" ) );
		Assert.IsFalse( matcher.Matches( "/items/799.item" ) );
		Assert.AreEqual( "!items/*.item", matcher.LastMatch( "/items/0.item" ).Text );
	}
}
