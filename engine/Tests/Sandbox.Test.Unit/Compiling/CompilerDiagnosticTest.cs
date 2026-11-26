using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CompilingTests;

/// <summary>
/// Exercises build and editor reporting with Roslyn source locations.
/// </summary>
[TestClass]
public class CompilerDiagnosticTest
{
	private static Diagnostic ErrorAt( Location location ) => Diagnostic.Create(
		new DiagnosticDescriptor( "TEST001", "Test error", "Unknown {0}", "Test", DiagnosticSeverity.Error, true ),
		location, "member" );

	/// <summary>
	/// A diagnostic whose end lies beyond EOF still carries its original error and permits subsequent errors to be reported.
	/// </summary>
	[TestMethod]
	public void OutOfRangeEnd()
	{
		var tree = CSharpSyntaxTree.ParseText( "a\nb", path: "test.cs" );
		var location = Location.Create( tree, new TextSpan( 2, 2 ) );
		var diagnostic = ErrorAt( location );
		var eof = ErrorAt( Location.Create( tree, new TextSpan( tree.Length, 0 ) ) );
		var result = new CompileGroup.Results { Diagnostics = [diagnostic, eof] };

		Assert.ThrowsException<System.ArgumentOutOfRangeException>( () => location.GetLineSpan() );
		Assert.AreEqual( "Error | TEST001: Unknown member - test.cs\nError | Unknown member - test.cs:1,1", result.BuildDiagnosticsString() );
		Assert.AreEqual( "Error | TEST001: Unknown member - test.cs", diagnostic.FormatForClipboard() );
		Assert.IsFalse( diagnostic.GetDisplayLocation().GetMappedLineSpan().IsValid );
		Assert.AreSame( location, result.Diagnostics[0].Location );
		Assert.AreEqual( eof.ToString(), eof.FormatForClipboard() );
	}

	/// <summary>
	/// External and mapped files retain their positions, while a project-wide error has no file position.
	/// </summary>
	[TestMethod]
	public void ExternalMappedAndMissingLocations()
	{
		var external = Location.Create( "panel.razor", default, new LinePositionSpan( new( 2, 3 ), new( 2, 3 ) ) );
		var tree = CSharpSyntaxTree.ParseText( "#line 20 \"panel.razor\"\nclass Panel {}", path: "generated.cs" );
		var mapped = ErrorAt( tree.GetRoot().DescendantTokens().First().GetLocation() );
		var result = new CompileGroup.Results { Diagnostics = [ErrorAt( external ), ErrorAt( Location.None )] };

		Assert.AreEqual( "Error | Unknown member - panel.razor:2,3\nError | TEST001: Unknown member", result.BuildDiagnosticsString() );
		Assert.AreEqual( "panel.razor", mapped.GetDisplayLocation().GetMappedLineSpan().Path );
		Assert.AreEqual( new LinePosition( 19, 0 ), mapped.GetDisplayLocation().GetMappedLineSpan().StartLinePosition );
	}
}
