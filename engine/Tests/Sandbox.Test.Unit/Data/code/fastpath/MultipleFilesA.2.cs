using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public int Main( StringWriter output )
	{
		return Example.Property + 2;
	}
}
