using InlineArrayAttr = System.Runtime.CompilerServices.InlineArrayAttribute;

[InlineArrayAttr( 4 )]
public struct IntBuffer
{
	private int _element0;
}

class AliasUsingTest
{
	static void Main()
	{
		IntBuffer buffer = default;
	}
}
