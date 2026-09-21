using System.Runtime.InteropServices;

namespace NativeEngine;

internal enum MaterialSystem2AppSystemDictFlags
{
	None = 0,
	IsConsoleApp = 1 << 0,
	IsGameApp = 1 << 1,
	IsDedicatedServer = 1 << 2,
	IsStandaloneGame = 1 << 3
};

[StructLayout( LayoutKind.Sequential )]
internal struct MaterialSystem2AppSystemDictCreateInfo
{
	public MaterialSystem2AppSystemDictFlags iFlags;
	public uint nSteamAppId;

	public IntPtr hInstance;
	public IntPtr hPrevInstance;
	public int nCmdShow;
};
