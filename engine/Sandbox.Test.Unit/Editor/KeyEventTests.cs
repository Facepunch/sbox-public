using Editor;

namespace EditorTests;

[TestClass]
public class KeyEventTests
{
	[TestMethod]
	public void AltGrUsesRightAltKeyName()
	{
		Assert.AreEqual( "RAlt", KeyEvent.GetKeyName( KeyCode.AltGr, 0xA5, "" ) );
	}
}
