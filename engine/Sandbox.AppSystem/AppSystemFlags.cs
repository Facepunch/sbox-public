using NativeEngine;

namespace Sandbox;

public enum AppSystemFlags
{
	None = 0,
	IsConsoleApp = 1 << 0,
	IsGameApp = 1 << 1,
	IsDedicatedServer = 1 << 2,
	IsStandaloneGame = 1 << 3,
	IsEditor = 1 << 4,
	IsUnitTest = 1 << 5,
}

public struct AppSystemCreateInfo
{
	public AppSystemFlags Flags;
	public string WindowTitle;

	internal bool WantsGameWindow => Flags.HasFlag( AppSystemFlags.IsGameApp )
		&& (Flags & (AppSystemFlags.IsEditor | AppSystemFlags.IsConsoleApp | AppSystemFlags.IsDedicatedServer | AppSystemFlags.IsUnitTest)) == 0;

	internal MaterialSystem2AppSystemDictCreateInfo ToMaterialSystem2AppSystemDictCreateInfo()
	{
		var ci = new MaterialSystem2AppSystemDictCreateInfo
		{
			iFlags = (MaterialSystem2AppSystemDictFlags)Flags,
		};

		return ci;
	}
}
