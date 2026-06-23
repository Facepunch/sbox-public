using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public void Testing( StringWriter output )
	{
		output.WriteLine( "Hello world!" );
		output.WriteLine( "Here's a lot of statements" );
		output.WriteLine( "that will be removed" );
	}

	public int Main( StringWriter output )
	{
		output.Write( "Hello World!" );

		return 0;
	}
}
