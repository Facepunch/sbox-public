using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TestPackage;

public class Program : CompilingTests.IProgram
{
	public int Main( StringWriter output )
	{
		output.Write( string.Join( ", ", Enumerable.Range( 0, 10 ).Select( x => (x + 1).ToString() ) ) );

		return 0;
	}

}
