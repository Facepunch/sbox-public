using System.Runtime.InteropServices;

namespace Sbox.Engine.Emulation;

public static unsafe class EngineExports
{
    private static bool _isReady = false;

    /// <summary>
    /// Main engine initialization function called by the managed runtime.
    /// This is the entry point that receives function pointers from both sides.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "igen_engine")]
    public static void IGenEngine(int hash, void** managedFunctions, void** nativeFunctions, int* structSizes)
    {
        Console.WriteLine($"[NativeAOT Engine] igen_engine called with hash: {hash}");

        // 1. Store managed function pointers (Imports)
        EngineGlue.StoreImports(managedFunctions);

        // 2. Fill native function pointers (Exports)
        EngineGlue.FillNativeFunctions(managedFunctions, nativeFunctions, structSizes);
        
        _isReady = true;
    }

    [UnmanagedCallersOnly(EntryPoint = "Debug_Error")]
    public static void DebugError(IntPtr message)
    {
        string? msg = Marshal.PtrToStringUTF8(message);
        Console.WriteLine($"[NativeAOT ERROR] {msg}");
    }
}
