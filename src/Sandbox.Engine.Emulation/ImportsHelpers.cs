using System;
using System.Runtime.InteropServices;

namespace Sandbox.Engine.Emulation.Generated;

public static unsafe partial class Imports
{
    public static IntPtr GetManagedHandle(void* ptr)
    {
        return (IntPtr)ptr;
    }
}
