using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using Sandbox.CodeUpgrader;
using Sandbox.Generator;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace CompilingTests;

[TestClass]
[DoNotParallelize]
public class ModelPhysicsCollectionUpgradeTest
{
	static readonly Lazy<MetadataReference[]> References = new( () =>
	{
		var runtime = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		return new[]
		{
			typeof( object ).Assembly.Location,
			Path.Combine( runtime, "System.Runtime.dll" ),
			Path.Combine( runtime, "System.Collections.dll" ),
			Path.Combine( runtime, "System.Linq.dll" ),
			typeof( ModelPhysics ).Assembly.Location,
			typeof( Vector3 ).Assembly.Location
		}.Distinct().Select( x => MetadataReference.CreateFromFile( x ) ).ToArray();
	} );

	static CSharpCompilation Compile( params string[] sources ) => CSharpCompilation.Create(
		$"modelphysics_upgrade_{Guid.NewGuid():N}",
		sources.Select( ( s, i ) => CSharpSyntaxTree.ParseText( s, path: $"source{i}.cs" ) ), References.Value,
		new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );

	static void AssertCompiles( CSharpCompilation compilation )
	{
		var errors = compilation.GetDiagnostics().Where( x => x.Severity == DiagnosticSeverity.Error ).ToArray();
		Assert.AreEqual( 0, errors.Length, string.Join( "\n", errors.Select( x => x.ToString() ) ) );
	}

	static CSharpCompilation Generate( CSharpCompilation input )
	{
		var processor = new Processor();
		processor.Run( CodeUpgrade.UpgradeAsync( input ).GetAwaiter().GetResult() );
		Assert.IsNull( processor.Exception, processor.Exception?.ToString() );
		Assert.IsFalse( processor.Diagnostics.Any( x => x.Severity == DiagnosticSeverity.Error ), string.Join( "\n", processor.Diagnostics ) );
		return processor.Compilation;
	}

	static bool Run( CSharpCompilation compilation )
	{
		AssertCompiles( compilation );
		using var stream = new MemoryStream();
		var emitted = compilation.Emit( stream );
		Assert.IsTrue( emitted.Success, string.Join( "\n", emitted.Diagnostics ) );
		return (bool)Assembly.Load( stream.ToArray() ).GetType( "Example" ).GetMethod( "Run" ).Invoke( null, null );
	}

	[TestMethod]
	public void ForwardingDeclarationsAndHelpersKeepTheLiveList()
	{
		var input = Compile( """
			using Sandbox;
			using System.Collections.Generic;
			public partial class Example
			{
				static NetList<ModelPhysics.Body> Live = [];
				static List<ModelPhysics.Body> Bodies => Live;
				static List<ModelPhysics.Body> GetBodies() { return Bodies; }
				static List<ModelPhysics.Body> Field = new();
				static void AddBody(List<ModelPhysics.Body> bodies) { Forward(bodies); }
				static void Forward(List<ModelPhysics.Body> bodies) { bodies.Add(default); }
			}
			""", """
			using Sandbox;
			using System.Collections.Generic;
			public partial class Example
			{
				static List<ModelPhysics.Body> Forwarded => GetBodies();
				public static bool Run()
				{
					List<ModelPhysics.Body> local = Forwarded;
					Field = local;
					AddBody(bodies: Field);
					var independent = new List<ModelPhysics.Body>();
					AddBody(independent);
					return ReferenceEquals(local, Live) && ReferenceEquals(Field, Live)
						&& Live.Count == 1 && independent.Count == 1;
				}
			}
			""" );
		var upgraded = Generate( input );
		Assert.IsTrue( Run( upgraded ) );
		Assert.AreEqual( "System.Collections.Generic.IList<Sandbox.ModelPhysics.Body>",
			upgraded.GetTypeByMetadataName( "Example" ).GetMembers( "AddBody" ).OfType<IMethodSymbol>().Single().Parameters[0].Type.ToDisplayString() );
		Assert.IsTrue( Run( Generate( input.WithAssemblyName( input.AssemblyName + "_repeat" ) ) ) );
	}

	[TestMethod]
	public void PublishedModelPhysicsPatternsCompile()
	{
		var input = Compile( """
			using Sandbox;
			using System.Collections.Generic;
			using System.Linq;
			using BodyList = System.Collections.Generic.List<Sandbox.ModelPhysics.Body>;
			public class Example
			{
				ModelPhysics physics;
				public BodyList Bodies => physics?.Bodies;
				public List<ModelPhysics.Joint> Joints { get { return physics?.Joints; } }
				void Replace()
				{
					physics.Bodies = new List<ModelPhysics.Body>();
					physics.Joints = new List<ModelPhysics.Joint>();
					physics.Bodies = physics.Bodies.Select(x => x).ToList();
					physics.Joints = physics.Joints.Where(x => true).ToList();
					physics.Bodies.ForEach(body => { });
					int removed = physics.Bodies.RemoveAll(body => false);
					physics.Joints.RemoveAll(joint => false);
				}
			}
			""" );
		AssertCompiles( Generate( input ) );
	}

	[TestMethod]
	public void ReplacementAssignmentsPreserveNullOrderAndEvaluateOnce()
	{
		var input = Compile( """
			using Sandbox;
			using System.Collections.Generic;
			using System.Linq;
			public class Example
			{
				static NetList<ModelPhysics.Body> Target;
				static int calls;
				static List<ModelPhysics.Body> Get(bool empty) { calls++; return empty ? null : new() { default, default }; }
				public static bool Run()
				{
					int __modelPhysicsList0 = 123;
					var assigned = (Target = Get(false));
					if (calls != 1 || Target.Count != 2 || !ReferenceEquals(assigned, Target)) return false;
					Target = Get(true);
					return calls == 2 && Target == null && __modelPhysicsList0 == 123;
				}
			}
			""" );
		Assert.IsTrue( Run( Generate( input ) ) );
	}

	[TestMethod]
	public void IndependentListsAndCopiesAreUnchanged()
	{
		var input = Compile( """
			using Sandbox;
			using System.Collections.Generic;
			using System.Linq;
			public class Example
			{
				ModelPhysics physics;
				List<ModelPhysics.Body> Bodies => physics.Bodies.ToList();
				List<ModelPhysics.Joint> Joints = new();
				void Use(List<ModelPhysics.Body> bodies) { bodies.RemoveAll(x => false); }
				void Run() { Use(physics.Bodies.Where(x => true).ToList()); Joints.ForEach(x => { }); }
			}
			""" );
		AssertCompiles( input );
		var generated = Generate( input );
		AssertCompiles( generated );
		Assert.AreEqual( "System.Collections.Generic.List<Sandbox.ModelPhysics.Body>",
			generated.GetTypeByMetadataName( "Example" ).GetMembers( "Bodies" ).OfType<IPropertySymbol>().Single().Type.ToDisplayString() );
	}

	[TestMethod]
	public void ForwardedReturnWithAnIndependentFallbackCompiles()
	{
		var input = Compile( """
			using Sandbox;
			using System.Collections.Generic;
			public class Example
			{
				static NetList<ModelPhysics.Joint> Live = [];
				static List<ModelPhysics.Joint> Get(bool live)
				{
					if(live) return Live;
					return new List<ModelPhysics.Joint>();
				}
				public static bool Run() => ReferenceEquals(Get(true), Live) && Get(false).Count == 0;
			}
			""" );
		Assert.IsTrue( Run( Generate( input ) ) );
	}

	[TestMethod]
	public void InterfaceContractsAreNotSilentlyChanged()
	{
		var input = Compile( """
			using Sandbox;
			using System.Collections.Generic;
			interface IRagdoll { List<ModelPhysics.Body> Bodies { get; } void Use(List<ModelPhysics.Body> bodies); }
			class Example : IRagdoll
			{
				ModelPhysics physics;
				public List<ModelPhysics.Body> Bodies => physics.Bodies;
				public void Use(List<ModelPhysics.Body> bodies) { }
				void Run() => Use(physics.Bodies);
			}
			""" );
		var generated = Generate( input );
		Assert.AreEqual( "System.Collections.Generic.List<Sandbox.ModelPhysics.Body>",
			generated.GetTypeByMetadataName( "Example" ).GetMembers( "Bodies" ).OfType<IPropertySymbol>().Single().Type.ToDisplayString() );
		Assert.AreEqual( "System.Collections.Generic.List<Sandbox.ModelPhysics.Body>",
			generated.GetTypeByMetadataName( "Example" ).GetMembers( "Use" ).OfType<IMethodSymbol>().Single().Parameters[0].Type.ToDisplayString() );
		Assert.IsTrue( generated.GetDiagnostics().Any( x => x.Severity == DiagnosticSeverity.Error ), "Unsupported contracts must still require a source fix" );
	}

	[TestMethod]
	public async Task ArchiveRebuildsRegenerateForwardersWithoutChangingSource()
	{
		const string legacy = """
			global using RagdollCollection = System.Collections.Generic.List<Sandbox.ModelPhysics.Body>;
			public class Provider
			{
				public static Sandbox.NetList<Sandbox.ModelPhysics.Body> Bodies { get; } = new();
			}
			""";
		const string independent = """
			global using RagdollCollection = System.Collections.Generic.List<int>;
			public class Provider
			{
				public static RagdollCollection Bodies { get; } = new() { 42 };
			}
			""";
		const string forwarding = "public class Example { public static RagdollCollection Bodies => Provider.Bodies; }";

		using var group = new CompileGroup( "ModelPhysics archive upgrade" );
		using var compiler = group.GetOrCreateCompiler( "modelphysics_upgrade" );
		foreach ( var source in new[] { legacy, legacy, independent, independent, legacy } )
		{
			var archive = new CodeArchive { CompilerName = compiler.Name, Configuration = new Compiler.Configuration() };
			archive.References.UnionWith( new[] { "Sandbox.Engine", "Sandbox.System" } );
			archive.SyntaxTrees.AddRange( Compile( source, forwarding ).SyntaxTrees );
			// Exercise the same serialization boundary as downloaded .cll files.
			var bytes = archive.Serialize();
			archive = new CodeArchive( bytes );
			compiler.UpdateFromArchive( archive );
			await group.BuildAsync();
			Assert.IsTrue( group.BuildResult.Success, compiler.Output.Exception?.ToString() ?? group.BuildResult.BuildDiagnosticsString() );
			CollectionAssert.AreEqual( bytes, archive.Serialize(), "Compilation must leave the published archive unchanged" );

			var result = CSharpCompilation.Create( "inspect", references: References.Value.Append(
				MetadataReference.CreateFromImage( compiler.Output.AssemblyData ) ) );
			var property = result.GetTypeByMetadataName( "Example" ).GetMembers( "Bodies" ).OfType<IPropertySymbol>().Single();
			Assert.AreEqual( source == legacy ? "Sandbox.NetList<Sandbox.ModelPhysics.Body>" : "System.Collections.Generic.List<int>", property.Type.ToDisplayString() );
		}
	}

	[TestMethod]
	public async Task LocalProjectsApplyFixesExplicitlyAndRetainFastHotload()
	{
		const string legacy = """
			using Sandbox;
			using System.Collections.Generic;
			public class Example
			{
				public static NetList<ModelPhysics.Body> Live { get; } = new();
				public static List<ModelPhysics.Body> Bodies => Live;
				public static int Count() { return Bodies.Count; }
			}
			""";
		var files = new MemoryFileSystem();
		files.WriteAllText( "/Example.cs", legacy );
		files.WriteAllText( "/Unchanged.cs", "public class Unchanged { public int Value() { return 42; } }" );
		using var group = new CompileGroup( "ModelPhysics local hotload" ) { AllowFastHotload = true };
		using var compiler = group.GetOrCreateCompiler( "modelphysics_local" );
		compiler.SetConfiguration( new Compiler.Configuration() );
		compiler.AddReference( "Sandbox.Engine" );
		compiler.AddReference( "Sandbox.System" );
		compiler.AddSourceLocation( files );
		compiler.MarkForRecompile();
		await group.BuildAsync();
		Assert.IsFalse( group.BuildResult.Success, "Local source must require an explicit fix, not an automatic upgrade" );
		Assert.IsTrue( compiler.Output.Diagnostics.Any( x => x.Id == "CS0029" ) );
		Assert.AreEqual( legacy, files.ReadAllText( "/Example.cs" ) );

		// Persist the same source edit offered by the IDE code fix, then hotload a method-body change.
		var upgraded = await CodeUpgrade.UpgradeAsync( Compile( legacy ) );
		var fixedSource = upgraded.SyntaxTrees.Single().ToString();
		files.WriteAllText( "/Example.cs", fixedSource );
		compiler.MarkForRecompile();
		await group.BuildAsync();
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		var previous = compiler.incrementalState.Compilation;
		Assert.IsNotNull( previous );
		var unchanged = previous.SyntaxTrees.Single( x => Path.GetFileName( x.FilePath ) == "Unchanged.cs" );

		files.WriteAllText( "/Example.cs", fixedSource.Replace( "return Bodies.Count;", "return Bodies.Count + 1;" ) );
		compiler.MarkForRecompile();
		await group.BuildAsync();
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		var current = compiler.incrementalState.Compilation;
		Assert.IsNotNull( current );
		Assert.IsTrue( current.SyntaxTrees.Contains( unchanged ), "Unchanged generated trees should be reused" );
		Assert.IsTrue( current.Assembly.GetAttributes().Any( x => x.AttributeClass.Name == "SupportsILHotloadAttribute" ),
			"A method-body edit should retain the fast IL hotload path" );
	}

	[TestMethod]
	public async Task ForwardingAcrossFilesRechecksEscapedReferences()
	{
		var input = Compile( """
			global using Bodies = System.Collections.Generic.List<Sandbox.ModelPhysics.Body>;
			class Source
			{
				public static Sandbox.ModelPhysics Physics = new();
				public static Bodies Items => Physics.Bodies;
			}
			""", """
			class Reader { public static Bodies Get() => Source.It\u0065ms; }
			""", """
			public class Example
			{
				static int Count(Bodies bodies) => bodies.Count;
				public static bool Run() => ReferenceEquals(Reader.G\u0065t(), Source.Physics.Bodies) && Count(Reader.Get()) == 0;
			}
			""" );
		Assert.IsTrue( Run( await CodeUpgrade.UpgradeAsync( input ) ) );
	}

	[TestMethod]
	public async Task UnrelatedConversionErrorsDoNotTriggerAnUpgrade()
	{
		var input = Compile( """
			using Sandbox;
			using System.Collections.Generic;
			class Example
			{
				ModelPhysics physics = new();
				List<ModelPhysics.Body> independent = new();
				int Count() => physics.Bodies.Count + independent.Count;
				int Broken() => "unrelated error";
			}
			""" );
		Assert.IsTrue( input.GetDiagnostics().Any( d => d.Id == "CS0029" ) );
		var diagnostics = await input.WithAnalyzers( ImmutableArray.Create<DiagnosticAnalyzer>( new ModelPhysicsCollectionAnalyzer() ) ).GetAnalyzerDiagnosticsAsync();
		Assert.AreEqual( 0, diagnostics.Length );
		Assert.AreSame( input, await CodeUpgrade.UpgradeAsync( input ) );
	}

	[TestMethod]
	public async Task ConversionDiagnosticsHandleAliasesAndNamedArguments()
	{
		var input = Compile( """
			global using Bodies = System.Collections.Generic.List<Sandbox.ModelPhysics.Body>;
			global using BodyView = System.Collections.Generic.IList<Sandbox.ModelPhysics.Body>;
			""", """
			using Sandbox;
			public class Example
			{
				static ModelPhysics physics = new();
				static BodyView View => physics.Bodies;
				static Bodies Forward => View;
				static int Count(int offset, Bodies bodies) => offset + bodies.Count;
				public static bool Run()
				{
					physics.Bodies.Add(default);
					return ReferenceEquals(Forward, physics.Bodies) && Count(bodies: physics.Bodies, offset: 2) == 3;
				}
			}
			""" );
		var errors = input.GetDiagnostics();
		Assert.IsTrue( errors.Any( d => d.Id == "CS0266" ) );
		Assert.IsTrue( errors.Any( d => d.Id == "CS1503" ) );
		Assert.IsTrue( Run( await CodeUpgrade.UpgradeAsync( input ) ) );
	}

	[TestMethod]
	[DataRow( UpgradeFailure.Analyzer, false )]
	[DataRow( UpgradeFailure.Fix, false )]
	[DataRow( UpgradeFailure.Analyzer, true )]
	[DataRow( UpgradeFailure.Fix, true )]
	[DataRow( UpgradeFailure.PartialFix, false )]
	[DataRow( UpgradeFailure.Cancellation, false )]
	public async Task FailedAutomaticUpgradesFallBackToOriginalSource( UpgradeFailure failure, bool needsUpgrade )
	{
		// Inject faults into the discovered fixer list without adding a runtime test hook.
		// This fixture is non-parallel; always restore the original entry after the build.
		var fixerTypes = (Type[])typeof( CodeUpgrade ).GetField( "FixerTypes", BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null );
		var originalFixer = fixerTypes[0];
		InjectedFailure = failure;
		fixerTypes[0] = typeof( FailingUpgradeFix );
		try
		{
			using var group = new CompileGroup( "Upgrade failure fallback" );
			using var compiler = group.GetOrCreateCompiler( $"upgrade_failure_{Guid.NewGuid():N}" );
			var source = needsUpgrade
				? "class Example { Sandbox.ModelPhysics physics; System.Collections.Generic.List<Sandbox.ModelPhysics.Body> Bodies => physics.Bodies; }"
				: "public class Example { public static int Value() => 42; }";
			var archive = new CodeArchive { CompilerName = compiler.Name, Configuration = new Compiler.Configuration() };
			archive.References.UnionWith( new[] { "Sandbox.Engine", "Sandbox.System" } );
			archive.SyntaxTrees.AddRange( Compile( source ).SyntaxTrees );
			var originalArchive = archive.Serialize();
			archive = new CodeArchive( originalArchive );
			compiler.UpdateFromArchive( archive );
			await group.BuildAsync();
			CollectionAssert.AreEqual( originalArchive, archive.Serialize() );

			if ( failure == UpgradeFailure.Cancellation )
			{
				Assert.IsFalse( group.BuildResult.Success );
				Assert.IsInstanceOfType<OperationCanceledException>( compiler.Output.Exception );
				return;
			}

			Assert.IsNull( compiler.Output.Exception, compiler.Output.Exception?.ToString() );
			if ( needsUpgrade )
			{
				Assert.IsFalse( group.BuildResult.Success );
				Assert.IsTrue( compiler.Output.Diagnostics.Any( d => d.Id == "CS0029" ), "Unfixed source must retain its normal compiler diagnostics" );
			}
			else
			{
				Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
				var assembly = Assembly.Load( compiler.Output.AssemblyData );
				Assert.AreEqual( 42, assembly.GetType( "Example" ).GetMethod( "Value" ).Invoke( null, null ) );
			}
		}
		finally
		{
			fixerTypes[0] = originalFixer;
		}
	}

	public enum UpgradeFailure { Analyzer, Fix, PartialFix, Cancellation }
	static UpgradeFailure InjectedFailure;

#pragma warning disable RS1001 // Injected only by this test; must not be discoverable as a real analyzer.
	public sealed class FailingUpgradeAnalyzer : Analyzer
	{
		public override DiagnosticDescriptor Rule { get; } = new( "TEST001", "Injected upgrade failure", "Injected upgrade failure", "Test", DiagnosticSeverity.Warning, true );

		public override void Init( AnalysisContext context ) => context.RegisterCompilationAction( analysis =>
		{
			if ( InjectedFailure == UpgradeFailure.Analyzer ) throw new InvalidOperationException( "Injected analyzer crash" );
			analysis.ReportDiagnostic( Diagnostic.Create( Rule, analysis.Compilation.SyntaxTrees.First().GetRoot().GetLocation() ) );
		} );
	}
#pragma warning restore RS1001

	public sealed class FailingUpgradeFix : Fixer<FailingUpgradeAnalyzer>
	{
		public override bool CanApplyDuringCompilation( System.Collections.Generic.IEnumerable<SyntaxTree> trees )
		{
			if ( InjectedFailure == UpgradeFailure.Cancellation ) throw new OperationCanceledException();
			return true;
		}

		public override async Task RegisterCodeFixesAsync( CodeFixContext context )
		{
			if ( InjectedFailure == UpgradeFailure.PartialFix )
			{
				var root = await context.Document.GetSyntaxRootAsync( context.CancellationToken );
				var token = root.DescendantTokens().FirstOrDefault( t => t.IsKind( SyntaxKind.NumericLiteralToken ) && t.ValueText == "42" );
				if ( token.RawKind != 0 )
					context.RegisterCodeFix( CodeAction.Create( "Change the original source", _ => Task.FromResult(
						context.Document.WithSyntaxRoot( root.ReplaceToken( token, SyntaxFactory.Literal( 43 ).WithTriviaFrom( token ) ) ) ), nameof( FailingUpgradeFix ) ), context.Diagnostics );
			}
			// Offer no action (after one edit in PartialFix mode), violating the fix contract.
		}
	}

	[TestMethod]
	public async Task AutomaticUpgradesDoNotApplyManualCodeFixes()
	{
		var input = Compile( "class Example { [HostSync] public int Value { get; set; } }" );
		var diagnostics = await input.WithAnalyzers( ImmutableArray.Create<DiagnosticAnalyzer>( new HostSyncAttributeAnalyzer() ) ).GetAnalyzerDiagnosticsAsync();
		Assert.IsTrue( diagnostics.Any( d => d.Id == "SBOX004" ), "This source has an existing manual code fix" );
		Assert.AreSame( input, await CodeUpgrade.UpgradeAsync( input ) );
	}

	[TestMethod]
	public async Task AutomaticUpgradePreservesInputsAndIsIdempotent()
	{
		var input = Compile( """
			using Sandbox;
			using System.Collections.Generic;
			class Example { ModelPhysics physics; public List<ModelPhysics.Body> Bodies => physics.Bodies; }
			""", "class Unrelated { public int Value => 42; }" );
		var original = input.SyntaxTrees.First().ToString();
		var upgraded = await CodeUpgrade.UpgradeAsync( input );
		AssertCompiles( upgraded );
		Assert.AreEqual( original, input.SyntaxTrees.First().ToString() );
		Assert.AreSame( input.SyntaxTrees.Last(), upgraded.SyntaxTrees.Last(), "Unchanged trees retain their identity" );
		Assert.AreSame( upgraded, await CodeUpgrade.UpgradeAsync( upgraded ), "Already-upgraded code must be a no-op" );
	}

	[TestMethod]
	public async Task AutomaticUpgradePreservesPerFileConditionalCompilation()
	{
		var input = Compile( """
			using Sandbox;
			using System.Collections.Generic;
			#if SERVER
			class Example { ModelPhysics physics; public List<ModelPhysics.Body> Bodies => physics.Bodies; }
			#else
			#error Incorrect server defines
			#endif
			""", """
			#if SERVER
			#error Incorrect client defines
			#else
			class ClientOnly { }
			#endif
			""" );
		var first = input.SyntaxTrees.First();
		var server = CSharpSyntaxTree.ParseText( first.GetText(), CSharpParseOptions.Default.WithPreprocessorSymbols( "SERVER" ), first.FilePath );
		input = input.ReplaceSyntaxTree( first, server );
		var upgraded = await CodeUpgrade.UpgradeAsync( input );
		AssertCompiles( upgraded );
		CollectionAssert.AreEqual( new[] { "SERVER" }, ((CSharpParseOptions)upgraded.SyntaxTrees.First().Options).PreprocessorSymbolNames.ToArray() );
		Assert.AreSame( input.SyntaxTrees.Last(), upgraded.SyntaxTrees.Last() );
	}

}
