namespace Sandbox.Launcher.SboxAndroid;

/// <summary>
/// Environnement minimal Android : pas d'AssemblyResolve ni de PATH mangé.
/// </summary>
public static class LauncherEnvironment
{
    public static string NativeLibraryName => "libengine2.so";
    public static string Abi => "arm64-v8a";
}



