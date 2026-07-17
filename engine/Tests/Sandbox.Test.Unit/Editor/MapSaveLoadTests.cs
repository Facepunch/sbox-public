namespace EditorTests;

[TestClass]
public class MapSaveLoadTests
{
	[TestMethod]
	public void MergePackageReferencesKeepsManualAndDetectedReferences()
	{
		var result = Editor.MapEditor.Hammer.MergePackageReferences(
			new[] { "facepunch.manual", "facepunch.manual" },
			new[] { "Facepunch.Decals", "facepunch.props#12" },
			new[] { "facepunch.addon" }
		);

		CollectionAssert.AreEqual(
			new[] { "facepunch.manual", "Facepunch.Decals", "facepunch.props#12", "facepunch.addon" },
			result
		);
	}
}
