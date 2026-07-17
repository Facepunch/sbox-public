using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox;
using Sandbox.Generator;

namespace CompilingTests.CodeAccess;

/// <summary>
/// Compiles a snippet exactly as a sandboxed package build does and runs the single code-access
/// analyzer (<see cref="AccessWalker"/>) over it. One Roslyn walker enforces both the member-level
/// allow-list and the memory-unsafe hard blocks - there is no separate gate and no IL scan.
/// </summary>
static class CodeAccessGate
{
	/// <summary>Outcome of the unified analyzer for one snippet.</summary>
	public sealed class Result
	{
		/// <summary>Signatures the analyzer rejected (allow-list denials + hard-block hits).</summary>
		public IReadOnlyList<string> Denied { get; init; } = [];

		/// <summary>True if the snippet failed to compile (a test-authoring error, unless intended).</summary>
		public bool EmitFailed { get; init; }

		/// <summary>Compiler / generator errors, if <see cref="EmitFailed"/>.</summary>
		public IReadOnlyList<string> EmitErrors { get; init; } = [];

		public override string ToString() =>
			$"EmitFailed={EmitFailed} Denied=[{string.Join( ", ", Denied )}] EmitErrors=[{string.Join( ", ", EmitErrors )}]";
	}

	// The production base reference set (Compiler.References._references), minus the razor mock which is
	// added separately because it's the one entry that legitimately may be absent in unit tests.
	static readonly string[] BaseReferences =
	[
		"Sandbox.System",
		"Sandbox.Engine",
		"Sandbox.Filesystem",
		"Sandbox.Reflection",
		"Sandbox.Mounting",
	];

	/// <summary>
	/// Compile <paramref name="source"/> exactly as a sandboxed package build would (full BCL ref pack,
	/// Sandbox refs, source generators, whitelist on, unsafe off), then run the analyzer.
	///
	/// <paramref name="runGenerator"/> defaults to true (production faithful). Pass false ONLY to
	/// characterize the rules against raw, un-rewritten source - e.g. before generator redirects (like
	/// the ArrayPool.Shared -> PublicArrayPool rewrite) kick in.
	/// </summary>
	public static Result Verify( string source, bool runGenerator = true, string assemblyName = "package.test", bool allowUnsafe = false, AccessRules rules = null, IEnumerable<MetadataReference> extraReferences = null )
	{
		var config = new Compiler.Configuration { Whitelist = true, Unsafe = allowUnsafe };

		var syntaxTree = CSharpSyntaxTree.ParseText( source, config.GetParseOptions() );

		var options = new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary )
			.WithConcurrentBuild( true )
			.WithPlatform( Platform.AnyCpu )
			.WithOptimizationLevel( OptimizationLevel.Debug )
			.WithNullableContextOptions( NullableContextOptions.Disable )
			.WithAllowUnsafe( allowUnsafe );

		var references = BuildReferences().Concat( extraReferences ?? [] );
		var compilation = CSharpCompilation.Create( assemblyName, [syntaxTree], references, options );

		// Run generators before the walk, matching production (BuildInternal runs generators, then the
		// walker on the resulting trees).
		var processor = new Processor
		{
			AddonName = assemblyName,
			PackageAssetResolver = p => $"/{p}/model_mock.mdl",
			EnableCorelibPolyfills = true,
		};
		if ( runGenerator )
			processor.Run( compilation );

		// Generator errors mean the compilation isn't valid; production (BuildInternal) bails here, so
		// treat it as a compile failure rather than running the analyzer against a broken compilation.
		var generatorErrors = processor.Diagnostics
			.Where( d => d.Severity == DiagnosticSeverity.Error )
			.Select( d => d.ToString() )
			.ToList();

		if ( generatorErrors.Count > 0 )
			return new Result { EmitFailed = true, EmitErrors = generatorErrors };

		// A first production build walks all rewritten input and generator-added trees. IL-hotload lowering
		// happens afterward and is intentionally excluded because it may introduce trusted compiler calls.
		IEnumerable<SyntaxTree> walkTrees = runGenerator ? processor.BeforeILHotloadProcessingTrees : compilation.SyntaxTrees;
		if ( runGenerator )
			compilation = processor.Compilation;

		// A snippet that doesn't compile can't be analyzed meaningfully; surface it like production would.
		var compileErrors = compilation.GetDiagnostics()
			.Where( d => d.Severity == DiagnosticSeverity.Error )
			.Select( d => d.ToString() )
			.ToList();

		if ( compileErrors.Count > 0 )
			return new Result { EmitFailed = true, EmitErrors = compileErrors };

		return new Result { Denied = RunWalker( compilation, walkTrees, rules ) };
	}

	internal static IReadOnlyList<MetadataReference> BuildReferences()
	{
		var refs = new List<MetadataReference>( FrameworkReferences.All.Values );

		// Mandatory: a missing Sandbox reference means the harness is NOT compiling like production,
		// so let FindByName throw rather than silently produce a different compilation.
		foreach ( var name in BaseReferences )
			refs.Add( FrameworkReferences.FindByName( name ) );

		// Optional: the razor mock isn't loaded in unit tests. Only razor snippets need it, and those
		// surface a clear CS-level EmitError if it's absent.
		try { refs.Add( FrameworkReferences.FindByName( "Microsoft.AspNetCore.Components" ) ); }
		catch { /* intentionally optional in unit tests */ }

		return refs;
	}

	static List<string> RunWalker( CSharpCompilation compilation, IEnumerable<SyntaxTree> trees, AccessRules rules )
	{
		var found = new List<string>();
		foreach ( var tree in trees )
		{
			var walker = new AccessWalker( compilation.GetSemanticModel( tree ), rules: rules );
			walker.Visit( tree.GetRoot() );
			foreach ( var d in walker.Diagnostics )
				found.Add( d.GetMessage() );
		}
		return found;
	}
}
