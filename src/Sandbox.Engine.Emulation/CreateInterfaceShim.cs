using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Engine.Emulation;

/// <summary>
/// Minimal stub to expose a CreateInterface export so NativeLibrary.GetExport succeeds.
/// This does not provide real interfaces yet; it simply logs and returns null.
/// </summary>
public static unsafe class CreateInterfaceShim
{
    [UnmanagedCallersOnly(EntryPoint = "CreateInterface", CallConvs = new[] { typeof(CallConvCdecl) })]
    public static IntPtr CreateInterface(byte* namePtr, IntPtr returnCode)
    {
        try
        {
            var name = namePtr != null ? Marshal.PtrToStringUTF8((IntPtr)namePtr) ?? string.Empty : string.Empty;
            Console.WriteLine($"[NativeAOT] CreateInterface stub called for '{name}'");

            // Signal failure to the caller (consistent with returning null)
            if (returnCode != IntPtr.Zero)
            {
                Marshal.WriteInt32(returnCode, 1);
            }

            return IntPtr.Zero;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] CreateInterface stub error: {ex}");
            return IntPtr.Zero;
        }
    }
}

