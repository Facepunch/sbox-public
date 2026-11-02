using Facepunch.InteropGen;
using System.Globalization;

namespace Sandbox.Test.Tools.InteropGen;

[TestClass]
public class ManglerTests
{
	/// <summary>
	/// Under tr-TR a case-insensitive [a-z] doesn't match 'I', which used to strip it from mangled names.
	/// </summary>
	[TestMethod]
	[DataRow( "en-US" )]
	[DataRow( "tr-TR" )]
	public void MangledNameIsCultureInvariant( string culture )
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo( culture );

		try
		{
			var type = Class.Parse( true, true, "class", "g_pInputSystem as NativeEngine.InputSystem" );
			var function = Function.Parse( "void SetGameFocus( bool focus );" );
			function.Class = type;
			type.Functions.Add( function );
			new Mangler().Mangle( [type] );

			Assert.AreEqual( "g_pInputSystem_SetGameFocus", function.MangledName );
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}
}
