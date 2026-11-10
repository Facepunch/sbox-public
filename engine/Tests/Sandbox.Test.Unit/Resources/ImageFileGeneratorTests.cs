using Sandbox.Resources;
using System.Text.Json.Nodes;

namespace ResourceTests;

[TestClass]
public class ImageFileGeneratorTests
{
	[TestMethod]
	public void CompressedByDefault()
	{
		Assert.IsNull( new ImageFileGenerator().FormatOverride );
	}

	[TestMethod]
	public void LosslessStoresRgba()
	{
		Assert.AreEqual( ImageFormat.RGBA8888, new ImageFileGenerator { Lossless = true }.FormatOverride );
		Assert.IsNull( new ImageFileGenerator { Lossless = true, ConvertHeightToNormals = true }.FormatOverride );
	}

	[TestMethod]
	public void SavedBeforeOptInStaysLossless()
	{
		var generator = new ImageFileGenerator();
		generator.Deserialize( new JsonObject { ["FilePath"] = "a.png" } );
		Assert.IsTrue( generator.Lossless );
	}

	[TestMethod]
	public void SavedSettingRoundTrips()
	{
		var generator = new ImageFileGenerator();
		generator.Deserialize( Json.ToNode( new ImageFileGenerator { FilePath = "a.png" } ) as JsonObject );
		Assert.IsFalse( generator.Lossless );
	}
}
