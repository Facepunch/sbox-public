using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Vfx;

namespace Sandbox.Engine.Emulation;

/// <summary>
/// Exposes a CreateInterface export and routes known interfaces to emulated factories.
/// </summary>
public static unsafe class CreateInterfaceShim
{
    [UnmanagedCallersOnly(EntryPoint = "CreateInterface")]
    public static IntPtr CreateInterface(byte* namePtr, IntPtr returnCode)
    {
        var name = namePtr != null ? Marshal.PtrToStringUTF8((IntPtr)namePtr) ?? string.Empty : string.Empty;
        try
        {
            IntPtr result = IntPtr.Zero;

            switch (name)
            {
                case "VFX_DLL_001":
                    Console.WriteLine("Hello");
                    result = VfxModule.GetVfxInterface();
                    break;
                case "filesystem_stdio":
                    // Filesystem interface is not used directly here; return a non-null placeholder.
                    result = VfxModule.GetFilesystemStub();
                    break;
                default:
                    Console.WriteLine($"[NativeAOT] CreateInterface unknown interface '{name}'");
                    break;
            }

            if (returnCode != IntPtr.Zero)
            {
                Marshal.WriteInt32(returnCode, result != IntPtr.Zero ? 0 : 1);
            }

            Console.WriteLine($"[NativeAOT] CreateInterface handled '{name}' -> 0x{result.ToInt64():X}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] CreateInterface error for '{name}': {ex}");
            if (returnCode != IntPtr.Zero)
            {
                Marshal.WriteInt32(returnCode, 1);
            }
            return IntPtr.Zero;
        }
    }
}

