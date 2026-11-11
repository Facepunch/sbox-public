using Sandbox.Engine;
using System;
using System.Globalization;

namespace ScriptingTests;

/// <summary>
/// Primitive values use their native formatting methods through the script catalog.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptPrimitiveFormattingTests
{
	GlobalContext previous;
	GlobalContext previousGame;
	CultureInfo previousCulture;

	/// <summary>
	/// Uses the shared scripting catalog with a predictable default culture.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		previousCulture = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		GlobalContext.Current = GlobalContext.Game = new GlobalContext { TypeLibrary = SharedTypeLibrary.ScriptingWithSystem };
	}

	/// <summary>
	/// Restores the original game context and formatting culture.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.Shutdown();
		GlobalContext.Current = previous;
		GlobalContext.Game = previousGame;
		CultureInfo.CurrentCulture = previousCulture;
	}

	/// <summary>
	/// Both exact LL-002 reproductions and ordinary numeric format strings bind successfully.
	/// </summary>
	[TestMethod]
	[DataRow( "return (1 + 1).ToString();", "2" )]
	[DataRow( "int n = 2; return n.ToString();", "2" )]
	[DataRow( "return (12).ToString(\"D4\");", "0012" )]
	[DataRow( "int n = 255; return n.ToString(\"X4\");", "00FF" )]
	[DataRow( "float n = 1.25f; return n.ToString(\"F2\");", "1.25" )]
	[DataRow( "double n = 12.5; return n.ToString(\"0.00\");", "12.50" )]
	public void ScriptsCanFormatNumbers( string source, string expected )
	{
		var script = Game.Scripting.CreateScript( source );
		Assert.AreEqual( expected, script.RunAndReturn<string>(), script.LastError?.Message );
		Assert.IsNull( script.LastError );
	}

	/// <summary>
	/// Signed, unsigned, floating-point, boolean and character primitives support parameterless formatting.
	/// </summary>
	[TestMethod]
	[DataRow( "byte value = 2;", "2" )]
	[DataRow( "sbyte value = -2;", "-2" )]
	[DataRow( "short value = -2;", "-2" )]
	[DataRow( "ushort value = 2;", "2" )]
	[DataRow( "int value = -2;", "-2" )]
	[DataRow( "uint value = 2u;", "2" )]
	[DataRow( "long value = -2L;", "-2" )]
	[DataRow( "ulong value = 2UL;", "2" )]
	[DataRow( "float value = 1.5f;", "1.5" )]
	[DataRow( "double value = 1.5;", "1.5" )]
	[DataRow( "bool value = true;", "True" )]
	[DataRow( "char value = 'x';", "x" )]
	public void ScriptsCanFormatPrimitiveValues( string declaration, string expected )
	{
		var script = Game.Scripting.CreateScript( $"{declaration} return value.ToString();" );
		Assert.AreEqual( expected, script.RunAndReturn<string>(), script.LastError?.Message );
		Assert.IsNull( script.LastError );
	}

	/// <summary>
	/// Parameterless and format-string overloads preserve the caller's current culture.
	/// </summary>
	[TestMethod]
	[DataRow( "return value.ToString();", "1,5" )]
	[DataRow( "return value.ToString(\"F2\");", "1,50" )]
	public void ScriptsRespectCurrentCulture( string source, string expected )
	{
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo( "fr-FR" );
		var script = Game.Scripting.CreateScript( source );
		script.SetArgument( "value", 1.5f );
		Assert.AreEqual( expected, script.RunAndReturn<string>(), script.LastError?.Message );
		Assert.IsNull( script.LastError );
	}

	/// <summary>
	/// Script lookup supplies public formatting overloads even when discovery omits them.
	/// </summary>
	[TestMethod]
	[DataRow( "return value.ToString(provider);", "1.5" )]
	[DataRow( "return value.ToString(\"F2\", provider);", "1.50" )]
	public void ScriptsCanUseCultureProviderOverloads( string source, string expected )
	{
		var script = Game.Scripting.CreateScript( source );
		script.SetArgument( "value", 1.5f );
		script.SetArgument( "provider", CultureInfo.InvariantCulture );
		Assert.AreEqual( expected, script.RunAndReturn<string>(), script.LastError?.ToString() );
		Assert.IsNull( script.LastError );
	}

	/// <summary>
	/// Common object methods do not grant reflection or access to protected object methods.
	/// </summary>
	[TestMethod]
	[DataRow( "return (2).GetType();" )]
	[DataRow( "return new object().MemberwiseClone();" )]
	[DataRow( "new object().Finalize();" )]
	public void OtherObjectMethodsRemainUnavailable( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		Assert.IsFalse( script.Run(), source );
		Assert.IsNotNull( script.LastError );
	}
}
