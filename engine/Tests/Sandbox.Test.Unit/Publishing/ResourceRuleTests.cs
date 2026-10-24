using System;
using System.Collections.Generic;
using Editor;
using static Editor.ProjectPublisher.PackageManifest;

[TestClass]
public class ResourceRuleTests
{
	[TestMethod]
	public void LiteralPropertiesAndArraysRecommendExistingMatchesOnly()
	{
		var references = FindCodeAssetPaths( """
			class HydraulicTool
			{
				protected override string CapModelA => "hydraulics/tool_hydraulic.vmdl";
				string[] Materials = ["hydraulics/metal_tile_line.vmat", "missing.vmat"];
				// "models/comment.vmdl"
			}
			""", "HydraulicTool.cs" );
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.CodeAssetWarnings.AddRange( FindCodeAssetWarnings( references, new Dictionary<string, string[]>
		{
			["hydraulics/tool_hydraulic.vmdl"] = ["hydraulics/tool_hydraulic.vmdl"],
			["hydraulics/metal_tile_line.vmat"] = ["hydraulics/metal_tile_line.vmat"],
			["models/comment.vmdl"] = ["models/comment.vmdl"]
		} ) );
		CollectionAssert.AreEquivalent( new[] { "hydraulics/tool_hydraulic.vmdl", "hydraulics/metal_tile_line.vmat" }, manifest.RecommendedIncludes );
	}

	[TestMethod]
	public void ExactDefaultsAreRecommendedWithOrWithoutResourceAttributes()
	{
		var references = FindCodeAssetPaths( """
			class Example
			{
				[Property, ResourceSelect(Extension = "bdef")]
				public string Definition { get; set; } = "entities/balloon/basic.bdef";
				[Sandbox.ResourceType("vmdl")]
				public string Model = "models/hat.vmdl";
				[ResourceSelectAttribute(Extension = ".vmat")]
				public string Material { get; set; } = "materials/hat.vmat";
				[ResourceSelect(Extension = "bdef")]
				public string Mismatch { get; set; } = "models/other.vmdl";
				public string Incidental { get; set; } = "models/incidental.vmdl";
				[ResourceSelect(Extension = "vmdl")]
				public string Dynamic { get; set; } = $"models/{Name}.vmdl";
			}
			""", "Test.cs" ).ToArray();
		CollectionAssert.AreEquivalent( new[] { "entities/balloon/basic.bdef", "models/hat.vmdl", "materials/hat.vmat", "models/other.vmdl", "models/incidental.vmdl" },
			references.Where( x => x.Recommended ).Select( x => x.Pattern ).ToArray() );
		Assert.AreEqual( "Default Definition: entities/balloon/basic.bdef", references.Single( x => x.Pattern.EndsWith( ".bdef" ) ).Usage );
	}

	[TestMethod]
	public void RecommendsAllExactLiteralsButNotGuessedPatterns()
	{
		var references = FindCodeAssetPaths( """
			var incidental = "models/hat.vmdl";
			var model = Model.Load( "models/hat.vmdl" );
			var material = global::Sandbox.Material.Load( filename: "materials/hat.vmat" );
			var texture = Texture.Load( "ui/hat.png" );
			var resource = Game.Resources.Get<Item>( filepath: "items/hat.item" );
			var another = ResourceLibrary.Get<Item>( "items/other.item" );
			var custom = Custom.Load( "models/custom.vmdl" );
			var dynamic = Model.Load( $"models/hat{index}.vmdl" );
			var pattern = Model.Load( "models/*.vmdl" );
			var relative = Texture.Load( filesystem, "custom/hat.png" );
			""", "Test.cs" ).ToArray();
		CollectionAssert.AreEquivalent( new[] { "models/hat.vmdl", "models/hat.vmdl", "materials/hat.vmat", "ui/hat.png", "items/hat.item", "items/other.item", "models/custom.vmdl", "custom/hat.png" },
			references.Where( x => x.Recommended ).Select( x => x.Pattern ).ToArray() );
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.CodeAssetWarnings.AddRange( FindCodeAssetWarnings( references, new Dictionary<string, string[]>
		{
			["models/hat.vmdl"] = ["models/hat.vmdl"],
			["models/hat2.vmdl"] = ["models/hat2.vmdl"],
			["models/custom.vmdl"] = ["models/custom.vmdl"]
		} ) );
		CollectionAssert.AreEqual( new[] { "models/custom.vmdl", "models/hat.vmdl" }, manifest.RecommendedIncludes );
	}

	[TestMethod]
	public void QueriesRemainScopedWarningsWithoutBulkRecommendations()
	{
		var types = new Dictionary<string, string[]> { ["Item"] = ["*.item"] };
		var references = FindCodeAssetPaths( """
			var items = ResourceLibrary.GetAll<Item>( "items", recursive: false );
			var unknown = Game.Resources.GetAll<Item>( "other", recursive: recursive );
			""", "Test.cs", resourceTypes: types );
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.CodeAssetWarnings.AddRange( FindCodeAssetWarnings( references, new Dictionary<string, string[]>
		{
			["items/hat.item"] = ["items/hat.item"],
			["items/nested/hat.item"] = ["items/nested/hat.item"],
			["other/hat.item"] = ["other/hat.item"]
		} ) );
		Assert.AreEqual( 2, manifest.CodeAssetWarnings.Count );
		CollectionAssert.AreEqual( new[] { "items/hat.item" }, manifest.CodeAssetWarnings.Single( x => x.Pattern == "items/*.item" ).ExcludedPaths );
		Assert.AreEqual( 0, manifest.RecommendedIncludes.Length );
		Assert.IsTrue( manifest.CodeAssetWarnings.All( x => !x.Recommended ) );
	}

	[TestMethod]
	public void GenericPickerAndSceneLookupCannotRecommendAllAssets()
	{
		var types = new Dictionary<string, string[]>
		{
			["Resource"] = ["*.vmdl", "*.scene"],
			["SceneFile"] = ["*.scene"]
		};
		var references = FindCodeAssetPaths( """
			var picker = ResourceLibrary.GetAll<Resource>().Where( x => HasExtension( x.ResourcePath ) );
			var scene = ResourceLibrary.GetAll<SceneFile>().FirstOrDefault( x => x.Id == sceneFile.Id );
			var model = Model.Load( "models/needed.vmdl" );
			""", "Test.cs", resourceTypes: types );
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.CodeAssetWarnings.AddRange( FindCodeAssetWarnings( references, new Dictionary<string, string[]>
		{
			["models/needed.vmdl"] = ["models/needed.vmdl"],
			["models/unused.vmdl"] = ["models/unused.vmdl"],
			["scenes/backup.scene"] = ["scenes/backup.scene"]
		} ) );
		Assert.IsTrue( manifest.CodeAssetWarnings.Any( x => x.Pattern == "*.scene" && !x.Recommended ) );
		Assert.IsTrue( manifest.CodeAssetWarnings.Any( x => x.Pattern == "*.vmdl" && !x.Recommended ) );
		CollectionAssert.AreEqual( new[] { "models/needed.vmdl" }, manifest.RecommendedIncludes );
		Assert.AreEqual( 3, manifest.CodeAssetWarnings.Single( x => x.Recommended ).Line );
		CollectionAssert.AreEqual( new[] { "models/needed.vmdl" }, CompactRecommendedRules( manifest.RecommendedIncludes,
			["models/needed.vmdl", "models/unused.vmdl", "scenes/backup.scene"], [] ) );
	}

	[TestMethod]
	public void HundredsOfRecommendationsCollapseToTheNarrowestSafeFolder()
	{
		var files = Enumerable.Range( 0, 800 ).Select( x => $"items/weapons/weapon{x}.item" ).ToArray();
		var rules = CompactRecommendedRules( files, files.Append( "ui/logo.png" ), [] );
		CollectionAssert.AreEqual( new[] { "items/weapons/*.item" }, rules );
		CollectionAssert.AreEquivalent( files, FindPublishRoots( files, x => [x], [], string.Join( "\n", rules ) ).ToArray() );
	}

	[TestMethod]
	public void WildcardsNeverSweepInUnrecommendedFilesOrSubfolders()
	{
		string[] targets = ["items/one.item", "items/two.item", "weapons/a.item", "weapons/b.item"];
		string[] files = [.. targets, "items/dev/unused.item", "weapons/test.scene"];
		var rules = CompactRecommendedRules( targets, files, [] );
		CollectionAssert.AreEqual( new[] { "items/one.item", "items/two.item", "weapons/*.item" }, rules );
		CollectionAssert.AreEquivalent( targets, FindPublishRoots( files, x => [x], [], string.Join( "\n", rules ) ).ToArray() );
	}

	[TestMethod]
	public void CompactionAllowsAlreadyIncludedFilesAndKeepsTypesSeparate()
	{
		string[] targets = ["/Models/A.vmdl", "models/b.vmdl", "models/c.vmat"];
		string[] files = ["models/a.vmdl", "models/b.vmdl", "models/c.vmat", "models/existing.vmdl", "models/unused.vmat"];
		var rules = CompactRecommendedRules( targets, files, ["MODELS/existing.vmdl"] );
		CollectionAssert.AreEquivalent( new[] { "models/*.vmdl", "models/c.vmat" }, rules );
		CollectionAssert.AreEquivalent( new[] { "models/a.vmdl", "models/b.vmdl", "models/c.vmat", "models/existing.vmdl" },
			FindPublishRoots( files, x => [x], [], string.Join( "\n", rules ) ).ToArray() );
	}

	[TestMethod]
	public void ExactRuleLookupRetainsWildcardAndCaseSemantics()
	{
		var rules = string.Join( "\n", Enumerable.Range( 0, 800 ).Select( x => $"models/hat{x}.vmdl" ) ) + "\nUI/icon_?.png\n// comment";
		var matcher = new ResourceRuleMatcher( rules );
		Assert.IsTrue( matcher.Matches( "/MODELS/HAT799.VMDL" ) );
		Assert.IsFalse( matcher.Matches( "/models/hat800.vmdl" ) );
		Assert.IsTrue( matcher.Matches( "/ui/icon_1.png" ) );
		Assert.IsFalse( matcher.Matches( "/ui/icon_12.png" ) );
	}

	[TestMethod]
	public void CleanupPreservesCommentsUnmatchedRulesAndTheIncludedFileSet()
	{
		const string rules = "// Runtime UI\r\nUI/*\r\n/ui/weapons/*\r\nUI/logo.png\r\nui/LOGO.PNG\r\n\r\nfuture/*.item\r\nicons/icon_?.png\r\nicons/icon_1.png\r\nitems/unique.item";
		var cleanup = CleanResourceRules( rules );
		Assert.AreEqual( 4, cleanup.Removed );
		Assert.AreEqual( "// Runtime UI\r\nUI/*\r\n\r\nfuture/*.item\r\nicons/icon_?.png\r\nitems/unique.item", cleanup.Rules );
		string[] assets = ["ui/logo.png", "ui/weapons/gun.png", "ui/weapons/nested/gun.png", "future/new.item", "icons/icon_1.png", "icons/icon_12.png", "items/unique.item", "items/other.item"];
		CollectionAssert.AreEquivalent( FindPublishRoots( assets, x => [x], [], rules ).ToArray(),
			FindPublishRoots( assets, x => [x], [], cleanup.Rules ).ToArray() );
		Assert.AreEqual( 0, CleanResourceRules( cleanup.Rules ).Removed );
	}

	[TestMethod]
	public void CleanupDoesNotGuessThatIntersectingWildcardsAreRedundant()
	{
		const string rules = "icons/*.png\nicons/icon_?.*\nfuture/new.item\n// Keep this\n";
		Assert.AreEqual( rules, CleanResourceRules( rules ).Rules );
		Assert.AreEqual( 0, CleanResourceRules( rules ).Removed );
		Assert.AreEqual( 0, CleanResourceRules( null ).Removed );
	}

	[TestMethod]
	public void BulkRulesDeduplicateAliasesAndRetainExistingComments()
	{
		var rules = AddResourceRules( "// Keep\r\nui/logo.png", ["/UI/LOGO.PNG", "models/hat.vmdl", @"models\hat.vmdl"] );
		Assert.AreEqual( "// Keep\r\nui/logo.png\r\nmodels/hat.vmdl", rules );
	}
}
