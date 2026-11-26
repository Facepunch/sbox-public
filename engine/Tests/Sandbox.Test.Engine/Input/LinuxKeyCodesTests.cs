using NativeEngine;
using Sandbox.Engine;

namespace EngineTests;

[TestClass]
public class LinuxKeyCodesTests
{
	[TestMethod]
	public void MapAsSdlDoes()
	{
		Assert.AreEqual( ButtonCode.KEY_W, KeyTranslation.ScanCodeToButtonCode( LinuxKeyCodes.ToScanCode( 17 ) ) );
		Assert.AreEqual( ButtonCode.KEY_1, KeyTranslation.ScanCodeToButtonCode( LinuxKeyCodes.ToScanCode( 2 ) ) );
		Assert.AreEqual( ButtonCode.KEY_0, KeyTranslation.ScanCodeToButtonCode( LinuxKeyCodes.ToScanCode( 11 ) ) );
		Assert.AreEqual( ButtonCode.KEY_F1, KeyTranslation.ScanCodeToButtonCode( LinuxKeyCodes.ToScanCode( 59 ) ) );
		Assert.AreEqual( ButtonCode.KEY_F12, KeyTranslation.ScanCodeToButtonCode( LinuxKeyCodes.ToScanCode( 88 ) ) );
		Assert.AreEqual( ButtonCode.KEY_ESCAPE, KeyTranslation.ScanCodeToButtonCode( LinuxKeyCodes.ToScanCode( 1 ) ) );
		Assert.AreEqual( ButtonCode.KEY_LSHIFT, KeyTranslation.ScanCodeToButtonCode( LinuxKeyCodes.ToScanCode( 42 ) ) );
		Assert.AreEqual( ButtonCode.KEY_SPACE, KeyTranslation.ScanCodeToButtonCode( LinuxKeyCodes.ToScanCode( 57 ) ) );
		Assert.AreEqual( ButtonCode.KEY_UP, KeyTranslation.ScanCodeToButtonCode( LinuxKeyCodes.ToScanCode( 103 ) ) );
		Assert.AreEqual( 0, LinuxKeyCodes.ToScanCode( -8 ) );
	}
}
