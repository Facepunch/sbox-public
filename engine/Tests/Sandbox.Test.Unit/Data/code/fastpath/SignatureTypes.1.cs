using System.IO;

namespace TestPackage;

public record ExampleType(int Property);

public class Program : CompilingTests.IProgram
{
	public ExampleType ExampleMethod( ExampleType parameter )
	{
		return new ExampleType( parameter.Property + 0 );
	}

	public int Main( StringWriter output )
	{
		return ExampleMethod( new ExampleType( 0 ) ).Property;
	}
}
