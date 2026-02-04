using System.Reflection;
using System.Text.RegularExpressions;
using Editor;

namespace TestEditor;

[TestClass]
public class StackTraceTests
{
	[TestMethod]
	[DataRow( @"at Example.Foo() in C:\sbox\Source.cs:line 123", true )]
	[DataRow( @"cat Example.Foo() in C:\sbox\Source.cs:line 123", false )]
	[DataRow( @"at Error in something in C:\sbox\Source.cs:line 123", true )]
	public void DefaultStackLine( string line, bool expectMatch )
	{
		var isMatch = GetDefaultStackLineHandlerRegex().Match( line );

		Assert.AreEqual( expectMatch, isMatch.Success );
	}

	private Regex GetDefaultStackLineHandlerRegex()
	{
		var method = typeof( StackTraceProperty ).GetMethod( nameof( StackTraceProperty.DefaultStackLineHandler ), BindingFlags.Static | BindingFlags.Public )!;

		return new Regex( method.GetCustomAttribute<StackLineHandlerAttribute>()!.Regex );
	}
}