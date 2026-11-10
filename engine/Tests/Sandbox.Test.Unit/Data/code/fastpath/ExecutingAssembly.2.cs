using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace TestPackage;

public record Example( int Property );

public class Program : CompilingTests.IProgram
{
	public int Main( StringWriter output )
	{
		output.Write( Assembly.GetExecutingAssembly().FullName );

		return 1;
	}
}
