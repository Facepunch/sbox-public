using NativeEngine;
using Sandbox.Engine;
using System;

namespace EngineTests;

[TestClass]
public class KeyTranslationTests
{
	[TestMethod]
	public void CanonicalNamesRoundTrip()
	{
		Assert.AreEqual( 369, (int)ButtonCode.BUTTON_CODE_COUNT );
		for ( var i = 1; i < (int)ButtonCode.BUTTON_CODE_COUNT; i++ )
		{
			var code = (ButtonCode)i;
			Assert.AreEqual( code, KeyTranslation.StringToButtonCode( KeyTranslation.CodeToString( code ) ) );
		}
	}

	[TestMethod]
	public void LegacyAliasesPreserveBindingNames()
	{
		Assert.AreEqual( ButtonCode.KEY_XBUTTON_A, KeyTranslation.StringToButtonCode( "a_button" ) );
		Assert.AreEqual( ButtonCode.KEY_XBUTTON_UP, KeyTranslation.StringToButtonCode( "aux29" ) );
		Assert.AreEqual( ButtonCode.KEY_XBUTTON_LEFT, KeyTranslation.StringToButtonCode( "AUX32" ) );
		Assert.AreEqual( ButtonCode.KEY_XBUTTON_UP, KeyTranslation.StringToButtonCode( "UP" ) );
		Assert.AreEqual( ButtonCode.KEY_UP, KeyTranslation.StringToButtonCode( "UPARROW" ) );
		Assert.AreEqual( "POV_UP", KeyTranslation.CodeToString( ButtonCode.KEY_XBUTTON_UP ) );
		Assert.AreEqual( "JOY1", KeyTranslation.CodeToString( ButtonCode.KEY_XBUTTON_A ) );
	}

	[TestMethod]
	public void InvalidCodesAreHarmless()
	{
		Assert.AreEqual( "", KeyTranslation.CodeToString( (ButtonCode)int.MaxValue ) );
		Assert.AreEqual( ButtonCode.BUTTON_CODE_INVALID, KeyTranslation.StringToButtonCode( "aux-1" ) );
		Assert.AreEqual( ButtonCode.BUTTON_CODE_INVALID, KeyTranslation.StringToButtonCode( "aux33" ) );
		Assert.AreEqual( ButtonCode.BUTTON_CODE_INVALID, KeyTranslation.StringToButtonCode( null ) );
		Assert.AreEqual( ButtonCode.KEY_NONE, KeyTranslation.ScanCodeToButtonCode( -1 ) );
		Assert.AreEqual( ButtonCode.KEY_NONE, KeyTranslation.ScanCodeToButtonCode( 512 ) );
		Assert.AreEqual( ButtonCode.KEY_NONE, KeyTranslation.KeyCodeToButtonCode( uint.MaxValue ) );
	}

	[TestMethod]
	public void PhysicalKeysAndCharactersStayDistinct()
	{
		Assert.AreEqual( ButtonCode.KEY_A, KeyTranslation.ScanCodeToButtonCode( 4 ) );
		Assert.AreEqual( ButtonCode.KEY_Q, KeyTranslation.KeyCodeToButtonCode( 'q' ) );
		Assert.AreEqual( ButtonCode.KEY_PAD_ENTER, KeyTranslation.ScanCodeToButtonCode( 88 ) );
		if ( OperatingSystem.IsWindows() )
		{
			Assert.AreEqual( ButtonCode.KEY_ENTER, KeyTranslation.VirtualKeyToButtonCode( 13 ) );
			Assert.AreEqual( 0, KeyTranslation.ButtonCodeToVirtualKey( ButtonCode.KEY_PAD_ENTER ) );
		}
	}
}
