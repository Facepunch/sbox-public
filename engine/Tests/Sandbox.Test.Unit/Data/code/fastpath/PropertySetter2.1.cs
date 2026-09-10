using System.IO;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	private string _backingField;

	public string Message
	{
		set => _backingField = value;
	}


	public int Main( StringWriter output )
	{
		Message = "Hello World!";

		output.Write( _backingField );
		return 0;
	}
}
