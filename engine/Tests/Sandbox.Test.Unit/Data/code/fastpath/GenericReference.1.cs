using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace TestPackage;

public record Example( int Property );

public class Program : CompilingTests.IProgram
{
	public IDictionary<int, Example> Dict { get; } = new Dictionary<int, Example>();

	public int Main( StringWriter output )
	{
		output.Write( $"Count: {((Dictionary<int, Example>)Dict).Count}" );

		Dict[1] = new Example( 123 );

		return Dict[1].Property;

	}
}
