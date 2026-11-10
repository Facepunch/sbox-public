using System.IO;

namespace TestPackage;

[Sandbox.Internal.SourceLocation( "abc", 123 )]
public class Program : CompilingTests.IProgram
{
	public int Main( StringWriter output )
	{
		return 0;
	}
}
