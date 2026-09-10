namespace Facepunch.Native;

/// <summary>
/// x86_64 Linux (the steam runtime) with gcc, generating one GNU makefile.
/// </summary>
public sealed class Linux64 : Posix
{
	public override string Name => "linux64";

	// The shipped layout, the prebuilt libraries and the schema compiler all spell it the long way.
	public override string DirectoryName => "linuxsteamrt64";
	public override bool IsLinux => true;

	public override string SharedExtension => "so";

	protected override string[] OsDefines =>
		["LINUX=1", "_LINUX=1", $"{DirectoryName.ToUpperInvariant()}=1", $"_{DirectoryName.ToUpperInvariant()}=1"];

	// avx is the instruction set baseline on x86_64, and what mathlib's CanCompileSSE3/SSE4/AVX resolve
	// against. cx16 is not implied by it, and threadinterlocks.h needs cmpxchg16b for its 128 bit
	// interlocks: without it the compiler calls out to libatomic instead.
	protected override string[] MachineOptions =>
		["-m64", "-fPIC", "-fvisibility=hidden", "-mavx", "-mcx16",
			"-gdwarf-2", "-g2", "-fdiagnostics-show-option"];

	protected override string[] SharedLinkOptions( Module module ) => ["-shared", "-Wl,--no-undefined"];

	// atomic: the 128 bit interlocks in threadinterlocks.h have no inline instruction.
	protected override string[] SystemLibs => ["dl", "pthread", "m", "rt", "atomic", "uuid", "SDL3"];
}
