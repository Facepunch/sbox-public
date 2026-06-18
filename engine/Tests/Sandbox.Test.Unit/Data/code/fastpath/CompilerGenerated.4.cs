using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public int Main( StringWriter output )
	{

		AsyncMethod( output ).Wait();
		return 0;
	}

	public async Task AsyncMethod( StringWriter output )
	{
		await Task.CompletedTask;

		output.Write( "Hello World!" );
	}
}
