using System.IO;
using System.Runtime.InteropServices;

namespace Borealis.Backend.Meshlets;

/// <summary>
/// meshoptimizer's C API, from the engine's <c>meshopt</c> shared library (<c>src/meshopt</c>: <c>meshopt.dll</c>,
/// <c>libmeshopt.so</c> in <c>game/bin/&lt;platform&gt;</c>): meshlet building and simplification. The library is built
/// without its asserts (a failed one would hang the process on an abort dialog), so callers check their inputs.
/// </summary>
internal static unsafe partial class MeshOptimizer
{
	const string LibraryName = "meshopt";

	/// <summary>A meshlet as meshoptimizer writes it: offsets into its vertex and triangle arrays, and their counts.</summary>
	[StructLayout( LayoutKind.Sequential )]
	public struct Meshlet
	{
		public uint VertexOffset, TriangleOffset, VertexCount, TriangleCount;
	}

	static readonly object loadLock = new();
	static IntPtr library;
	static bool tried;

	/// <summary>Where to look first for the library; the engine's native folder beside the application otherwise.</summary>
	public static string Directory { get; set; }

	static MeshOptimizer()
	{
		// In the engine its own resolver (DLLImportResolver) is already set for this assembly and loads meshopt from the native folder
		try
		{
			NativeLibrary.SetDllImportResolver( typeof( MeshOptimizer ).Assembly, ( name, _, _ ) => name == LibraryName ? Load() : IntPtr.Zero );
		}
		catch ( InvalidOperationException )
		{
		}
	}

	/// <summary>Whether the library is there to call.</summary>
	public static bool Available => Load() != IntPtr.Zero;

	static IntPtr Load()
	{
		lock ( loadLock )
		{
			if ( tried ) return library;
			tried = true;
			foreach ( var directory in Candidates() )
			{
				var path = Path.Combine( directory, EngineFolders.Library( LibraryName ) );
				if ( File.Exists( path ) && NativeLibrary.TryLoad( path, out library ) ) return library;
			}

			return library = IntPtr.Zero;
		}
	}

	static IEnumerable<string> Candidates()
	{
		if ( !string.IsNullOrEmpty( Directory ) ) yield return Directory;

		// game/bin/managed beside game/bin/<platform>, and any game folder above the application (the tests' output)
		for ( var dir = new DirectoryInfo( AppContext.BaseDirectory ); dir is not null; dir = dir.Parent )
		{
			yield return Path.Combine( dir.FullName, EngineFolders.Native );
			yield return EngineFolders.NativeIn( Path.Combine( dir.FullName, "game" ) );
		}
	}

	/// <summary>meshoptimizer's <c>meshopt_Simplify*</c> options.</summary>
	[Flags]
	public enum SimplifyOptions : uint
	{
		None = 0,

		/// <summary>Keep the vertices on the mesh's border where they are: a range that meets another stays joined.</summary>
		LockBorder = 1 << 0,
		Sparse = 1 << 1,
		ErrorAbsolute = 1 << 2,
		Prune = 1 << 3,
		Regularize = 1 << 4,
		Permissive = 1 << 5,
		RegularizeLight = 1 << 6,
	}

	/// <summary>
	/// <c>meshopt_simplify</c>: indices at most <paramref name="targetIndexCount"/> long into <paramref name="destination"/>
	/// (which holds <paramref name="indexCount"/>), stopping at <paramref name="targetError"/> (relative to the mesh's extent
	/// unless <see cref="SimplifyOptions.ErrorAbsolute"/>); returns how many it wrote. Callers check their inputs: the library
	/// is built without its asserts.
	/// </summary>
	[LibraryImport( LibraryName, EntryPoint = "meshopt_simplify" )]
	public static partial nuint Simplify( uint* destination, uint* indices, nuint indexCount, float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride,
		nuint targetIndexCount, float targetError, SimplifyOptions options, float* resultError );

	/// <summary><c>meshopt_simplifyWithAttributes</c>: as <see cref="Simplify"/>, with attributes weighed into the error and vertices locked.</summary>
	[LibraryImport( LibraryName, EntryPoint = "meshopt_simplifyWithAttributes" )]
	public static partial nuint SimplifyWithAttributes( uint* destination, uint* indices, nuint indexCount, float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride,
		float* vertexAttributes, nuint vertexAttributesStride, float* attributeWeights, nuint attributeCount, byte* vertexLock, nuint targetIndexCount, float targetError,
		SimplifyOptions options, float* resultError );

	/// <summary><c>meshopt_simplifyScale</c>: the mesh's extent, which relative errors are fractions of.</summary>
	[LibraryImport( LibraryName, EntryPoint = "meshopt_simplifyScale" )]
	public static partial float SimplifyScale( float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride );

	[LibraryImport( LibraryName, EntryPoint = "meshopt_buildMeshletsBound" )]
	public static partial nuint BuildMeshletsBound( nuint indexCount, nuint maxVertices, nuint maxTriangles );

	[LibraryImport( LibraryName, EntryPoint = "meshopt_buildMeshlets" )]
	public static partial nuint BuildMeshlets( Meshlet* meshlets, uint* meshletVertices, byte* meshletTriangles, uint* indices, nuint indexCount,
		float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride, nuint maxVertices, nuint maxTriangles, float coneWeight );
}
