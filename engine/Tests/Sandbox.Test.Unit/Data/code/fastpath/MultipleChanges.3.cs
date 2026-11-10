using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public int Testing()
	{
		return 200;
	}

	public int Main( StringWriter output )
	{
		return Testing() + 2;
	}
}
