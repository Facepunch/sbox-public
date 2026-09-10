using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public int Main( StringWriter output )
	{
		for ( var i = 0; i < 10; ++i )
		{
			output.WriteLine( $"Hello {i + 1}" );
		}

		return 0;
	}
}
