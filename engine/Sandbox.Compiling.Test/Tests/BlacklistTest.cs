using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TestCompiler;

[TestClass]
public partial class BlacklistTest
{

	[TestMethod]
	public async Task DefaultCompilerFailsWhitelist()
	{
		var codePath = System.IO.Path.GetFullPath( "data/code/blacklist" );
		var group = new CompileGroup( "TestWhitelist" );

		var compiler = group.GetOrCreateCompiler( "test" );
		compiler.AddSourcePath( codePath );
		compiler.MarkForRecompile();
		await group.BuildAsync();

		// Verify compilation failed due to whitelist violations
		var output = compiler.Output;
		Assert.IsNotNull( output );
		Assert.IsFalse( output.Successful, "Compiler should fail with default whitelist settings" );
		Assert.IsTrue( output.Diagnostics.Count > 0, "Should have diagnostics for whitelist violations" );
	}

	[TestMethod]
	public async Task CompilerWithWhitelistFails()
	{
		var codePath = System.IO.Path.GetFullPath( "data/code/blacklist" );
		var group = new CompileGroup( "TestWhitelist" );

		var compilerSettings = new Compiler.Configuration();
		compilerSettings.Whitelist = true;
		compilerSettings.Unsafe = false;

		var compiler = group.CreateCompiler( "test", codePath, compilerSettings );
		await group.BuildAsync();

		// Verify compilation failed due to whitelist being enabled
		var output = compiler.Output;
		Assert.IsNotNull( output );
		Assert.IsFalse( output.Successful, "Compiler should fail when whitelist is explicitly enabled" );
		Assert.IsTrue( output.Diagnostics.Count > 0, "Should have diagnostics for whitelist violations" );
	}

	[TestMethod]
	public async Task CompilerWithoutWhitelistSucceeds()
	{
		var codePath = System.IO.Path.GetFullPath( "data/code/blacklist" );
		var group = new CompileGroup( "TestWhitelist" );

		var compilerSettings = new Compiler.Configuration();
		compilerSettings.Whitelist = false;
		compilerSettings.Unsafe = true;

		var compiler = group.CreateCompiler( "test", codePath, compilerSettings );
		await group.BuildAsync();

		// Verify compilation succeeded with whitelist disabled
		var output = compiler.Output;
		Assert.IsNotNull( output );
		Assert.IsTrue( output.Successful, "Compiler should succeed when whitelist is disabled" );
		Assert.IsNull( output.Exception, "Should not have any exceptions" );
	}

	[TestMethod]
	public async Task EndToEndBuildFailure()
	{
		bool compileSuccessCallback = false;

		var codePath = System.IO.Path.GetFullPath( "data/code/blacklist" );
		var group = new CompileGroup( "Test" );
		group.OnCompileSuccess = () => compileSuccessCallback = true;

		var compilerSettings = new Compiler.Configuration();
		compilerSettings.Clean();

		var compiler = group.CreateCompiler( "test", codePath, compilerSettings );
		await group.BuildAsync();

		foreach ( var diag in compiler.Diagnostics )
		{
			Console.WriteLine( $"{diag}" );
		}

		// 3 errors please
		// data\code\blacklist\Unsafe.cs( 10, 17 ): error SB500: Prohibited type 'System.Runtime.CompilerServices.Unsafe.As<float, int>(float)' used
		// data\code\blacklist\UsingStatic.cs( 8, 28 ): error SB500: Prohibited type 'System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan<int>(int, int)' used
		// data\code\blacklist\UsingAlias.cs( 3, 2 ): error SB500: Prohibited type 'System.Runtime.CompilerServices.InlineArrayAttribute.InlineArrayAttribute(int)' used
		Assert.AreEqual( 4, compiler.Diagnostics.Length );

		// We want to fail
		Assert.IsFalse( compileSuccessCallback );
		Assert.IsNotNull( group.BuildResult );
		Assert.IsFalse( group.BuildResult.Success );
	}

	void CompileAndWalk( string code, out List<Diagnostic> diagnostics )
	{
		var syntaxTree = CSharpSyntaxTree.ParseText( code );

		var path = System.IO.Path.GetDirectoryName( typeof( System.Object ).Assembly.Location );

		var compilation = CSharpCompilation.Create(
			assemblyName: "TestAssembly.dll",
			syntaxTrees: [syntaxTree],
			references: [
				MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
				MetadataReference.CreateFromFile( $"{path}\\System.Runtime.dll" ),
				MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(System.Runtime.InteropServices.MemoryMarshal).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(Networking).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(ConCmdAttribute).Assembly.Location) // Sandbox.System
			] );

		var processor = new Sandbox.Generator.Processor();
		processor.AddonName = "TestAssembly";
		processor.PackageAssetResolver = ( p ) => $"/{p}/model_mock.mdl";
		processor.Run( compilation );

		compilation = processor.Compilation;

		// New syntax tree after codegen
		syntaxTree = compilation.SyntaxTrees.First();

		var semanticModel = compilation.GetSemanticModel( syntaxTree );

		var walker = new BlacklistCodeWalker( semanticModel );
		walker.Visit( syntaxTree.GetRoot() );

		diagnostics = walker.Diagnostics;
	}

	public enum MemberQualificationKind
	{
		FullyQualified,
		UsingStatic,
		Alias,
		UsingNamespace
	}

	public static object[][] MemberQualificationKinds => Enum.GetValues<MemberQualificationKind>()
		.Select( x => new object[] { x } ).ToArray();

	[TestMethod]
	public void CodeGenWrapped()
	{
		// H1-3204420
		var sourceCode = """
			#define DUMMY
			#define DUMMY2
			using System;
			using System.Diagnostics;
			using System.Reflection;
			using Sandbox;
			using Sandbox.Diagnostics;

			public static class SandboxEscapeDegeneratedBlacklist
			{
				[AttributeUsage(AttributeTargets.Method)]
				[CodeGenerator(CodeGeneratorFlags.WrapMethod | CodeGeneratorFlags.Static, "OnMethodInvoked")]
				private sealed class WrapAttribute : Attribute;

				[Wrap]
				public static T As<T>(object o) where T : class
				{
					// ensure #if starts a new line
			#if !DUMMY
					return System.Runtime.CompilerServices.Unsafe.As<T>(o);
			#endif
			
			#if DUMMY2
					return System.Runtime.CompilerServices.Unsafe.As<T>(o);
			#endif
			
					return default;
				}
			
				private static T OnMethodInvoked<T>(WrappedMethod<T> m, object o)
				{
					return m.Resume();
				}
				
				[ConCmd("escape")]
				public static void Escape()
				{
					var type = typeof(Type);
					var typeShadow = As<ModelRenderer>(type);
					Log.Info("type " + type);
					Log.Info("typeShadow " + typeShadow);
				}
			}
			""";

		CompileAndWalk( sourceCode, out var diagnostics );

		// Prohibited type 'System.Runtime.CompilerServices.Unsafe.As<T>(object)' used
		Assert.AreEqual( 1, diagnostics.Count );
		Assert.AreEqual( "SB500", diagnostics.FirstOrDefault().Id );
	}

	[TestMethod]
	[DynamicData( nameof( MemberQualificationKinds ) )]
	public void MethodInvoke( MemberQualificationKind kind )
	{
		var sourceCode = """
		var version = System.Runtime.CompilerServices.Unsafe.As<System.Version>( new System.Version() );
		""";

		sourceCode = ApplyMemberQualification( sourceCode, "System.Runtime.CompilerServices.Unsafe.As", kind );

		CompileAndWalk( sourceCode, out var diagnostics );

		// (1,15): error SB500: Prohibited type 'System.Runtime.CompilerServices.Unsafe.As' used
		Assert.AreEqual( 1, diagnostics.Count );
		Assert.AreEqual( "SB500", diagnostics.FirstOrDefault().Id );
	}

	[TestMethod]
	public void MethodReturnValue()
	{
		var sourceCode = """
		public static object GetThing()
		{
		    return System.Runtime.CompilerServices.Unsafe.As<object>;
		}
		""";

		CompileAndWalk( sourceCode, out var diagnostics );

		Assert.AreEqual( 1, diagnostics.Count );
		Assert.AreEqual( "SB500", diagnostics.FirstOrDefault().Id );
	}

	[TestMethod]
	public void MethodParameter()
	{
		var sourceCode = """
		public static void DoSomething( object o )
		{
		    Log.Info( o );
		}

		DoSomething( System.Runtime.CompilerServices.Unsafe.As<object> );
		""";

		CompileAndWalk( sourceCode, out var diagnostics );

		Assert.AreEqual( 1, diagnostics.Count );
		Assert.AreEqual( "SB500", diagnostics.FirstOrDefault().Id );
	}

	[TestMethod]
	[DynamicData( nameof( MemberQualificationKinds ) )]
	public void MethodDelegateVariable( MemberQualificationKind kind )
	{
		var sourceCode = """
		var func = System.Runtime.CompilerServices.Unsafe.As<Version>;
		var t2 = func( new Version() );
		""";

		sourceCode = ApplyMemberQualification( sourceCode, "System.Runtime.CompilerServices.Unsafe.As", kind );

		CompileAndWalk( sourceCode, out var diagnostics );

		Assert.AreEqual( 1, diagnostics.Count );
		Assert.AreEqual( "SB500", diagnostics.FirstOrDefault().Id );
	}

	[TestMethod]
	[DynamicData( nameof( MemberQualificationKinds ) )]
	public void MethodDelegateAssignment( MemberQualificationKind kind )
	{
		var sourceCode = """
		System.Func<object, Version> late;
		late = System.Runtime.CompilerServices.Unsafe.As<Version>;

		var t2 = late( new Version() );
		""";

		sourceCode = ApplyMemberQualification( sourceCode, "System.Runtime.CompilerServices.Unsafe.As", kind );

		CompileAndWalk( sourceCode, out var diagnostics );

		Assert.AreEqual( 1, diagnostics.Count );
		Assert.AreEqual( "SB500", diagnostics.FirstOrDefault().Id );
	}

	[TestMethod]
	[DynamicData( nameof( MemberQualificationKinds ) )]
	public void MethodLambdaAlias( MemberQualificationKind kind )
	{
		var sourceCode = """
		System.Func<object, System.Version> alias = o => System.Runtime.CompilerServices.Unsafe.As<System.Version>(o);
		var t2 = alias(new System.Version());
		""";

		sourceCode = ApplyMemberQualification( sourceCode, "System.Runtime.CompilerServices.Unsafe.As", kind );

		CompileAndWalk( sourceCode, out var diagnostics );

		Assert.AreEqual( 1, diagnostics.Count );
		Assert.AreEqual( "SB500", diagnostics.FirstOrDefault().Id );
	}

	[TestMethod]
	[DynamicData( nameof( MemberQualificationKinds ) )]
	public void MethodGenericWrapper( MemberQualificationKind kind )
	{
		var sourceCode = """
		T UnsafeCast<T>(object o) where T : class => System.Runtime.CompilerServices.Unsafe.As<T>(o);
		var t2 = UnsafeCast<System.Version>(new System.Version());
		""";

		sourceCode = ApplyMemberQualification( sourceCode, "System.Runtime.CompilerServices.Unsafe.As", kind );

		CompileAndWalk( sourceCode, out var diagnostics );

		Assert.AreEqual( 1, diagnostics.Count );
		Assert.AreEqual( "SB500", diagnostics.FirstOrDefault().Id );
	}

	[TestMethod]
	[DynamicData( nameof( MemberQualificationKinds ) )]
	public void MethodGenericIdentity( MemberQualificationKind kind )
	{
		var sourceCode = """
		class UnsafeUtils
		{
			public static T Cast<T>( object o ) where T : class => System.Runtime.CompilerServices.Unsafe.As<T>( o );
		}

		var t2 = UnsafeUtils.Cast<System.Version>(new System.Version());
		""";

		sourceCode = ApplyMemberQualification( sourceCode, "System.Runtime.CompilerServices.Unsafe.As", kind );

		CompileAndWalk( sourceCode, out var diagnostics );

		Assert.AreEqual( 1, diagnostics.Count );
		Assert.AreEqual( "SB500", diagnostics.FirstOrDefault().Id );
	}

	[TestMethod]
	[DynamicData( nameof( MemberQualificationKinds ) )]
	public void MethodClassMemberAssignment( MemberQualificationKind kind )
	{
		var sourceCode = """
		using System;
		class Test
		{
			public Func<object, Version> Member = System.Runtime.CompilerServices.Unsafe.As<Version>;
		}

		var a = new Test();
		var b = a.Member( new Version() );
		""";

		sourceCode = ApplyMemberQualification( sourceCode, "System.Runtime.CompilerServices.Unsafe.As", kind );

		CompileAndWalk( sourceCode, out var diagnostics );

		Assert.AreEqual( 1, diagnostics.Count );
		Assert.AreEqual( "SB500", diagnostics.FirstOrDefault().Id );
	}

	[TestMethod]
	[DynamicData( nameof( MemberQualificationKinds ) )]
	public void MethodGroupArgument( MemberQualificationKind kind )
	{
		var sourceCode = """
		using System;
		void Run( Func<object, Version> func )
		{
			func( new Version() );
		}

		Run( System.Runtime.CompilerServices.Unsafe.As<Version> );
		""";

		sourceCode = ApplyMemberQualification( sourceCode, "System.Runtime.CompilerServices.Unsafe.As", kind );

		CompileAndWalk( sourceCode, out var diagnostics );

		Assert.AreEqual( 1, diagnostics.Count );
		Assert.AreEqual( "SB500", diagnostics.FirstOrDefault().Id );
	}

	/// <summary>
	/// Rewrite <paramref name="source"/> to replace references to <paramref name="fullyQualifiedMember"/> with
	/// a different kind of qualification.
	/// </summary>
	private string ApplyMemberQualification( string source, string fullyQualifiedMember, MemberQualificationKind kind )
	{
		var memberRegex = new Regex( @"^(?<namespace>[_A-Za-z0-9.]+)\.(?<type>[_A-Za-z0-9]+)\.(?<member>[^.]+)$" );

		if ( memberRegex.Match( fullyQualifiedMember ) is not { Success: true } match )
		{
			throw new ArgumentOutOfRangeException( nameof( fullyQualifiedMember ), "Expected fully qualified member." );
		}

		if ( !source.Contains( fullyQualifiedMember ) )
		{
			throw new ArgumentOutOfRangeException( nameof( source ), "Source doesn't contain specified member." );
		}

		var ns = match.Groups["namespace"].Value;
		var type = match.Groups["type"].Value;
		var memberName = match.Groups["member"].Value;

		switch ( kind )
		{
			case MemberQualificationKind.UsingStatic:
				return $"""
				using static {ns}.{type};
				{source.Replace( fullyQualifiedMember, memberName )}
				""";

			case MemberQualificationKind.Alias:
				return $"""
				using _alias = {ns}.{type};
				{source.Replace( fullyQualifiedMember, $"_alias.{memberName}" )}
				""";

			case MemberQualificationKind.UsingNamespace:
				return $"""
				using {ns};
				{source.Replace( fullyQualifiedMember, $"{type}.{memberName}" )}
				""";

			default:
				return source;
		}
	}
}
