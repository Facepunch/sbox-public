using System.IO;
using System.Runtime.InteropServices;

namespace Borealis.Backend.Meshlets;

/// <summary>
/// meshoptimizer's C API, from the engine's <c>meshopt</c> shared library (<c>src/meshopt</c>: <c>meshopt.dll</c>,
/// <c>libmeshopt.so</c> in <c>game/bin/&lt;platform&gt;</c>). Only what meshlet building calls.
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
		NativeLibrary.SetDllImportResolver( typeof( MeshOptimizer ).Assembly, ( name, _, _ ) => name == LibraryName ? Load() : IntPtr.Zero );
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

	[LibraryImport( LibraryName, EntryPoint = "meshopt_buildMeshletsBound" )]
	public static partial nuint BuildMeshletsBound( nuint indexCount, nuint maxVertices, nuint maxTriangles );

	[LibraryImport( LibraryName, EntryPoint = "meshopt_buildMeshlets" )]
	public static partial nuint BuildMeshlets( Meshlet* meshlets, uint* meshletVertices, byte* meshletTriangles, uint* indices, nuint indexCount,
		float* vertexPositions, nuint vertexCount, nuint vertexPositionsStride, nuint maxVertices, nuint maxTriangles, float coneWeight );
}
