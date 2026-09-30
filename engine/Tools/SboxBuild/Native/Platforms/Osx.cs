namespace Facepunch.Native;

/// <summary>
/// macOS with Apple clang, generating one GNU makefile the same way Linux does. Shared libraries
/// are dylibs found through @rpath and debug info stays in the binary: ld64 has no objcopy to
/// split it out with.
/// </summary>
public sealed class Osx : Posix
{
	public Osx() => IsArm64 = true;

	public override string Name => "osxarm64";
	public override bool IsOsx => true;

	public override string Compiler => "clang++";
	public override string CCompiler => "clang";

	public override string SharedExtension => "dylib";
	public override bool Permissive => false;
	public override bool GnuLd => false;
	public override bool SplitDebugSymbols => false;
	public override string RpathOrigin => "@loader_path";

	protected override string[] OsDefines =>
		["OSX=1", "_OSX=1", "OSXARM64=1", "_OSXARM64=1"];

	protected override string[] MachineOptions =>
		["-arch", "arm64", "-fPIC", "-fvisibility=hidden", "-g2", "-fdiagnostics-show-option"];

	// The install name is @rpath relative, so whatever loads the dylib finds it through its own rpath.
	protected override string[] SharedLinkOptions( Module module ) =>
		["-dynamiclib", $"-Wl,-install_name,@rpath/{OutputFile( module )}"];

	protected override string[] SystemLibs => ["dl", "pthread", "m", "SDL3"];

	// macOS keeps malloc.h under malloc/; the shim keeps plain #include <malloc.h> working.
	protected override string[] CompatIncludes => ["common/applecompat"];

	// CoreServices for the FSEvents directory watcher, SystemConfiguration for libcurl's proxy
	// discovery, Security for its Secure Transport TLS backend.
	protected override string[] Frameworks =>
		["CoreFoundation", "CoreServices", "SystemConfiguration", "Security"];
}
