using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Compiled shader entries (<see cref="ShaderCacheFile"/>) and the files their compiles read: an offline (shipped) compile
/// whose dependency is missing is used as it is, since a player's install has none of the private headers the build left
/// out and compiling again couldn't find them; one whose dependency changed, or a disk cache entry missing one, isn't.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ShaderCacheFileTests
{
	[TestMethod]
	public void OfflineEntriesKeepMissingDependenciesButNotChangedOnes()
	{
		var folder = Path.Combine( Path.GetTempPath(), "borealis-shadercachefile-test" );
		Directory.CreateDirectory( folder );
		var header = Path.Combine( folder, "math_general.fxc" );
		try
		{
			File.WriteAllText( header, "float Square( float x ) { return x * x; }" );
			string Resolve( string name ) => name.StartsWith( "<0>/", StringComparison.Ordinal ) ? Path.Combine( folder, name[4..] ) : null;
			byte[] Entry( string key ) => ShaderCacheFile.Write( key, [("<0>/math_general.fxc", PassCache.FileHash( header ))], w => w.Write( 42 ) );
			bool Read( byte[] bytes, string key, out int value ) => ShaderCacheFile.TryRead( bytes, key, Resolve, ( r, _ ) => r.ReadInt32(), out value );

			var offline = Entry( "" );
			var cached = Entry( "key" );
			Assert.IsTrue( Read( offline, "", out var value ) && value == 42, "an offline entry with its dependency as it was" );
			Assert.IsTrue( Read( cached, "key", out _ ), "a cache entry with its dependency as it was" );

			// Missing, as in a player's install: the offline entry is used, the disk cache's isn't
			File.Delete( header );
			PassCache.Recheck();
			Assert.IsTrue( Read( offline, "", out value ) && value == 42, "an offline entry whose dependency is missing is used as it is" );
			Assert.IsFalse( Read( cached, "key", out _ ), "a cache entry whose dependency is missing compiles again" );

			// Changed: neither
			File.WriteAllText( header, "float Square( float x ) { return x * x * 1.0; }" );
			PassCache.Recheck();
			Assert.IsFalse( Read( offline, "", out _ ), "an offline entry whose dependency changed compiles again" );
			Assert.IsFalse( Read( cached, "key", out _ ), "a cache entry whose dependency changed compiles again" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A pass module whose private header is missing (a player's install) takes what it declared from its offline compile:
	/// the header's combo is still one of its stage's, so draws ask for the values it was compiled with. Without the
	/// offline compile, or once the module changed, it's read from what's there.
	/// </summary>
	[TestMethod]
	public void PassModuleWithoutItsHeadersTakesItsOfflineDeclarations()
	{
		var folder = Path.Combine( Path.GetTempPath(), "borealis-passdeclarations-test" );
		Directory.CreateDirectory( folder );
		try
		{
			const string text = "#include \"sbox/pass.slangh\"\n#include \"private.fxc\"\n#if ( PROGRAM == VFX_PROGRAM_VS )\nDynamicCombo( D_OWN, 0..1, Sys( ALL ) );\n[shader( \"vertex\" )] float4 MainVs() : SV_Position { return 0; }\n#endif\n";
			var header = Path.Combine( folder, "private.fxc" );
			var path = Path.Combine( folder, "private_pass.slang" );
			File.WriteAllText( header, "DynamicCombo( D_PRIVATE, 0..1, Sys( ALL ) );\nfloat g_flPrivateValue;\n" );
			File.WriteAllText( path, text );
			static string[] Names( PassModule module ) => module.Combos( StageKind.Vertex ).Select( c => c.Name ).Order( StringComparer.Ordinal ).ToArray();

			var whole = PassModule.Read( path );
			CollectionAssert.AreEqual( new[] { "D_OWN", "D_PRIVATE" }, Names( whole ) );
			SlangOutput.Write( SlangOutput.PathOf( path ), "test", text, true, [], [], whole.Declarations() );

			File.Delete( header );
			var player = PassModule.Read( path );
			CollectionAssert.AreEqual( new[] { "D_OWN", "D_PRIVATE" }, Names( player ), "the missing header's combo, from the offline compile" );
			Assert.IsTrue( player.Has( StageKind.Vertex ) );
			Assert.IsTrue( player.BindingNames().Contains( "g_flPrivateValue" ), "and its names" );

			File.WriteAllText( path, text + "// changed\n" );
			CollectionAssert.AreEqual( new[] { "D_OWN" }, Names( PassModule.Read( path ) ), "a changed module is read from what's there" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}
}
