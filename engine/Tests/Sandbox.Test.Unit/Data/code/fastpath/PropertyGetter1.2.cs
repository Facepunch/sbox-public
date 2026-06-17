using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public string Message
	{
		get
		{
			return "Hello Blorld!";
		}
	}


	public int Main( StringWriter output )
	{
		output.Write( Message );
		return 0;
	}
}
