using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	private int Value = 5;

	public int Main( StringWriter output )
	{
		output.Write( Value * 2 );
		return 0;
	}
}
