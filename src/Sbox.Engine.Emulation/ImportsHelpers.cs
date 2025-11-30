using System;
using System.Runtime.InteropServices;

namespace Sbox.Engine.Emulation.Generated;

public static unsafe partial class Imports
{
    public static IntPtr GetManagedHandle(void* ptr)
    {
        return (IntPtr)ptr;
    }
}
