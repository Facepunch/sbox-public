using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sandbox;
using NativeEngine;
using Sandbox.Engine.Emulation.Common;

namespace Sandbox.Engine.Emulation.RenderAttributes;

/// <summary>
/// Wrapper public pour CSamplerStateDesc (qui est internal dans NativeEngine).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 20)]
public struct RenderAttributesSamplerStateDesc
{
    public byte m_nFilterMode;
    public byte m_nMipLodBias;
    public byte m_nMipLodBiasSign;
    public byte m_nAddressU;
    public byte m_nAddressV;
    public byte m_nAddressW;
    public byte m_nAnisoExp;
    public byte m_nComparisonFunc;
    public byte m_nAllowGlobalMipBiasOverride;
    public byte m_nMinLod;
    public byte m_nMaxLod;
    public uint m_nBorderColor8Bit;
    public ushort m_nPad;
}

/// <summary>
/// Module d'émulation pour CRenderAttributes.
/// Gère les attributs de rendu (paramètres de shader, textures, etc.).
/// </summary>
public static unsafe class RenderAttributes
{
    /// <summary>
    /// Initialise les fonctions natives de CRenderAttributes.
    /// </summary>
    public static void Init(void** native)
    {
        Console.WriteLine("[NativeAOT] RenderAttributes.Init: patching CRenderAttributes function pointers (indices 689-727)");
        // Fonctions principales (indices 689-727)
        native[689] = (void*)(delegate* unmanaged<IntPtr, void>)&CRndrttrbts_DeleteThis;
        native[690] = (void*)(delegate* unmanaged<IntPtr>)&CRndrttrbts_Create;
        native[691] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, float, void>)&CRndrttrbts_SetFloatValue;
        native[692] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, float, float>)&CRndrttrbts_GetFloatValue;
        native[693] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, void>)&CRndrttrbts_DeleteFloatValue;
        native[694] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, Vector2*, void>)&CRndrttrbts_SetVector2DValue;
        native[695] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, Vector2*, Vector2>)&CRndrttrbts_GetVector2DValue;
        native[696] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, void>)&CRndrttrbts_DeleteVector2DValue;
        native[697] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, Vector3*, void>)&CRndrttrbts_SetVectorValue;
        native[698] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, Vector3*, Vector3>)&CRndrttrbts_GetVectorValue;
        native[699] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, void>)&CRndrttrbts_DeleteVectorValue;
        native[700] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, Vector4*, void>)&CRndrttrbts_SetVector4DValue;
        native[701] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, Vector4*, Vector4>)&CRndrttrbts_GetVector4DValue;
        native[702] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, void>)&CRndrttrbts_DeleteVector4DValue;
        native[703] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, Matrix*, void>)&CRndrttrbts_SetVMatrixValue;
        native[704] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, Matrix*, Matrix>)&CRndrttrbts_GetVMatrixValue;
        native[705] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, void>)&CRndrttrbts_DeleteVMatrixValue;
        native[706] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, IntPtr, void>)&CRndrttrbts_SetStringValue;
        native[707] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, void>)&CRndrttrbts_DeleteStringValue;
        native[708] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, int, void>)&CRndrttrbts_SetIntValue;
        native[709] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, int, int>)&CRndrttrbts_GetIntValue;
        native[710] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, void>)&CRndrttrbts_DeleteIntValue;
        native[711] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, byte, void>)&CRndrttrbts_SetComboValue;
        native[712] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, byte, byte>)&CRndrttrbts_GetComboValue;
        native[713] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, void>)&CRndrttrbts_DeleteComboValue;
        native[714] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, int, void>)&CRndrttrbts_SetBoolValue;
        native[715] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, int, int>)&CRndrttrbts_GetBoolValue;
        native[716] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, void>)&CRndrttrbts_DeleteBoolValue;
        native[717] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, IntPtr, int, void>)&CRndrttrbts_SetTextureValue;
        native[718] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, IntPtr, IntPtr>)&CRndrttrbts_GetTextureValue;
        native[719] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, void>)&CRndrttrbts_DeleteTextureValue;
        native[720] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, RenderAttributesSamplerStateDesc*, void>)&CRndrttrbts_SetSamplerValue;
        native[721] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, IntPtr, void>)&CRndrttrbts_SetBufferValue;
        native[722] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, IntPtr, void>)&CRndrttrbts_SetPtrValue;
        native[723] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, void>)&CRndrttrbts_DeletePtrValue;
        native[724] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, int, int, int, int, void>)&CRndrttrbts_SetIntVector4DValue;
        native[725] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CRndrttrbts_MergeToPtr;
        native[726] = (void*)(delegate* unmanaged<IntPtr, int>)&CRndrttrbts_IsEmpty;
        native[727] = (void*)(delegate* unmanaged<IntPtr, int, int, void>)&CRndrttrbts_Clear;

        Console.WriteLine($"[NativeAOT] RenderAttributes.Init: native[690]=0x{(nuint)native[690]:X}");
    }
    
    /// <summary>
    /// Représentation émulée d'un CRenderAttributes.
    /// Stocke tous les attributs de rendu (paramètres de shader, textures, etc.).
    /// </summary>
    private class EmulatedRenderAttributes
    {
        public Dictionary<Sandbox.StringToken, float> FloatValues { get; } = new();
        public Dictionary<Sandbox.StringToken, Vector2> Vector2DValues { get; } = new();
        public Dictionary<Sandbox.StringToken, Vector3> VectorValues { get; } = new();
        public Dictionary<Sandbox.StringToken, Vector4> Vector4DValues { get; } = new();
        public Dictionary<Sandbox.StringToken, Matrix> MatrixValues { get; } = new();
        public Dictionary<Sandbox.StringToken, string> StringValues { get; } = new();
        public Dictionary<Sandbox.StringToken, int> IntValues { get; } = new();
        public Dictionary<Sandbox.StringToken, byte> ComboValues { get; } = new();
        public Dictionary<Sandbox.StringToken, bool> BoolValues { get; } = new();
        public Dictionary<Sandbox.StringToken, ITexture> TextureValues { get; } = new();
        public Dictionary<Sandbox.StringToken, RenderBufferHandle_t> BufferValues { get; } = new();
        public Dictionary<Sandbox.StringToken, IntPtr> PtrValues { get; } = new();
        public Dictionary<Sandbox.StringToken, RenderAttributesSamplerStateDesc> SamplerValues { get; } = new();
        
        public bool IsEmpty()
        {
            return FloatValues.Count == 0 && Vector2DValues.Count == 0 && VectorValues.Count == 0 &&
                   Vector4DValues.Count == 0 && MatrixValues.Count == 0 && StringValues.Count == 0 &&
                   IntValues.Count == 0 && ComboValues.Count == 0 && BoolValues.Count == 0 &&
                   TextureValues.Count == 0 && BufferValues.Count == 0 && PtrValues.Count == 0 &&
                   SamplerValues.Count == 0;
        }
        
        public void Clear(bool freeMemory)
        {
            if (freeMemory)
            {
                FloatValues.Clear();
                Vector2DValues.Clear();
                VectorValues.Clear();
                Vector4DValues.Clear();
                MatrixValues.Clear();
                StringValues.Clear();
                IntValues.Clear();
                ComboValues.Clear();
                BoolValues.Clear();
                TextureValues.Clear();
                BufferValues.Clear();
                PtrValues.Clear();
                SamplerValues.Clear();
            }
        }
    }
    
    /// <summary>
    /// Helper function to create render attributes (can be called from managed code).
    /// </summary>
    private static long _createCount = 0;
    public static IntPtr CreateRenderAttributesInternal()
    {
        var attributes = new EmulatedRenderAttributes();
        int handle = HandleManager.Register(attributes);
        if (handle == 0)
        {
            Console.WriteLine("[NativeAOT] CRndrttrbts_Create: failed to register handle (0)");
            return IntPtr.Zero;
        }

        long n = System.Threading.Interlocked.Increment(ref _createCount);

        // Throttle logging: only log the first few and then every 10,000 creations with a stack trace to locate the caller.
        // if (n <= 5 || (n % 10000) == 0)
        // {
        //     string? trace = null;
        //     try { trace = Environment.StackTrace; } catch { /* ignore */ }
            Console.WriteLine($"[NativeAOT] CRndrttrbts_Create: handle={handle}, count={n}");
        //     if (!string.IsNullOrEmpty(trace))
        //     {
        //         Console.WriteLine("[NativeAOT] CRndrttrbts_Create stack:\n" + trace);
        //     }
        // }

        return (IntPtr)handle;
    }
    
    /// <summary>
    /// Helper function to set a string value in render attributes (can be called from managed code).
    /// </summary>
    public static void SetStringValueHelper(IntPtr self, string name, string value)
    {
        if (self == IntPtr.Zero) return;
        
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        
        var token = new Sandbox.StringToken(name);
        attrs.StringValues[token] = value;
    }
    
    /// <summary>
    /// Helper function to get a string value from render attributes (can be called from managed code).
    /// </summary>
    public static string? GetStringValueHelper(IntPtr self, string name)
    {
        if (self == IntPtr.Zero) return null;
        
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return null;
        
        var token = new Sandbox.StringToken(name);
        return attrs.StringValues.TryGetValue(token, out var value) ? value : null;
    }
    
    /// <summary>
    /// Helper function to get a float value from render attributes (can be called from managed code).
    /// </summary>
    public static float GetFloatValueHelper(IntPtr self, string name, float defaultValue)
    {
        if (self == IntPtr.Zero) return defaultValue;
        
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return defaultValue;
        
        var token = new Sandbox.StringToken(name);
        return attrs.FloatValues.TryGetValue(token, out var value) ? value : defaultValue;
    }
    
    /// <summary>
    /// Helper function to get an int value from render attributes (can be called from managed code).
    /// </summary>
    public static int GetIntValueHelper(IntPtr self, string name, int defaultValue)
    {
        if (self == IntPtr.Zero) return defaultValue;
        
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return defaultValue;
        
        var token = new Sandbox.StringToken(name);
        return attrs.IntValues.TryGetValue(token, out var value) ? value : defaultValue;
    }
    
    /// <summary>
    /// Helper function to get a bool value from render attributes (can be called from managed code).
    /// </summary>
    public static bool GetBoolValueHelper(IntPtr self, string name, bool defaultValue)
    {
        if (self == IntPtr.Zero) return defaultValue;
        
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return defaultValue;
        
        var token = new Sandbox.StringToken(name);
        return attrs.BoolValues.TryGetValue(token, out var value) ? value : defaultValue;
    }
    
    /// <summary>
    /// Helper function to get a Vector4 value from render attributes (can be called from managed code).
    /// </summary>
    public static Vector4 GetVector4DValueHelper(IntPtr self, string name, Vector4 defaultValue)
    {
        if (self == IntPtr.Zero) return defaultValue;
        
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return defaultValue;
        
        var token = new Sandbox.StringToken(name);
        return attrs.Vector4DValues.TryGetValue(token, out var value) ? value : defaultValue;
    }
    
    /// <summary>
    /// Helper function to set a Vector4 value in render attributes (can be called from managed code).
    /// </summary>
    public static void SetVector4DValueHelper(IntPtr self, string name, Vector4 value)
    {
        if (self == IntPtr.Zero) return;
        
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        
        var token = new Sandbox.StringToken(name);
        attrs.Vector4DValues[token] = value;
    }
    
    /// <summary>
    /// Helper function to get a texture value from render attributes (can be called from managed code).
    /// </summary>
    public static IntPtr GetTextureValueHelper(IntPtr self, string name, IntPtr defaultValue)
    {
        if (self == IntPtr.Zero) return defaultValue;
        
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return defaultValue;
        
        var token = new Sandbox.StringToken(name);
        if (attrs.TextureValues.TryGetValue(token, out var texture))
        {
            return texture.self;
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Helper function to set a texture value in render attributes (can be called from managed code).
    /// </summary>
    public static void SetTextureValueHelper(IntPtr self, string name, IntPtr texture)
    {
        if (self == IntPtr.Zero) return;
        
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        
        var token = new Sandbox.StringToken(name);
        if (texture != IntPtr.Zero)
        {
            var textureWrapper = new ITexture { self = texture };
            attrs.TextureValues[token] = textureWrapper;
        }
        else
        {
            attrs.TextureValues.Remove(token);
        }
    }
    
    /// <summary>
    /// Helper function to check if a parameter exists in render attributes (can be called from managed code).
    /// </summary>
    public static bool HasParamHelper(IntPtr self, string name)
    {
        if (self == IntPtr.Zero) return false;
        
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return false;
        
        var token = new Sandbox.StringToken(name);
        return attrs.FloatValues.ContainsKey(token) ||
               attrs.Vector2DValues.ContainsKey(token) ||
               attrs.VectorValues.ContainsKey(token) ||
               attrs.Vector4DValues.ContainsKey(token) ||
               attrs.MatrixValues.ContainsKey(token) ||
               attrs.StringValues.ContainsKey(token) ||
               attrs.IntValues.ContainsKey(token) ||
               attrs.ComboValues.ContainsKey(token) ||
               attrs.BoolValues.ContainsKey(token) ||
               attrs.TextureValues.ContainsKey(token) ||
               attrs.BufferValues.ContainsKey(token) ||
               attrs.PtrValues.ContainsKey(token) ||
               attrs.SamplerValues.ContainsKey(token);
    }
    
    /// <summary>
    /// Helper function to get the first texture attribute from render attributes (can be called from managed code).
    /// </summary>
    public static IntPtr GetFirstTextureAttributeHelper(IntPtr self)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return IntPtr.Zero;
        
        // Retourner la première texture trouvée
        foreach (var texture in attrs.TextureValues.Values)
        {
            return texture.self;
        }
        
        return IntPtr.Zero;
    }

    /// <summary>
    /// Helper function to set a pointer value (managed helper, avoids UnmanagedCallersOnly call).
    /// </summary>
    public static void SetPtrValueHelper(IntPtr self, Sandbox.StringToken token, IntPtr ptr)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.PtrValues[token] = ptr;
    }
    
    /// <summary>
    /// Helper to get EmulatedRenderAttributes from IntPtr.
    /// </summary>
    private static EmulatedRenderAttributes? GetRenderAttributes(IntPtr self)
    {
        if (self == IntPtr.Zero) return null;
        int handle = (int)self;
        var result = HandleManager.Get<EmulatedRenderAttributes>(handle);
        if (result == null)
        {
            Console.WriteLine($"[NativeAOT] RenderAttributes: handle {handle} not found");
        }
        return result;
    }
    
    // ========== Fonctions principales ==========
    
    /// <summary>
    /// Supprime un CRenderAttributes.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeleteThis(IntPtr self)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var attributes = HandleManager.Get<EmulatedRenderAttributes>(handle);
        if (attributes != null)
        {
            HandleManager.Unregister(handle);
            Console.WriteLine($"[NativeAOT] CRndrttrbts_DeleteThis: deleted handle={handle}");
        }
    }
    
    /// <summary>
    /// Crée un nouveau CRenderAttributes.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CRndrttrbts_Create()
    {
        return CreateRenderAttributesInternal();
    }
    
    // ========== Float ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetFloatValue(IntPtr self, Sandbox.StringToken nTokenID, float flValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.FloatValues[nTokenID] = flValue;
    }
    
    [UnmanagedCallersOnly]
    public static float CRndrttrbts_GetFloatValue(IntPtr self, Sandbox.StringToken nTokenID, float flDefaultValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return flDefaultValue;
        return attrs.FloatValues.TryGetValue(nTokenID, out var value) ? value : flDefaultValue;
    }
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeleteFloatValue(IntPtr self, Sandbox.StringToken nTokenID)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.FloatValues.Remove(nTokenID);
    }
    
    // ========== Vector2D ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetVector2DValue(IntPtr self, Sandbox.StringToken nTokenID, Vector2* vValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null || vValue == null) return;
        attrs.Vector2DValues[nTokenID] = *vValue;
    }
    
    [UnmanagedCallersOnly]
    public static Vector2 CRndrttrbts_GetVector2DValue(IntPtr self, Sandbox.StringToken nTokenID, Vector2* vDefaultValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null)
        {
            return vDefaultValue != null ? *vDefaultValue : Vector2.Zero;
        }
        return attrs.Vector2DValues.TryGetValue(nTokenID, out var value) ? value : (vDefaultValue != null ? *vDefaultValue : Vector2.Zero);
    }
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeleteVector2DValue(IntPtr self, Sandbox.StringToken nTokenID)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.Vector2DValues.Remove(nTokenID);
    }
    
    // ========== Vector3 ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetVectorValue(IntPtr self, Sandbox.StringToken nTokenID, Vector3* vValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null || vValue == null) return;
        attrs.VectorValues[nTokenID] = *vValue;
    }
    
    [UnmanagedCallersOnly]
    public static Vector3 CRndrttrbts_GetVectorValue(IntPtr self, Sandbox.StringToken nTokenID, Vector3* vDefaultValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null)
        {
            return vDefaultValue != null ? *vDefaultValue : Vector3.Zero;
        }
        return attrs.VectorValues.TryGetValue(nTokenID, out var value) ? value : (vDefaultValue != null ? *vDefaultValue : Vector3.Zero);
    }
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeleteVectorValue(IntPtr self, Sandbox.StringToken nTokenID)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.VectorValues.Remove(nTokenID);
    }
    
    // ========== Vector4D ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetVector4DValue(IntPtr self, Sandbox.StringToken nTokenID, Vector4* vValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null || vValue == null) return;
        attrs.Vector4DValues[nTokenID] = *vValue;
    }
    
    [UnmanagedCallersOnly]
    public static Vector4 CRndrttrbts_GetVector4DValue(IntPtr self, Sandbox.StringToken nTokenID, Vector4* vDefaultValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null)
        {
            return vDefaultValue != null ? *vDefaultValue : Vector4.Zero;
        }
        return attrs.Vector4DValues.TryGetValue(nTokenID, out var value) ? value : (vDefaultValue != null ? *vDefaultValue : Vector4.Zero);
    }
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeleteVector4DValue(IntPtr self, Sandbox.StringToken nTokenID)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.Vector4DValues.Remove(nTokenID);
    }
    
    // ========== Matrix ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetVMatrixValue(IntPtr self, Sandbox.StringToken nTokenID, Matrix* value)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null || value == null) return;
        attrs.MatrixValues[nTokenID] = *value;
    }
    
    [UnmanagedCallersOnly]
    public static Matrix CRndrttrbts_GetVMatrixValue(IntPtr self, Sandbox.StringToken nTokenID, Matrix* vDefaultValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null)
        {
            return vDefaultValue != null ? *vDefaultValue : Matrix.Identity;
        }
        return attrs.MatrixValues.TryGetValue(nTokenID, out var value) ? value : (vDefaultValue != null ? *vDefaultValue : Matrix.Identity);
    }
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeleteVMatrixValue(IntPtr self, Sandbox.StringToken nTokenID)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.MatrixValues.Remove(nTokenID);
    }
    
    // ========== String ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetStringValue(IntPtr self, Sandbox.StringToken nTokenID, IntPtr str)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        
        string? value = null;
        if (str != IntPtr.Zero)
        {
            value = Marshal.PtrToStringUTF8(str);
        }
        attrs.StringValues[nTokenID] = value ?? "";
    }
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeleteStringValue(IntPtr self, Sandbox.StringToken nTokenID)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.StringValues.Remove(nTokenID);
    }
    
    // ========== Int ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetIntValue(IntPtr self, Sandbox.StringToken nTokenID, int nValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.IntValues[nTokenID] = nValue;
    }
    
    [UnmanagedCallersOnly]
    public static int CRndrttrbts_GetIntValue(IntPtr self, Sandbox.StringToken nTokenID, int nDefaultValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return nDefaultValue;
        return attrs.IntValues.TryGetValue(nTokenID, out var value) ? value : nDefaultValue;
    }
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeleteIntValue(IntPtr self, Sandbox.StringToken nTokenID)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.IntValues.Remove(nTokenID);
    }
    
    // ========== Combo ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetComboValue(IntPtr self, Sandbox.StringToken nTokenID, byte nValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.ComboValues[nTokenID] = nValue;
    }
    
    [UnmanagedCallersOnly]
    public static byte CRndrttrbts_GetComboValue(IntPtr self, Sandbox.StringToken nTokenID, byte nValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return nValue;
        return attrs.ComboValues.TryGetValue(nTokenID, out var value) ? value : nValue;
    }
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeleteComboValue(IntPtr self, Sandbox.StringToken nTokenID)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.ComboValues.Remove(nTokenID);
    }
    
    // ========== Bool ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetBoolValue(IntPtr self, Sandbox.StringToken nTokenID, int bValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.BoolValues[nTokenID] = bValue != 0;
    }
    
    [UnmanagedCallersOnly]
    public static int CRndrttrbts_GetBoolValue(IntPtr self, Sandbox.StringToken nTokenID, int bValue)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return bValue;
        return attrs.BoolValues.TryGetValue(nTokenID, out var value) ? (value ? 1 : 0) : bValue;
    }
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeleteBoolValue(IntPtr self, Sandbox.StringToken nTokenID)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.BoolValues.Remove(nTokenID);
    }
    
    // ========== Texture ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetTextureValue(IntPtr self, Sandbox.StringToken nTokenID, IntPtr txtr, int nSingleMipLevelToBind)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        
        if (txtr != IntPtr.Zero)
        {
            var texture = new ITexture { self = txtr };
            attrs.TextureValues[nTokenID] = texture;
        }
        else
        {
            attrs.TextureValues.Remove(nTokenID);
        }
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr CRndrttrbts_GetTextureValue(IntPtr self, Sandbox.StringToken nTokenID, IntPtr defaultTxtr)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return defaultTxtr;
        
        if (attrs.TextureValues.TryGetValue(nTokenID, out var texture))
        {
            return texture.self;
        }
        return defaultTxtr;
    }
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeleteTextureValue(IntPtr self, Sandbox.StringToken nTokenID)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.TextureValues.Remove(nTokenID);
    }
    
    // ========== Sampler ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetSamplerValue(IntPtr self, Sandbox.StringToken nTokenID, RenderAttributesSamplerStateDesc* samplerDesc)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null || samplerDesc == null) return;
        attrs.SamplerValues[nTokenID] = *samplerDesc;
    }
    
    // ========== Buffer ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetBufferValue(IntPtr self, Sandbox.StringToken nTokenID, IntPtr hRenderBuffer)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        
        if (hRenderBuffer != IntPtr.Zero)
        {
            var buffer = new RenderBufferHandle_t { self = hRenderBuffer };
            attrs.BufferValues[nTokenID] = buffer;
        }
        else
        {
            attrs.BufferValues.Remove(nTokenID);
        }
    }
    
    // ========== Ptr ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetPtrValue(IntPtr self, Sandbox.StringToken nTokenID, IntPtr ptr)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.PtrValues[nTokenID] = ptr;
    }
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_DeletePtrValue(IntPtr self, Sandbox.StringToken nTokenID)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.PtrValues.Remove(nTokenID);
    }
    
    // ========== IntVector4D ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_SetIntVector4DValue(IntPtr self, Sandbox.StringToken nTokenID, int x, int y, int z, int w)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        // Stocker comme Vector4 (conversion implicite)
        attrs.Vector4DValues[nTokenID] = new Vector4(x, y, z, w);
    }
    
    // ========== Merge ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_MergeToPtr(IntPtr self, IntPtr attrList)
    {
        var attrs = GetRenderAttributes(self);
        var sourceAttrs = GetRenderAttributes(attrList);
        if (attrs == null || sourceAttrs == null) return;
        
        // Fusionner tous les dictionnaires
        foreach (var kvp in sourceAttrs.FloatValues)
            attrs.FloatValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.Vector2DValues)
            attrs.Vector2DValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.VectorValues)
            attrs.VectorValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.Vector4DValues)
            attrs.Vector4DValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.MatrixValues)
            attrs.MatrixValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.StringValues)
            attrs.StringValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.IntValues)
            attrs.IntValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.ComboValues)
            attrs.ComboValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.BoolValues)
            attrs.BoolValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.TextureValues)
            attrs.TextureValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.BufferValues)
            attrs.BufferValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.PtrValues)
            attrs.PtrValues[kvp.Key] = kvp.Value;
        foreach (var kvp in sourceAttrs.SamplerValues)
            attrs.SamplerValues[kvp.Key] = kvp.Value;
    }
    
    // ========== IsEmpty ==========
    
    [UnmanagedCallersOnly]
    public static int CRndrttrbts_IsEmpty(IntPtr self)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return 1; // Empty if invalid
        return attrs.IsEmpty() ? 1 : 0;
    }
    
    // ========== Clear ==========
    
    [UnmanagedCallersOnly]
    public static void CRndrttrbts_Clear(IntPtr self, int freeMemory, int resetParent)
    {
        var attrs = GetRenderAttributes(self);
        if (attrs == null) return;
        attrs.Clear(freeMemory != 0);
    }
}

