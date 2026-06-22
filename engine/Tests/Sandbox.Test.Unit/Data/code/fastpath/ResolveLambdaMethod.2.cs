using System;
using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public Func<string> Example;

	public int Main( StringWriter output )
	{
		Example = () => GetType().Name;

		return 0;
	}
}
