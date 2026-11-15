using System.IO;

namespace Borealis.Backend;

/// <summary>
/// Where the engine keeps its own binaries on this platform: the native folder under <c>game/bin</c> (Slang, vfx_vulkan,
/// spirv_optimizer) and the managed tools beside the application.
/// </summary>
internal static class EngineFolders
{
	/// <summary>The native folder's name under <c>game/bin</c>: <c>win64</c>, <c>linuxsteamrt64</c> or <c>osxarm64</c>.</summary>
	public static string Native { get; } = OperatingSystem.IsWindows() ? "win64" : OperatingSystem.IsMacOS() ? "osxarm64" : "linuxsteamrt64";

	/// <summary><paramref name="game"/>'s native folder, <c>game/bin/win64</c> on Windows.</summary>
	public static string NativeIn( string game ) => Path.Combine( game, "bin", Native );

	/// <summary>A native library's file name: <c>vfx_vulkan.dll</c> on Windows, <c>libvfx_vulkan.so</c> on Linux.</summary>
	public static string Library( string name ) => OperatingSystem.IsWindows() ? $"{name}.dll" : OperatingSystem.IsMacOS() ? $"lib{name}.dylib" : $"lib{name}.so";

	/// <summary>One of the engine's executables: <c>ShaderCompiler.exe</c> on Windows, <c>ShaderCompiler</c> elsewhere.</summary>
	public static string Executable( string name ) => OperatingSystem.IsWindows() ? $"{name}.exe" : name;
}
