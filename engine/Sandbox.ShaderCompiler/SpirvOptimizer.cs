using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// vfx's spirv-opt recipe (vfx_spirv_fixups.cpp's <c>SPIRV_OPT_DEFAULT_RECIPE</c>, or <c>VFX_SPIRVOPT</c> as vfx reads it)
/// over the SPIR-V Slang emits, as vfx runs it over a .shader's: Slang compiles at optimization none and this is the one
/// spirv-opt run. It strips dead code and the bindings only dead code read, and shrinks the module. Runs through
/// <c>spirv_optimizer.dll</c> beside <c>slang.dll</c>; where that isn't built (it's Windows only) Slang optimizes at its
/// default level instead.
/// </summary>
internal static unsafe class SpirvOptimizer
{
	/// <summary>vfx's default recipe: Slang's own default-level passes, then spirv-opt's size passes.</summary>
	const string DefaultRecipe = "--wrap-opkill --eliminate-dead-branches --merge-return --inline-entry-points-exhaustive --eliminate-dead-functions " +
		"--eliminate-dead-code-aggressive --private-to-local --scalar-replacement=100 --ccp --simplify-instructions --convert-local-access-chains " +
		"--eliminate-local-single-block --eliminate-dead-code-aggressive --vector-dce -Os";

	static readonly object loadLock = new();
	static readonly Dictionary<string, (IntPtr Run, IntPtr Free, string Identity)> libraries = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The recipe: vfx's, unless <c>VFX_SPIRVOPT</c> replaces it (blank for none).</summary>
	public static string Recipe { get; } = Environment.GetEnvironmentVariable( "VFX_SPIRVOPT" ) ?? DefaultRecipe;

	static (IntPtr Run, IntPtr Free, string Identity) Library( string directory )
	{
		lock ( loadLock )
		{
			if ( libraries.TryGetValue( directory, out var known ) ) return known;

			var path = Path.Combine( directory, OperatingSystem.IsWindows() ? "spirv_optimizer.dll" : "libspirv_optimizer.so" );
			var result = (IntPtr.Zero, IntPtr.Zero, "none");
			if ( !string.IsNullOrWhiteSpace( Recipe ) && File.Exists( path ) && NativeLibrary.TryLoad( path, out var library )
				&& NativeLibrary.TryGetExport( library, "SpirvOptimizerRun", out var run ) && NativeLibrary.TryGetExport( library, "SpirvOptimizerFree", out var free ) )
			{
				var hash = Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( path ) ) );
				result = (run, free, $"{hash}:{Recipe}");
			}

			return libraries[directory] = result;
		}
	}

	/// <summary>Whether the optimizer in <paramref name="directory"/> runs, so Slang leaves optimizing to it.</summary>
	public static bool Available( string directory ) => Library( directory ).Run != IntPtr.Zero;

	/// <summary>What the optimizer is, for compile cache keys: the library's contents and the recipe, or "none".</summary>
	public static string Identity( string directory ) => Library( directory ).Identity;

	/// <summary>
	/// <paramref name="spirv"/> optimized with <see cref="Recipe"/>; as it was where the optimizer isn't available or fails
	/// (as vfx does, a bad recipe costs the optimization rather than the shader), with spirv-opt's messages in
	/// <paramref name="failure"/>.
	/// </summary>
	public static byte[] Optimize( string directory, byte[] spirv, out string failure )
	{
		failure = null;
		var (run, free, _) = Library( directory );
		if ( run == IntPtr.Zero ) return spirv;

		var recipe = Encoding.UTF8.GetBytes( Recipe + "\0" );
		uint* output = null;
		nuint outputWords = 0;
		byte* messages = null;
		int ok;
		fixed ( byte* r = recipe, words = spirv )
			ok = ((delegate* unmanaged< byte*, byte*, nuint, uint**, nuint*, byte**, int >)run)( r, words, (nuint)(spirv.Length / 4), &output, &outputWords, &messages );

		var release = (delegate* unmanaged< void*, void >)free;
		if ( ok == 0 )
		{
			failure = messages is null ? "spirv-opt failed" : Marshal.PtrToStringUTF8( (IntPtr)messages );
			if ( messages is not null ) release( messages );
			return spirv;
		}

		var result = new ReadOnlySpan<byte>( output, checked((int)outputWords * 4) ).ToArray();
		release( output );
		return result;
	}
}
