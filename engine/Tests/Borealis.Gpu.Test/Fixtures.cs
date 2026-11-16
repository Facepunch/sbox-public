using Borealis.Source2;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// Where tests keep what they compile. Slang surfaces take seconds each, so tests share the backend's disk cache
/// (<c>game/.source2/borealissurfaces</c>), and source files a test writes go to a fixed folder of its own under
/// <c>game/.source2</c> rather than a new temporary one, so their compiles are found again on the next run (the test may
/// still delete them: the next run writes the same text). An entry is used only while the compiler, the source and
/// everything it read are unchanged, so a test reads what a compile now makes.
/// </summary>
static class Fixtures
{
	static string Game => ContentFiles.FindGame().Roots.FirstOrDefault() is { } core ? Path.GetDirectoryName( core ) : null;

	/// <summary>The backend's surface cache (<c>SurfaceShaders.CacheFolder</c>'s default).</summary>
	public static string SurfaceCache => Path.Combine( Game, ".source2", "borealissurfaces" );

	/// <summary>A test's own folder for the files it writes, at the same path every run.</summary>
	public static string Folder( string name )
	{
		var folder = Path.Combine( Game, ".source2", "borealis-gpu-tests", name );
		Directory.CreateDirectory( folder );
		return folder;
	}
}
