using Microsoft.CodeAnalysis.CSharp;
using System;

namespace CompilingTests;

// A network archive update that only removes or edits one file can rebind an unchanged file onto blacklisted API.
[TestClass]
[DoNotParallelize]
public class IncrementalBlacklistTest
{
	const string Shadow = """
		namespace Shadowed
		{
			public static class Unsafe
			{
				public static TTo As<TFrom, TTo>( ref TFrom value ) => default;
			}
		}
		""";

	const string ShadowEdited = """
		namespace Shadowed
		{
			public static class NotUnsafe
			{
			}
		}
		""";

	const string User = """
		using System.Runtime.CompilerServices;

		namespace Shadowed
		{
			public static class User
			{
				public static int Bits()
				{
					float value = 1.0f;
					return Unsafe.As<float, int>( ref value );
				}
			}
		}
		""";

	const string Other = "namespace Shadowed { public static class Other { public static int Value => 1; } }";

	static async Task<CompilerOutput> BuildArchive( CompileGroup group, Compiler compiler, params (string Path, string Source)[] files )
	{
		var archive = new CodeArchive { CompilerName = compiler.Name, Configuration = new Compiler.Configuration() };
		archive.References.UnionWith( new[] { "Sandbox.Engine", "Sandbox.System" } );

		foreach ( var (path, source) in files )
		{
			archive.SyntaxTrees.Add( CSharpSyntaxTree.ParseText( source, path: path ) );
		}

		// Round trip like a networked archive, so every update brings fresh tree instances
		compiler.UpdateFromArchive( new CodeArchive( archive.Serialize() ) );
		await group.BuildAsync();

		return compiler.Output;
	}

	static void AssertBlacklisted( CompilerOutput output )
	{
		Assert.IsFalse( output.Successful, "Rebound unchanged file must not compile" );
		Assert.IsTrue( output.Diagnostics.Any( x => x.Id == "SB500" ),
			"Expected a blacklist diagnostic, got:\n" + string.Join( "\n", output.Diagnostics ) );
	}

	[TestMethod]
	public async Task Removing_Shadow_Rescans_Unchanged_File()
	{
		using var group = new CompileGroup( "IncrementalBlacklist" );
		using var compiler = group.GetOrCreateCompiler( $"incremental_blacklist_{Guid.NewGuid():N}" );

		var first = await BuildArchive( group, compiler, ("Shadow.cs", Shadow), ("User.cs", User) );
		Assert.IsTrue( first.Successful, string.Join( "\n", first.Diagnostics ) );

		AssertBlacklisted( await BuildArchive( group, compiler, ("User.cs", User) ) );
	}

	[TestMethod]
	public async Task Editing_Shadow_Rescans_Unchanged_File()
	{
		using var group = new CompileGroup( "IncrementalBlacklist" );
		using var compiler = group.GetOrCreateCompiler( $"incremental_blacklist_{Guid.NewGuid():N}" );

		var first = await BuildArchive( group, compiler, ("Shadow.cs", Shadow), ("User.cs", User) );
		Assert.IsTrue( first.Successful, string.Join( "\n", first.Diagnostics ) );

		AssertBlacklisted( await BuildArchive( group, compiler, ("Shadow.cs", ShadowEdited), ("User.cs", User) ) );
	}

	[TestMethod]
	public async Task Removing_File_Keeps_Legitimate_Code_Compiling()
	{
		using var group = new CompileGroup( "IncrementalBlacklist" );
		using var compiler = group.GetOrCreateCompiler( $"incremental_blacklist_{Guid.NewGuid():N}" );

		var first = await BuildArchive( group, compiler, ("Shadow.cs", Shadow), ("Other.cs", Other) );
		Assert.IsTrue( first.Successful, string.Join( "\n", first.Diagnostics ) );

		var second = await BuildArchive( group, compiler, ("Other.cs", Other) );
		Assert.IsTrue( second.Successful, string.Join( "\n", second.Diagnostics ) );
	}
}
