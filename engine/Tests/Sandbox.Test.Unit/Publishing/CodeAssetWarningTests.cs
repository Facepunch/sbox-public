using System;
using System.Collections.Generic;
using System.Threading;
using static Editor.ProjectPublisher.PackageManifest;

[TestClass]
public class CodeAssetWarningTests
{
	[TestMethod]
	public void WarningsPreserveSourceLinesAndLocations()
	{
		var references = FindCodeAssetPaths( """
			// Keep the original line numbers and code, including generic types.
			var resources = ResourceLibrary.GetAll<Item>( "items", recursive: false );
			var model = Model.Load(
			    "models/hat.vmdl" );
			""", "Code/Test.cs", resourceTypes: new Dictionary<string, string[]> { ["Item"] = ["*.item"] } );
		var warnings = FindCodeAssetWarnings( references, new Dictionary<string, string[]>
		{
			["items/hat.item"] = ["items/hat.item"],
			["models/hat.vmdl"] = ["models/hat.vmdl"]
		} ).ToArray();
		var query = warnings.Single( x => x.Pattern == "items/*.item" );
		Assert.AreEqual( "Code/Test.cs", query.SourceFile );
		Assert.AreEqual( 2, query.Line );
		Assert.AreEqual( "var resources = ResourceLibrary.GetAll<Item>( \"items\", recursive: false );", query.SourceLine );
		var literal = warnings.Single( x => x.Pattern == "models/hat.vmdl" );
		Assert.AreEqual( 4, literal.Line );
		Assert.AreEqual( "\"models/hat.vmdl\" );", literal.SourceLine );
	}

	[TestMethod]
	public void ExtensionSelectorsAreNotCompleteAssetPaths()
	{
		var references = FindCodeAssetPaths( """
			[ResourceSelect(Extension = ".vmat")]
			public string Material { get; set; } = "materials/test.vmat";
			var extension = ".vmdl";
			""", "Test.cs" ).ToArray();
		CollectionAssert.AreEqual( new[] { "materials/test.vmat" }, references.Select( x => x.Pattern ).ToArray() );
		Assert.IsTrue( references[0].Recommended );
	}

	[TestMethod]
	public void FindsLiteralAndInterpolatedPathsWithoutEvaluatingCode()
	{
		var paths = FindCodeAssetPaths( """"
			var a = Model.Load( "models/hat.vmdl" );
			var b = Model.Load( $"crabs/crab{Game.Random.Int(0, 5)}.vmdl" );
			var c = @"Models\Crab.vmdl";
			var d = """data/items.json""";
			"""", "Code/Test.cs" ).ToArray();
		CollectionAssert.AreEqual( new[] { "models/hat.vmdl", "crabs/crab*.vmdl", "Models/Crab.vmdl", "data/items.json" }, paths.Select( x => x.Pattern ).ToArray(), string.Join( " | ", paths.Select( x => x.Pattern ) ) );
		Assert.AreEqual( 2, paths[1].Line );
		Assert.AreEqual( "Code/Test.cs", paths[1].SourceFile );
	}

	[TestMethod]
	public void IgnoresCommentsPartialExpressionsAndUnrecognizablePaths()
	{
		var paths = FindCodeAssetPaths( """
			// Model.Load( "models/comment.vmdl" );
			/* var comment = "models/other.vmdl"; */
			var a = "models/" + name + ".vmdl";
			var b = $"{name}.vmdl";
			var c = $"models/{name}.{extension}";
			var d = "https://example.com/model.vmdl";
			var e = "../outside.vmdl";
			var f = "C:\models\hat.vmdl";
			""", "Test.cs" );
		Assert.AreEqual( 0, paths.Count() );
	}

	[TestMethod]
	public void WarnsOnlyAboutExcludedMatchesAndDeduplicatesRules()
	{
		var references = FindCodeAssetPaths( """
			var a = "models/used.vmdl";
			var b = $"crabs/crab{number}.vmdl";
			var c = "CRABS/CRAB*.VMDL";
			var d = "missing.vmdl";
			var e = "data/items.json";
			""", "Test.cs" );
		var excluded = new Dictionary<string, string[]>
		{
			["crabs/crab1.vmdl"] = ["crabs/crab1.vmdl", "crabs/crab1.vmdl_c"],
			["crabs/crab2.vmdl"] = ["crabs/crab2.vmdl"],
			["crabs/other.vmdl"] = ["crabs/other.vmdl"],
			["data/items.json"] = ["data/items.json"]
		};
		var warnings = FindCodeAssetWarnings( references, excluded ).ToArray();
		Assert.AreEqual( 2, warnings.Length );
		CollectionAssert.AreEqual( new[] { "crabs/crab1.vmdl", "crabs/crab2.vmdl" }, warnings.Single( x => x.Pattern == "crabs/crab*.vmdl" ).ExcludedPaths );
		CollectionAssert.AreEqual( new[] { "data/items.json" }, warnings.Single( x => x.Pattern == "data/items.json" ).ExcludedPaths );
		Assert.AreEqual( 4, excluded.Count ); // Diagnostics don't alter the selection.
	}

	[TestMethod]
	public void SuggestedRulesIncludeEveryMatchOnNextSelection()
	{
		var rules = AddResourceRules( "// Keep these\nui/logo.png", ["crabs/crab*.vmdl"] );
		rules = AddResourceRules( rules, ["CRABS\\CRAB*.VMDL"] );
		Assert.AreEqual( "// Keep these\nui/logo.png\ncrabs/crab*.vmdl", rules );
		var assets = new[] { "crabs/crab0.vmdl", "crabs/crab5.vmdl", "models/unused.vmdl" };
		var roots = FindPublishRoots( assets, x => [x], [], rules );
		CollectionAssert.AreEquivalent( assets.Take( 2 ).ToArray(), roots.ToArray() );
		Assert.AreEqual( "*.item", AddResourceRules( "", ["*.item"] ) );
	}

	[TestMethod]
	[DataRow( "../outside.vmdl" )]
	[DataRow( "models/a.vmdl\n*.vmdl" )]
	[DataRow( "// ignored.vmdl" )]
	[DataRow( "https://example.com/a.vmdl" )]
	public void RuleActionRejectsInvalidOrMultipleRules( string rule )
	{
		Assert.ThrowsException<ArgumentException>( () => AddResourceRules( "", [rule] ) );
	}

	interface IItem { }
	class Item : IItem { }
	class Weapon : Item { }

	[TestMethod]
	public void GetAllWarnsForConcreteBaseAndInterfaceTypes()
	{
		var types = GetResourceTypePatterns( [(typeof( Weapon ), "weapon"), (typeof( Item ), "item")] );
		var paths = FindCodeAssetPaths( """
			var a = ResourceLibrary.GetAll<Weapon>();
			var b = Sandbox.ResourceLibrary.GetAll<Item>();
			var c = Game.Resources.GetAll<IItem>();
			var d = SomethingElse.GetAll<Weapon>();
			var e = ResourceLibrary.GetAll<Unknown>();
			""", "Test.cs", resourceTypes: types ).ToArray();
		CollectionAssert.AreEquivalent( new[] { "*.weapon", "*.item", "*.weapon", "*.item", "*.weapon" }, paths.Select( x => x.Pattern ).ToArray() );
		Assert.IsTrue( paths.All( x => x.Usage.StartsWith( "GetAll<" ) ) );
		var warnings = FindCodeAssetWarnings( paths, new Dictionary<string, string[]> { ["items/gun.weapon"] = ["items/gun.weapon"] } ).ToArray();
		Assert.AreEqual( 1, warnings.Length );
		Assert.AreEqual( "*.weapon", warnings[0].Pattern );
	}

	[TestMethod]
	public void GetAllRecognizesQualifiedTypesAndLiteralFolderOverload()
	{
		var types = GetResourceTypePatterns( [(typeof( Weapon ), "weapon")] );
		var paths = FindCodeAssetPaths( """
			var a = global::Sandbox.ResourceLibrary.GetAll<CodeAssetWarningTests.Weapon>( "items/weapons" );
			var b = ResourceLibrary.GetAll<Weapon>( folder );
			""", "Test.cs", resourceTypes: types ).ToArray();
		Assert.AreEqual( 1, paths.Length );
		Assert.AreEqual( "items/weapons/*.weapon", paths[0].Pattern );
	}

	[TestMethod]
	public void GetAllWithNonRecursiveFolderDoesNotWarnAboutSubfolders()
	{
		var types = GetResourceTypePatterns( [(typeof( Weapon ), "weapon")] );
		var references = FindCodeAssetPaths( """
				var a = ResourceLibrary.GetAll<Weapon>( recursive: false, filepath: "items" );
				""", "Test.cs", resourceTypes: types );
		var warnings = FindCodeAssetWarnings( references, new Dictionary<string, string[]>
		{
			["items/gun.weapon"] = ["items/gun.weapon"],
			["items/nested/gun.weapon"] = ["items/nested/gun.weapon"]
		} ).ToArray();
		CollectionAssert.AreEqual( new[] { "items/gun.weapon" }, warnings.Single().ExcludedPaths );
	}

	[TestMethod]
	public void ReviewInformationIsNotSerializedIntoPublishedManifest()
	{
		var manifest = new Editor.ProjectPublisher.PackageManifest();
		manifest.ExcludedAssets.Add( "secret.vmdl" );
		manifest.CodeAssetWarnings.Add( new( "*.vmdl", "C:/source.cs", 12, ["secret.vmdl"] ) );
		var json = manifest.ToJson();
		Assert.IsFalse( json.Contains( "secret.vmdl" ) );
		Assert.IsFalse( json.Contains( "C:/source.cs" ) );
	}

	[TestMethod]
	public void ScanningHonorsCancellation()
	{
		using var cancel = new CancellationTokenSource();
		cancel.Cancel();
		Assert.ThrowsException<OperationCanceledException>( () => FindCodeAssetPaths( "var a = \"a.vmdl\";", "Test.cs", cancel.Token ).ToArray() );
	}
}
