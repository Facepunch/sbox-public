using Facepunch.InteropGen;

namespace Sandbox.Test.Tools.InteropGen;

[TestClass]
public class NativePrefixTests
{
	[TestMethod]
	[DataRow( "", "::DelayPrecise(" )]
	[DataRow( "SDL_", "::SDL_DelayPrecise(" )]
	public void GlobalPrefixOnlyChangesTheNativeCall( string prefix, string expected )
	{
		var definition = new Definition { SaveFileCppH = "test.h" };
		var type = Class.Parse( true, true, "class", "globalSdl as NativeEngine.Sdl" );
		if ( prefix.Length > 0 ) type.Attributes.Add( $"NativePrefix:{prefix}" );
		var function = Function.Parse( "void DelayPrecise();" );
		function.Class = type;
		type.Functions.Add( function );
		definition.Classes.Add( type );
		new Mangler().Mangle( definition.Classes );

		var writer = new TestWriter( definition );
		writer.Generate();
		StringAssert.Contains( writer.Output, expected );
		Assert.AreEqual( "DelayPrecise", function.GetManagedName() );
	}

	class TestWriter( Definition definition ) : NativeWriter( definition, "test.cpp" )
	{
		internal string Output => Builder.ToString();
	}
}
