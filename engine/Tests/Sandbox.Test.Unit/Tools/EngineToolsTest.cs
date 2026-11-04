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
			Editor.EngineTools.SetUnavailable( library );

			Assert.IsFalse( Editor.EngineTools.IsAvailable( library ) );
			Assert.IsTrue( Editor.EngineTools.All.Any( x => x.Library == library ) );
		}
		finally
		{
			Editor.EngineTools.SetAvailable( library );
		}
	}

	[TestMethod]
	public void UnavailableNativeEditorHasPlatformAppropriateMessage()
	{
		var expected = System.OperatingSystem.IsWindows()
			? "The native editor library couldn't be loaded."
			: "Native tools aren't supported on non-Windows builds.";

		Assert.AreEqual( expected, Editor.EngineTools.GetUnavailableMessage() );
	}
}
