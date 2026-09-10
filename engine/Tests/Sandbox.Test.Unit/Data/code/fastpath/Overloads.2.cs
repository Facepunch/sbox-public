using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public int Main( StringWriter output, int extraParam )
	{
		return 1;
	}

	public int Main( StringWriter output )
	{
		output.Write( "Hello World!" );
		return 0;
	}
}
