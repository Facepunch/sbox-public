namespace ToolsTests;

[TestClass]
[DoNotParallelize]
public class EngineToolsTest
{
	[TestMethod]
	public void UnavailableNativeEditorRemainsListed()
	{
		const string library = "modeldoc_editor";

		try
		{
			Editor.EngineTools.SetUnavailable( library, "missing native library" );

			Assert.IsFalse( Editor.EngineTools.IsAvailable( library ) );
			Assert.IsTrue( Editor.EngineTools.All.Any( x => x.Library == library ) );
		}
		finally
		{
			Editor.EngineTools.SetAvailable( library );
		}
	}
}
