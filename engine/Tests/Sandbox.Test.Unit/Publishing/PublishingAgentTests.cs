using Editor;
using Editor.Mcp;
using System.Text.Json;
using static Editor.ProjectPublisher.PackageManifest;

[TestClass]
public class PublishingAgentTests
{
	[TestMethod]
	public void ReportPreservesProposedRulesWarningsAndUnknownCosts()
	{
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.Assets.Add( new() { Name = "scenes/start.scene_c", Size = 100 } );
		manifest.Assets.Add( new() { Name = "models/hat.vmdl_c", Size = 50 } );
		manifest.IncludedExclusionReasons["models/hat.vmdl_c"] = "Required by the start scene";
		manifest.ExcludedAssets.Add( "models/dev.vmdl" );
		manifest.ExcludedByRules["models/dev.vmdl"] = "!models/dev*";
		manifest.SceneWarnings.Add( new( "scenes/missing.scene", true ) );
		manifest.CodeAssetWarnings.Add( new( "models/dev.vmdl", "Code/Test.cs", 12, ["models/dev.vmdl"],
			ExclusionRule: "!models/dev*", SourceLine: "var model = Model.Load( \"models/dev.vmdl\" );" ) );
		manifest.Errors.Add( "Missing required content" );
		var options = new PublishOptions( true, "models/*\n!models/dev*" );

		var report = PublishingTools.CreateReport( manifest, "test.sbproj", options );
		using var json = JsonDocument.Parse( JsonSerializer.Serialize( report ) );
		var data = json.RootElement;
		Assert.AreEqual( 150L, data.GetProperty( "IncludedBytes" ).GetInt64() );
		Assert.AreEqual( 2, data.GetProperty( "WarningCount" ).GetInt32() );
		Assert.AreEqual( options.Resources, data.GetProperty( "Options" ).GetProperty( "Resources" ).GetString() );
		Assert.AreEqual( "Required by the start scene", data.GetProperty( "Included" )[1].GetProperty( "ExclusionOverride" ).GetString() );
		var excluded = data.GetProperty( "Excluded" )[0];
		Assert.AreEqual( JsonValueKind.Null, excluded.GetProperty( "Bytes" ).ValueKind );
		Assert.AreEqual( JsonValueKind.Null, excluded.GetProperty( "AdditionalImpact" ).GetProperty( "TotalBytes" ).ValueKind );
		Assert.AreEqual( "!models/dev*", excluded.GetProperty( "ExclusionRule" ).GetString() );
		Assert.IsTrue( excluded.GetProperty( "Dependencies" ).GetProperty( "Incomplete" ).GetBoolean() );
		var source = data.GetProperty( "CodeWarnings" )[0].GetProperty( "Source" );
		Assert.AreEqual( 12, source.GetProperty( "Line" ).GetInt32() );
		Assert.AreEqual( manifest.CodeAssetWarnings[0].SourceLine, source.GetProperty( "SourceLine" ).GetString() );
		Assert.AreEqual( "Missing required content", data.GetProperty( "Errors" )[0].GetString() );
	}
}
