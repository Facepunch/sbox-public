using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Common;

namespace Sandbox.Engine.Emulation.Rendering;

/// <summary>
/// Emulation des fonctions VertexLayout_* (indices 2654-2658) pour exposer stride/attributs aux RenderTools.
/// </summary>
internal static unsafe class VertexLayoutInterop
{
    internal class LayoutData
    {
        public int Size;
        public readonly List<(string Semantic, int SemanticIndex, uint Format, int Offset)> Attributes = new();
    }

    private static readonly Dictionary<IntPtr, LayoutData> _layouts = new();

    public static void Init(void** native)
    {
        native[2654] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr>)&VertexLayout_Create;
        native[2655] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&VertexLayout_Destroy;
        native[2656] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&VertexLayout_Free;
        native[2657] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, uint, int, IntPtr>)&VertexLayout_Add;
        native[2658] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&VertexLayout_Build;
    }

    public static bool TryGetLayout(IntPtr handle, out LayoutData? data)
    {
        lock (_layouts)
        {
            return _layouts.TryGetValue(handle, out data);
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr VertexLayout_Create(IntPtr name, int size)
    {
        var data = new LayoutData { Size = size };
        int handle = HandleManager.Register(data);
        lock (_layouts)
        {
            _layouts[(IntPtr)handle] = data;
        }
        return (IntPtr)handle;
    }

    private static void DestroyLayout(IntPtr self)
    {
        lock (_layouts)
        {
            _layouts.Remove(self);
        }
        HandleManager.Unregister((int)self);
    }

    [UnmanagedCallersOnly]
    private static IntPtr VertexLayout_Destroy(IntPtr self)
    {
        DestroyLayout(self);
        return IntPtr.Zero;
    }

    [UnmanagedCallersOnly]
    private static IntPtr VertexLayout_Free(IntPtr self)
    {
        DestroyLayout(self);
        return IntPtr.Zero;
    }

    [UnmanagedCallersOnly]
    private static IntPtr VertexLayout_Add(IntPtr self, IntPtr semanticName, int semanticIndex, uint format, int offset)
    {
        lock (_layouts)
        {
            if (_layouts.TryGetValue(self, out var data))
            {
                string semantic = Marshal.PtrToStringUTF8(semanticName) ?? "";
                data.Attributes.Add((semantic, semanticIndex, format, offset));
            }
        }
        return self;
    }

    [UnmanagedCallersOnly]
    private static IntPtr VertexLayout_Build(IntPtr self)
    {
        // Rien de particulier à faire côté émulation pour le build.
        return self;
    }
}

