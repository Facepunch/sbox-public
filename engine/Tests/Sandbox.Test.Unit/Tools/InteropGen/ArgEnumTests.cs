using Facepunch.InteropGen;

namespace Sandbox.Test.Tools.InteropGen;

[TestClass]
public class ArgEnumTests
{
	[TestMethod]
	public void NativeConversionNarrowsOnlyQFlags()
	{
		var qflags = new Struct
		{
			NativeName = "WindowFlags",
			NativeNamespace = "Qt",
			ManagedName = "WindowFlags",
			IsEnum = true
		};
		qflags.TakeAttributes( ["QFlags"] );

		var wide = new Struct
		{
			NativeName = "ESceneObjectFlags",
			ManagedName = "SceneObjectFlags",
			IsEnum = true
		};

		Assert.AreEqual( "(Qt::WindowFlags)(int)(value)", new ArgEnum( qflags, "value" ).FromInterop( Side.Native, "value" ) );
		Assert.AreEqual( "(ESceneObjectFlags)(value)", new ArgEnum( wide, "value" ).FromInterop( Side.Native, "value" ) );
	}
}
