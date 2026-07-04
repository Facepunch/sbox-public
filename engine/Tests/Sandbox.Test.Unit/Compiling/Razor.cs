using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;

namespace CompilingTests
{

	[TestClass]
	[DoNotParallelize]
	public class RazorParserTest
	{

		public string BuildRazorFile( string razorFilePath )
		{
			List<SyntaxTree> SyntaxTree = new List<SyntaxTree>();

			var optn = new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary )
									.WithConcurrentBuild( true )
									.WithOptimizationLevel( OptimizationLevel.Debug )
									.WithGeneralDiagnosticOption( ReportDiagnostic.Info )
									.WithPlatform( Microsoft.CodeAnalysis.Platform.AnyCpu )
									.WithAllowUnsafe( false );

			var refs = new List<MetadataReference>();

			var path = System.IO.Path.GetDirectoryName( typeof( System.Object ).Assembly.Location );
			refs.Add( MetadataReference.CreateFromFile( typeof( System.Object ).Assembly.Location ) );
			refs.Add( MetadataReference.CreateFromFile( $"{path}\\System.Runtime.dll" ) );

			refs.Add( MetadataReference.CreateFromFile( typeof( Networking ).Assembly.Location ) );
			refs.Add( MetadataReference.CreateFromFile( typeof( ConCmdAttribute ).Assembly.Location ) );

			CSharpCompilation compiler = CSharpCompilation.Create( $"poopy.dll", SyntaxTree, refs, optn );

			// Razor files are now processed via RazorProcessor.GenerateFromSource
			// Generate C# code from the razor file first
			var fullPath = System.IO.Path.GetFullPath( razorFilePath );
			var razorText = System.IO.File.ReadAllText( fullPath );
			var generatedCode = Sandbox.Razor.RazorProcessor.GenerateFromSource( razorText, razorFilePath );

			// Parse into syntax tree and add to compilation
			var razorTree = CSharpSyntaxTree.ParseText( generatedCode, path: $"_gen_{System.IO.Path.GetFileName( razorFilePath )}.cs", encoding: System.Text.Encoding.UTF8 );
			compiler = compiler.AddSyntaxTrees( razorTree );

			var processor = new Sandbox.Generator.Processor();
			processor.AddonName = $"poopy";

			processor.Run( compiler );

			compiler = processor.Compilation;

			Assert.AreEqual( 1, compiler.SyntaxTrees.Count() );

			var code = compiler.SyntaxTrees.First();
			var source = code.GetText().ToString();
			System.Console.WriteLine( source );
			return source;
		}

		[TestMethod]
		public void RazorTemplateEasy()
		{
			BuildRazorFile( "data/codegen/EasyMode.razor" );
		}

		[TestMethod]
		public void RazorTemplateHard()
		{
			var code = BuildRazorFile( "data/codegen/HardMode.razor" );
			Assert.IsTrue( code.Contains( "HardMode.razor\", 5, 0 );" ) );
			Assert.IsTrue( code.Contains( "HardMode.razor\", 7, 0 );" ) );
		}

		[TestMethod]
		public void RazorTemplateRef()
		{
			var code = BuildRazorFile( "data/codegen/Ref.razor" );
			Assert.IsTrue( code.Contains( ".OpenElement(" ) );
			Assert.IsTrue( code.Contains( ".CloseElement()" ) );
			Assert.IsTrue( code.Contains( ".AddReferenceCapture(" ) );
		}

		[TestMethod]
		public void RazorTemplateEvents()
		{
			var code = BuildRazorFile( "data/codegen/Events.razor" );

			// @onclick=Clicked
			Assert.IsTrue( code.Contains( "__builder.AddAttribute( 1, \"onclick\", Clicked );" ) );

			// @onclick=@(() => DoSomething())
			Assert.IsTrue( code.Contains( "__builder.AddAttribute( 5, \"onclick\", () => DoSomething() );" ) );

			// @OnEvent=@Clicked
			Assert.IsTrue( code.Contains( "__builder.AddAttribute<MyPanel>( 9, ( o ) => {  o.OnEvent = Clicked; } );" ) );

			// @OnEvent=@(() => DoSomething())
			Assert.IsTrue( code.Contains( "__builder.AddAttribute<MyPanel>( 13, ( o ) => {  o.OnEvent = () => DoSomething(); } );" ) );
		}

		[TestMethod]
		public void RazorTemplateAttributes()
		{
			BuildRazorFile( "data/codegen/Attributes.razor" );
		}

		[TestMethod]
		public void StylesheetAttribute()
		{
			var code = BuildRazorFile( "data/codegen/StylesheetAttribute.razor" );
			Assert.IsTrue( code.Contains( "[StyleSheet(" ) );
		}

		[TestMethod]
		public void StyleBlock()
		{
			var code = BuildRazorFile( "data/codegen/StyleBlock.razor" );
			Assert.IsFalse( code.Contains( "<style>" ) );
			Assert.IsFalse( code.Contains( "</style>" ) );
			Assert.IsTrue( code.Contains( ".AddStyleDefinitions(" ), "Missing .AddStyleBlock(" );
		}

		[TestMethod]
		public void RootElement()
		{
			var code = BuildRazorFile( "data/codegen/RootElement.razor" );
			Assert.IsTrue( code.Contains( "__builder.OpenElement( 0, \"root\", null );" ) );
			Assert.IsFalse( code.Contains( "internal partial class" ) );
			Assert.IsTrue( code.Contains( "public partial class" ) );
		}

		[TestMethod]
		public void InternalDirective()
		{
			var code = BuildRazorFile( "data/codegen/InternalDirective.razor" );
			Assert.IsTrue( code.Contains( "internal partial class" ) );
		}

		[TestMethod]
		public void Binds()
		{
			var code = BuildRazorFile( "data/codegen/Binds.razor" );
			Assert.IsFalse( code.Contains( "internal partial class" ) );
			Assert.IsTrue( code.Contains( "public partial class" ) );
			Assert.IsTrue( code.Contains( ".AddBind(" ) );
		}

		[TestMethod]
		public void Recursion()
		{
			// We want recursion to throw an exception at runtime (when building the render tree)
			// so that users can see what they've done wrong
			var code = BuildRazorFile( "data/codegen/RecursivePanel.razor" );
			Assert.IsTrue( code.Contains( "throw new System.Exception" ) );
		}

		[TestMethod]
		public void Nesting()
		{
			// We don't want nesting to fail at all
			var code = BuildRazorFile( "data/codegen/NestedPanel.razor" );
			Assert.IsFalse( code.Contains( "throw new System.Exception" ) );
		}

		[TestMethod]
		public void RenderFragment()
		{
			var code = BuildRazorFile( "data/codegen/RenderFragmentTest.razor" );
			Assert.IsTrue( code.Contains( "__builder.SetRenderFragment" ) );
			Assert.IsTrue( code.Contains( "__builder ) =>" ) );
		}

		[TestMethod]
		public void Generic_WithOneParam()
		{
			var code = BuildRazorFile( "data/codegen/Generic1.razor" );
			Assert.IsTrue( code.Contains( "public partial class Generic1<T1>" ) );
			Assert.IsTrue( code.Contains( "ListComponent<string>" ) );
		}

		[TestMethod]
		public void Generic_WithTwoParam()
		{
			var code = BuildRazorFile( "data/codegen/Generic2.razor" );
			Assert.IsTrue( code.Contains( "public partial class Generic2<T1, T2>" ) );
			Assert.IsTrue( code.Contains( "ListComponent<string,int>" ) );
		}

		[TestMethod]
		public void AutomaticNamespace()
		{
			// Test that namespace is automatically generated from folder structure
			var razorText = System.IO.File.ReadAllText( "data/codegen/NamespaceTest.razor" );

			// Test 1: Generate code WITH a root namespace and folder structure
			// The path "data/codegen/NamespaceTest.razor" should produce "MyApp.UI.data.codegen"
			var generatedCodeWithFolders = Sandbox.Razor.RazorProcessor.GenerateFromSource( razorText, "data/codegen/NamespaceTest.razor", "MyApp.UI" );
			System.Console.WriteLine( "Generated code with folder-based namespace:" );
			System.Console.WriteLine( generatedCodeWithFolders );

			// Should contain the root namespace + folders
			Assert.IsTrue( generatedCodeWithFolders.Contains( "namespace MyApp.UI.data.codegen" ), "Generated code should contain 'namespace MyApp.UI.data.codegen'" );

			// Test 2: Generate code with a realistic addon path structure
			// Simulating: UI/Components/Loader/LoaderFullScreen.razor -> Sandbox.UI.Components.Loader
			var generatedCodeRealistic = Sandbox.Razor.RazorProcessor.GenerateFromSource( razorText, "UI/Components/Loader/LoaderFullScreen.razor", "Sandbox" );
			System.Console.WriteLine( "\nGenerated code with realistic path:" );
			System.Console.WriteLine( generatedCodeRealistic );

			Assert.IsTrue( generatedCodeRealistic.Contains( "namespace Sandbox.UI.Components.Loader" ), "Generated code should contain 'namespace Sandbox.UI.Components.Loader'" );

			// Test 3: Generate code WITHOUT a root namespace (should not have namespace directive)
			var generatedCodeWithoutNamespace = Sandbox.Razor.RazorProcessor.GenerateFromSource( razorText, "data/codegen/NamespaceTest.razor", null );
			System.Console.WriteLine( "\nGenerated code without namespace:" );
			System.Console.WriteLine( generatedCodeWithoutNamespace );

			// Verify no namespace is added
			Assert.IsFalse( generatedCodeWithoutNamespace.Contains( "namespace MyApp" ), "Generated code without root namespace should not contain namespace declaration" );

			// Test 4: File in root directory (no subfolders) should just use root namespace
			var generatedCodeRootLevel = Sandbox.Razor.RazorProcessor.GenerateFromSource( razorText, "TestComponent.razor", "MyApp" );
			System.Console.WriteLine( "\nGenerated code for root-level file:" );
			System.Console.WriteLine( generatedCodeRootLevel );

			Assert.IsTrue( generatedCodeRootLevel.Contains( "namespace MyApp" ), "Root-level file should use root namespace" );
			Assert.IsFalse( generatedCodeRootLevel.Contains( "namespace MyApp." ), "Root-level file should not have sub-namespaces" );
		}
	}
}
