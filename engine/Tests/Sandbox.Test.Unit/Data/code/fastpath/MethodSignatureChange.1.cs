using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public int Main( StringWriter output )
	{
		return 0;
	}

	public string SomeMethod()
	{
		return null;
	}
}
