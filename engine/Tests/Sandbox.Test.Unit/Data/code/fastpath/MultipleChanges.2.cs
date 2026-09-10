using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public int Testing()
	{
		return 100;
	}

	public int Main( StringWriter output )
	{
		return Testing() + 2;
	}
}
