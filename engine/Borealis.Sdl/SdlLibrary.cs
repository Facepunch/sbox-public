using Borealis.Sdl.Native;

namespace Borealis.Sdl;

/// <summary>
/// Where SDL3 is loaded from. By default: beside the application or this assembly, then the engine's native folder
/// beside those (<c>game/bin/win64</c>, <c>game/bin/linuxsteamrt64</c>), then the system's search path
/// (<c>SDL3.dll</c> on Windows, <c>libSDL3.so.0</c> on Linux).
/// </summary>
public static class SdlLibrary
{
	/// <summary>A full path to SDL3 to try first. Set it before anything else in Borealis.Sdl is used.</summary>
	public static string Path { get; set; }

	/// <summary>Whether SDL3 could be loaded, without initialising anything.</summary>
	public static bool IsAvailable => SdlNative.TryLoad();

	/// <summary>Where SDL3 was loaded from, once it has been.</summary>
	public static string LoadedFrom => SdlNative.LoadedFrom;

	/// <summary>The library's file names this platform looks for.</summary>
	public static IReadOnlyList<string> FileNames => SdlNative.FileNames;
}

/// <summary>
/// An SDL call failed, or SDL3 couldn't be loaded.
/// </summary>
public sealed class SdlException : Exception
{
	/// <summary>An SDL failure.</summary>
	public SdlException( string message ) : base( message ) { }
}
