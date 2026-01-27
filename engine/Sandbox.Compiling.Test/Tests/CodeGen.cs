using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Generator
{

	[TestClass]
	public class CodeGen
	{
		private void AddTree( List<SyntaxTree> syntaxTree, string path, string[] defines = null )
		{
			var parseOptions = CSharpParseOptions.Default.WithLanguageVersion( LanguageVersion.Default );

			if ( defines is not null )
				parseOptions = parseOptions.WithPreprocessorSymbols( defines );

			var code = System.IO.File.ReadAllText( path );
			var tree = CSharpSyntaxTree.ParseText( text: code, options: parseOptions, path: path, encoding: System.Text.Encoding.UTF8 );

			if ( defines is not null )
				tree = Compiler.StripDisabledTextTrivia( tree );

			syntaxTree.Add( tree );
		}

		CSharpCompilation Build( string assemblyName, params string[] files )
		{
			return Build( assemblyName, null, files );
		}

		CSharpCompilation Build( string assemblyName, string[] defines = null, params string[] files )
		{
			List<SyntaxTree> SyntaxTree = new List<SyntaxTree>();

			foreach ( var file in files )
				AddTree( SyntaxTree, $"data/codegen/{file}", defines );

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
			refs.Add( MetadataReference.CreateFromFile( typeof( ConCmdAttribute ).Assembly.Location ) ); // Sandbox.System

			CSharpCompilation compiler = CSharpCompilation.Create( $"{assemblyName}.dll", SyntaxTree, refs, optn );

			var processor = new Sandbox.Generator.Processor();
			processor.AddonName = assemblyName;
			processor.PackageAssetResolver = ( p ) => $"/{p}/model_mock.mdl";
			processor.Run( compiler );

			compiler = processor.Compilation;

			foreach ( var tree in compiler.SyntaxTrees )
			{
				System.Console.WriteLine( tree.FilePath );
				System.Console.WriteLine( tree.GetText().ToString() );
			}

			return compiler;
		}

		CSharpCompilation BuildString( string contents, string assemblyName = "assembly_name" )
		{
			List<SyntaxTree> SyntaxTree = new List<SyntaxTree>();

			{
				var parseOptions = CSharpParseOptions.Default.WithLanguageVersion( LanguageVersion.Default );
				var code = contents;
				var tree = CSharpSyntaxTree.ParseText( text: code, options: parseOptions, path: "Program.cs", encoding: System.Text.Encoding.UTF8 );
				SyntaxTree.Add( tree );
			}

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
			refs.Add( MetadataReference.CreateFromFile( typeof( ConCmdAttribute ).Assembly.Location ) ); // Sandbox.System

			CSharpCompilation compiler = CSharpCompilation.Create( $"{assemblyName}.dll", SyntaxTree, refs, optn );

			var processor = new Sandbox.Generator.Processor();
			processor.AddonName = assemblyName;
			processor.PackageAssetResolver = ( p ) => $"/{p}/model_mock.mdl";
			processor.Run( compiler );

			compiler = processor.Compilation;

			foreach ( var tree in compiler.SyntaxTrees )
			{
				System.Console.WriteLine( tree.FilePath );
				System.Console.WriteLine( tree.GetText().ToString() );
			}

			return compiler;
		}

		[TestMethod]
		[DataRow( "TestVar.cs" )]
		[DataRow( "TestRpc.cs" )]
		[DataRow( "TestReplicate.cs" )]
		[DataRow( "TestStackOverflow.cs" )]
		[DataRow( "TestRpc.Entity.cs" )]
		[DataRow( "TestReplicate.Entity.cs" )]
		public void DoCodeGen( string filename )
		{
			Build( "do_code_gen", filename );
		}


		[TestMethod]
		public void AdditionalFiles()
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

			// Process Razor files using RazorProcessor before running the Processor
			foreach ( var file in System.IO.Directory.EnumerateFiles( "data/codegen/", "*.razor" ) )
			{
				var razorText = System.IO.File.ReadAllText( file );
				var generatedCode = Sandbox.Razor.RazorProcessor.GenerateFromSource( razorText, file );
				var razorTree = CSharpSyntaxTree.ParseText( generatedCode, path: $"_gen_{System.IO.Path.GetFileName( file )}.cs", encoding: System.Text.Encoding.UTF8 );
				compiler = compiler.AddSyntaxTrees( razorTree );
			}

			var processor = new Sandbox.Generator.Processor();
			processor.AddonName = $"poopy";
			processor.Run( compiler );

			compiler = processor.Compilation;

			foreach ( var tree in compiler.SyntaxTrees )
			{
				System.Console.WriteLine( tree.GetText().ToString() );
			}
		}

		/// <summary>
		/// Note that our aim here is to prevent 99% of accidental overflows. There are still going to be ways
		/// to overflow and force quit.. but this should all stop the accidental stuff.
		/// </summary>
		//	[TestMethod]
		public void StackOverflowPrevention()
		{
			var compiler = Build( "do_code_gen", "TestStackOverflow.cs" );

			// actually compile
			using var stream = new MemoryStream();
			var result = compiler.Emit( stream );
			Assert.IsTrue( result.Success, "Failed to compile!" );

			// Load it
			var assemblyBytes = stream.ToArray();
			var asm = System.Reflection.Assembly.Load( assemblyBytes );

			// Call RecurseForever
			var TestStackOverflow = asm.GetType( "TestStackOverflow" );


			// Should successfully throw an exception due to stack overflowing
			var RecurseForever = TestStackOverflow.GetMethod( "RecurseForever" );
			Assert.ThrowsException<TargetInvocationException>( () => RecurseForever.Invoke( null, null ) );

			var RecurseForeverInline = TestStackOverflow.GetMethod( "RecurseForeverInline" );
			Assert.ThrowsException<TargetInvocationException>( () => RecurseForeverInline.Invoke( null, null ) );
		}

		[TestMethod]
		public void TestCloudAssets()
		{
			var compiler = Build( "do_code_gen", "CloudAssetTest.cs" );

			Assert.AreEqual( 2, compiler.SyntaxTrees.Count(), "Should have the original syntaxtree plus the generated one" );

			var added = compiler.SyntaxTrees.First( x => x.FilePath.Contains( "_gen__AddedCode.cs" ) );
			Assert.IsTrue( added.GetText().ToString().Contains( "[assembly:Sandbox.Cloud.Asset( " ), "Outputted text should contain the asset attribute" );
		}

		[TestMethod]
		public void TestWrapCall()
		{
			var compiler = Build( "do_code_gen", "TestWrapCall.cs" );
			var tree = compiler.SyntaxTrees.First();

			System.Console.WriteLine( tree.GetText().ToString() );

			Assert.IsTrue( tree.GetText().ToString().Contains( "Object = null, MethodIdentity = -1168963981, MethodName = \"TestWrappedStaticCall\", TypeName = \"TestWrapCall\"" ), "Generated code should wrap static method call" );
			Assert.IsTrue( tree.GetText().ToString().Contains( "Object = this, MethodIdentity = -446800946, MethodName = \"TestWrappedInstanceCall\", TypeName = \"TestWrapCall\"" ), "Generated code should wrap instance method call" );
			Assert.IsTrue( tree.GetText().ToString().Contains( "Object = null, MethodIdentity = 1638661065, MethodName = \"TestWrappedStaticCallNoArg\", TypeName = \"TestWrapCall\"" ), "Generated code should wrap static method call with no arg" );
			Assert.IsTrue( tree.GetText().ToString().Contains( "Object = this, MethodIdentity = -1769572979, MethodName = \"TestWrappedInstanceCallNoArg\", TypeName = \"TestWrapCall\"" ), "Generated code should wrap instance method call with no arg" );
			Assert.IsTrue( tree.GetText().ToString().Contains( "Object = this, MethodIdentity = 1201362747, MethodName = \"ExpressionBodiedBroadcast\", TypeName = \"TestWrapCall\"" ), "Generated code should wrap expression bodied method" );
			Assert.IsTrue( tree.GetText().ToString().Contains( "Object = this, MethodIdentity = -1316352073, MethodName = \"TestWrappedInstanceCallReturnType\", TypeName = \"TestWrapCall\"" ), "Generated code should wrap instance method call with return type" );
			Assert.IsTrue( tree.GetText().ToString().Contains( "Object = null, MethodIdentity = 1168636003, MethodName = \"TestAsyncTaskCall\", TypeName = \"TestWrapCall\", IsStatic = true" ), "Generated code should wrap async Task method call" );
			Assert.IsTrue( tree.GetText().ToString().Contains( "MethodName = \"MyGenericCall\", TypeName = \"TestWrapCall\", IsStatic = false, Attributes = __898531504__Attrs, GenericArguments = new[] { typeof(T) } }" ), "Generated code should include closed generic types" );
			Assert.IsTrue( tree.GetText().ToString().Contains( "MethodName = \"MyGenericCallAsync\", TypeName = \"TestWrapCall\", IsStatic = false, Attributes = __1383690312__Attrs, GenericArguments = new[] { typeof(T) } }" ), "Generated code should include closed generic types when returning a Task" );
		}

		[TestMethod]
		public void TestPreprocessorWrappedCall()
		{
			var compiler = Build( "do_code_gen", "TestPreprocessorWrapCall.cs" );
			var tree = compiler.SyntaxTrees.First();
			var treeText = tree.GetText().ToString();

			Assert.IsTrue( treeText.Contains( "#if true" ), "Maintain preprocessor directive in body" );
			Assert.IsFalse( treeText.Contains( $");}}#endif" ), "No trailing trivia outside of body" );

			compiler = Build( "do_code_gen", ["SERVER"], "TestPreprocessorWrapCall.cs" );
			tree = compiler.SyntaxTrees.First();
			treeText = tree.GetText().ToString();

			Assert.IsFalse( treeText.Contains( "// Client-side code only" ), "Strip code with missing preprocessor directive" );
			Assert.IsTrue( treeText.Contains( "// Server-side code only" ), "Maintain SERVER preprocessor directive in body" );
		}

		[TestMethod]
		public void TestWrapGet()
		{
			var compiler = Build( "do_code_gen", "TestWrapGet.cs" );
			var tree = compiler.SyntaxTrees.First();
			System.Console.WriteLine( tree.GetText().ToString() );

			Assert.IsTrue( tree.GetText().ToString().Contains( "return (bool)WrapGet.OnWrapGetStatic(new global::Sandbox.WrappedPropertyGet<bool> { Value = field" ), "Generated code should wrap static property get accessor" );
			Assert.IsTrue( tree.GetText().ToString().Contains( "return (bool)OnWrapGet(new global::Sandbox.WrappedPropertyGet<bool> { Value = field" ), "Generated code should wrap instance property get accessor" );
		}

		[TestMethod]
		public void TestWrapSet()
		{
			var compiler = Build( "do_code_gen", "TestWrapSet.cs" );
			var tree = compiler.SyntaxTrees.First();
			System.Console.WriteLine( tree.GetText().ToString() );

			Assert.IsTrue( tree.GetText().ToString().Contains( "WrapSet.OnWrapSetStatic(new global::Sandbox.WrappedPropertySet<bool> { Value = value, Object = null, Setter = (v) =>" ), "Generated code should wrap static property set accessor" );
			Assert.IsTrue( tree.GetText().ToString().Contains( "OnWrapSet(new global::Sandbox.WrappedPropertySet<bool> { Value = value, Object = this, Setter = (v) =>" ), "Generated code should wrap instance property set accessor" );
		}

		[TestMethod]
		public void CodeGenFullyQualified()
		{
			var compiler = BuildString( """
		        namespace CodeGenFullyQualified;
		        
		        [Sandbox.CodeGenerator(Sandbox.CodeGeneratorFlags.WrapPropertyGet | Sandbox.CodeGeneratorFlags.Static | Sandbox.CodeGeneratorFlags.Instance, "CodeGenFullyQualified.TestCodeGenAttribute.GetWrapper")]
		        [System.AttributeUsage(System.AttributeTargets.Property)]
		        public class TestCodeGenAttribute : System.Attribute
		        {
		        	internal static T GetWrapper<T>(Sandbox.WrappedPropertyGet<T> property)
		        	{
		        		return property.Value;
		        	}
		        }
		        
		        public class Program
		        {
		            [TestCodeGen] public bool TestProperty { get; set; }
		        
		            public static void Main()
		            {
		            }
		        }
		        """ );

			var tree = compiler.SyntaxTrees.First();
			Assert.IsTrue( compiler.GetDiagnostics().Length == 0, "Should be no compile errors" );
		}

		[TestMethod]
		public void StringTokenReplacement()
		{
			var compiler = BuildString( """"
				using Sandbox;
				
				public static class Program
				{
					public static void Main()
					{

						RenderAttributes ra = new ();

						ra.Set( "PoopSock", Vector3.One ); 

					}
				}

				"""" );

			var tree = compiler.SyntaxTrees.First();
			Assert.IsTrue( tree.GetText().ToString().Contains( "\"PoopSock\",1590150592U" ), "Should have replaced string token" );

		}

		[TestMethod]
		public void ParameterDescriptionAttribute()
		{
			var compiler = Build( "do_code_gen", "Descriptions.cs" );
			var tree = compiler.SyntaxTrees.First();

			Assert.IsTrue( tree.GetText().ToString().Contains( "[DescriptionAttribute( \"This parameter has a description!\" )]int baz" ) );
		}


		[TestMethod]
		public void ReturnsDescriptionAttribute()
		{
			var compiler = Build( "do_code_gen", "Descriptions.cs" );
			var tree = compiler.SyntaxTrees.First();

			Assert.IsTrue( tree.GetText().ToString().Contains( "[return:DescriptionAttribute( \"Here's a description of the return value!\" )]" ) );
		}
	}
}
