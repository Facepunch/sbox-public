using System.Linq;

namespace CompilingTests.CodeAccess;

/// <summary>
/// Counterweight to the escape and lowering tests - every construct here lowers to members a
/// sandboxed package is meant to use, and must not be denied.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CodeAccessLegalCodeTest
{
	[TestMethod]
	public void EverydayLanguageConstructs_AreAllowed()
	{
		var result = CodeAccessGate.Verify( """
			using System.Collections.Generic;
			using System.Linq;
			using System.Threading.Tasks;

			public record Thing( string Name, int Value );

			public static class Probe
			{
				static readonly object Gate = new object();

				public static async Task<string> Run( List<Thing> things )
				{
					lock ( Gate ) { }

					var names = new List<string>();
					foreach ( var thing in things )
						names.Add( thing.Name );

					var lookup = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };
					foreach ( var (key, value) in lookup )
						names.Add( $"{key}={value}" );

					using var stream = new System.IO.MemoryStream();
					using ( var writer = new System.IO.StringWriter() )
						writer.Write( "hello" );

					var total = things.Where( x => x.Value > 0 ).Sum( x => x.Value );

					var numbers = new int[] { 1, 2, 3 };
					System.Span<int> span = numbers;
					System.ReadOnlySpan<int> readOnly = span;
					var slice = numbers[1..];

					System.Memory<int> memory = numbers;
					System.ReadOnlyMemory<int> readOnlyMemory = memory;

					await Task.Delay( 1 );

					return $"{string.Join( ",", names )} {total} {readOnly.Length} {slice.Length} {readOnlyMemory.Length}";
				}
			}
			""" );

		Assert.IsFalse( result.EmitFailed, $"fixture should compile: {result}" );
		Assert.AreEqual( 0, result.Denied.Count, $"legal addon code must not be denied: {result}" );
	}

	[TestMethod]
	public void StackAllocAndSpanIndexing_AreAllowed()
	{
		var result = CodeAccessGate.Verify( """
			public static class Probe
			{
				public static int Run()
				{
					System.Span<int> span = stackalloc int[4];
					span[0] = 1;
					span.Fill( 2 );

					System.ReadOnlySpan<int> readOnly = span;
					return readOnly[0] + span.Length;
				}
			}
			""" );

		Assert.IsFalse( result.EmitFailed, $"fixture should compile: {result}" );
		Assert.AreEqual( 0, result.Denied.Count, $"stackalloc spans must not be denied: {result}" );
	}

	/// <summary>
	/// The compiler-extra tree gets no exemption, so everything <see cref="Compiler"/> emits into it
	/// must be allow-listed on its own merits. If this fails, every sandboxed build fails.
	/// </summary>
	[TestMethod]
	public async Task GeneratedCompilerExtraTree_PassesTheAllowList()
	{
		var fileSystem = new MemoryFileSystem();
		fileSystem.WriteAllText( "/Probe.cs", "public static class Probe { public static int Run() => 1; }" );

		var settings = new Compiler.Configuration();
		settings.Clean();

		using var group = new CompileGroup( "GeneratedExtraTree" );
		var compiler = group.CreateCompiler( "generated.extra.tree", null, settings );
		compiler.AddSourceLocation( fileSystem );

		// Exactly what Project.UpdateCompiler and EditorUtility.Projects.Compile append.
		compiler.GeneratedCode.AppendLine( "global using static Sandbox.Internal.GlobalGameNamespace;" );
		compiler.GeneratedCode.AppendLine( "[assembly: global::System.Reflection.AssemblyMetadata( \"Ident\", \"generated.extra\" )]" );
		compiler.MarkForRecompile();

		await group.BuildAsync();

		var denied = compiler.Output.Diagnostics.Where( x => x.Id == "SB500" ).Select( x => x.GetMessage() ).ToArray();
		Assert.AreEqual( 0, denied.Length, $"the generated compiler-extra tree must pass on its own: [{string.Join( " | ", denied )}]" );
		Assert.IsTrue( compiler.Output.Successful, group.BuildResult.BuildDiagnosticsString() );
	}

	/// <summary>
	/// The allow-list carries no entries for `Unsafe.*` or `InlineArray*` - they're hard blocked, and
	/// the constructs that lower to them are lowered during emit, after the walk. This compiles those
	/// constructs for real, through emit, to prove the walker never sees the lowered members.
	/// </summary>
	[TestMethod]
	public async Task ConstructsThatLowerToBlockedMembers_StillBuild()
	{
		var fileSystem = new MemoryFileSystem();
		fileSystem.WriteAllText( "/Lowering.cs", """
			using System;
			using System.Collections.Generic;

			public static class Lowering
			{
				// collection expressions -> InlineArrayN<T> buffers
				public static ReadOnlySpan<int> Constants => [1, 2, 3, 4, 5];
				public static List<int> Collection => [1, 2, 3];

				// params ReadOnlySpan<T> call site -> InlineArrayN<T>
				static int Total( params ReadOnlySpan<int> values )
				{
					var sum = 0;
					foreach ( var value in values ) sum += value;
					return sum;
				}

				// UTF-8 literal -> RuntimeHelpers.CreateSpan over the data section
				public static ReadOnlySpan<byte> Utf8 => "hello"u8;

				public static int Run()
				{
					Span<int> span = stackalloc int[4];
					span[0] = 1;
					span.Fill( 2 );

					// Span indexing / iteration -> Unsafe.Add
					var indexed = 0;
					for ( var i = 0; i < span.Length; i++ ) indexed += span[i];
					foreach ( var value in Constants ) indexed += value;

					var array = new[] { 1, 2, 3 };
					var sliced = array[1..];

					return indexed + Total( 1, 2, 3 ) + Total( Constants ) + Utf8.Length + sliced.Length + Collection.Count;
				}
			}
			""" );

		var settings = new Compiler.Configuration();
		settings.Clean();

		using var group = new CompileGroup( "LoweringBuild" );
		var compiler = group.CreateCompiler( "lowering.build", null, settings );
		compiler.AddSourceLocation( fileSystem );
		compiler.MarkForRecompile();

		await group.BuildAsync();

		var denied = compiler.Output.Diagnostics.Where( x => x.Id == "SB500" ).Select( x => x.GetMessage() ).ToArray();
		Assert.AreEqual( 0, denied.Length, $"lowering must not trip a hard block: [{string.Join( " | ", denied )}]" );
		Assert.IsTrue( compiler.Output.Successful, group.BuildResult.BuildDiagnosticsString() );
	}

	/// <summary>
	/// Unchanged trees are re-analyzed every incremental build, which must not fail a clean rebuild.
	/// </summary>
	[TestMethod]
	public async Task IncrementalRebuild_OfLegalCodeStaysClean()
	{
		var fileSystem = new MemoryFileSystem();
		fileSystem.WriteAllText( "/Untouched.cs", """
			public static class Untouched
			{
				public static int Sum( System.Collections.Generic.List<int> values )
				{
					var total = 0;
					foreach ( var value in values ) total += value;
					return total;
				}
			}
			""" );
		fileSystem.WriteAllText( "/Edited.cs", "public static class Edited { public static int Value => 1; }" );

		using var group = new CompileGroup( "IncrementalLegal" );
		var compiler = group.GetOrCreateCompiler( "incremental.legal" );
		compiler.AddSourceLocation( fileSystem );
		compiler.MarkForRecompile();

		await group.BuildAsync();
		Assert.IsTrue( compiler.Output.Successful, group.BuildResult.BuildDiagnosticsString() );

		group.Reset();

		fileSystem.WriteAllText( "/Edited.cs", "public static class Edited { public static int Value => 2; }" );
		compiler.MarkForRecompile();
		await group.BuildAsync();

		Assert.IsTrue( compiler.Output.Successful, group.BuildResult.BuildDiagnosticsString() );
		Assert.AreEqual( 0, compiler.Output.Diagnostics.Count( x => x.Id == "SB500" ) );
	}
}
