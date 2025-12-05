using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.InteropServices.Marshalling;
using Sandbox.Engine.Emulation.Common;

namespace Sandbox.Engine.Emulation.Physics;

/// <summary>
/// Module d'émulation pour CPhysSurfaceProperties (CPhysSrfcPrprts_*).
/// Gère les propriétés de surface physique (friction, élasticité, etc.).
/// </summary>
public static unsafe class PhysicSurfaceProperties
{
    // Cache pour les pointeurs de chaînes UTF-8 (pour Get__CPhysSrfcPrprts_m_name et m_description)
    private static readonly Dictionary<IntPtr, IntPtr> _stringCache = new();
    private static readonly List<IntPtr> _allocatedStrings = new();
    private static readonly object _cacheLock = new object();

    /// <summary>
    /// Initialise le module PhysicSurfaceProperties en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 15519-15535 :
    /// - CPhysSrfcPrprts_UpdatePhysics: 654
    /// - Get__CPhysSrfcPrprts_m_name: 655
    /// - Set__CPhysSrfcPrprts_m_name: 656
    /// - Get__CPhysSrfcPrprts_m_nameHash: 657
    /// - Set__CPhysSrfcPrprts_m_nameHash: 658
    /// - Get__CPhysSrfcPrprts_m_baseNameHash: 659
    /// - Set__CPhysSrfcPrprts_m_baseNameHash: 660
    /// - Get__CPhysSrfcPrprts_m_nIndex: 661
    /// - Set__CPhysSrfcPrprts_m_nIndex: 662
    /// - Get__CPhysSrfcPrprts_m_nBaseIndex: 663
    /// - Set__CPhysSrfcPrprts_m_nBaseIndex: 664
    /// - Get__CPhysSrfcPrprts_m_AudioSurface: 665
    /// - Set__CPhysSrfcPrprts_m_AudioSurface: 666
    /// - Get__CPhysSrfcPrprts_m_bHidden: 667
    /// - Set__CPhysSrfcPrprts_m_bHidden: 668
    /// - Get__CPhysSrfcPrprts_m_description: 669
    /// - Set__CPhysSrfcPrprts_m_description: 670
    /// </summary>
    public static void Init(void** native)
    {
        // CPhysSrfcPrprts_UpdatePhysics: Index 654
        native[654] = (void*)(delegate* unmanaged<IntPtr, float, float, float, float, float, void>)&CPhysSrfcPrprts_UpdatePhysics;

        // Get/Set m_name: Indices 655-656
        native[655] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, IntPtr>)&Get__CPhysSrfcPrprts_m_name;
        native[656] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, IntPtr, void>)&Set__CPhysSrfcPrprts_m_name;

        // Get/Set m_nameHash: Indices 657-658
        native[657] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get__CPhysSrfcPrprts_m_nameHash;
        native[658] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set__CPhysSrfcPrprts_m_nameHash;

        // Get/Set m_baseNameHash: Indices 659-660
        native[659] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get__CPhysSrfcPrprts_m_baseNameHash;
        native[660] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set__CPhysSrfcPrprts_m_baseNameHash;

        // Get/Set m_nIndex: Indices 661-662
        native[661] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CPhysSrfcPrprts_m_nIndex;
        native[662] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CPhysSrfcPrprts_m_nIndex;

        // Get/Set m_nBaseIndex: Indices 663-664
        native[663] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CPhysSrfcPrprts_m_nBaseIndex;
        native[664] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CPhysSrfcPrprts_m_nBaseIndex;

        // Get/Set m_AudioSurface: Indices 665-666
        native[665] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CPhysSrfcPrprts_m_AudioSurface;
        native[666] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CPhysSrfcPrprts_m_AudioSurface;

        // Get/Set m_bHidden: Indices 667-668
        native[667] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CPhysSrfcPrprts_m_bHidden;
        native[668] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CPhysSrfcPrprts_m_bHidden;

        // Get/Set m_description: Indices 669-670
        native[669] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, IntPtr>)&Get__CPhysSrfcPrprts_m_description;
        native[670] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, IntPtr, void>)&Set__CPhysSrfcPrprts_m_description;

        Console.WriteLine("[NativeAOT] PhysicSurfaceProperties module initialized");
    }

    /// <summary>
    /// Update physics properties of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15519: delegate* unmanaged< IntPtr, float, float, float, float, float, void >
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CPhysSrfcPrprts_UpdatePhysics(IntPtr self, float Friction, float Elasticity, float Density, float RollingResistance, float BounceThreshold)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return;

        property.Friction = Friction;
        property.Elasticity = Elasticity;
        property.Density = Density;
        property.RollingResistance = RollingResistance;
        property.BounceThreshold = BounceThreshold;
    }

    /// <summary>
    /// Get the name of a surface property.
    /// Returns a cached UTF-8 string pointer.
    /// Signature exacte depuis Interop.Engine.cs ligne 15520: delegate* unmanaged[SuppressGCTransition]<IntPtr, IntPtr>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IntPtr Get__CPhysSrfcPrprts_m_name(IntPtr self)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return IntPtr.Zero;

        lock (_cacheLock)
        {
            if (_stringCache.TryGetValue(self, out var cachedPtr))
                return cachedPtr;

            // Allouer une nouvelle chaîne UTF-8
            string name = property.Name ?? "";
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(name);
            int size = utf8Bytes.Length + 1; // +1 pour le null terminator

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.Copy(utf8Bytes, 0, ptr, utf8Bytes.Length);
            Marshal.WriteByte(ptr, utf8Bytes.Length, 0); // Null terminator

            _stringCache[self] = ptr;
            _allocatedStrings.Add(ptr);

            return ptr;
        }
    }

    /// <summary>
    /// Set the name of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15521: delegate* unmanaged[SuppressGCTransition]<IntPtr, IntPtr, void>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set__CPhysSrfcPrprts_m_name(IntPtr self, IntPtr namePtr)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return;

        string? name = Marshal.PtrToStringUTF8(namePtr);
        if (name != null)
        {
            property.Name = name;
            property.NameHash = HashUtils.MurmurHash2(name, lowercase: true);

            // Invalider le cache de la chaîne
            lock (_cacheLock)
            {
                if (_stringCache.TryGetValue(self, out var cachedPtr))
                {
                    _stringCache.Remove(self);
                    _allocatedStrings.Remove(cachedPtr);
                    Marshal.FreeHGlobal(cachedPtr);
                }
            }
        }
    }

    /// <summary>
    /// Get the name hash of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15522: delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static uint Get__CPhysSrfcPrprts_m_nameHash(IntPtr self)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return 0;

        return property.NameHash;
    }

    /// <summary>
    /// Set the name hash of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15523: delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set__CPhysSrfcPrprts_m_nameHash(IntPtr self, uint value)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return;

        property.NameHash = value;
    }

    /// <summary>
    /// Get the base name hash of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15524: delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static uint Get__CPhysSrfcPrprts_m_baseNameHash(IntPtr self)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return 0;

        return property.BaseNameHash;
    }

    /// <summary>
    /// Set the base name hash of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15525: delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set__CPhysSrfcPrprts_m_baseNameHash(IntPtr self, uint value)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return;

        property.BaseNameHash = value;
    }

    /// <summary>
    /// Get the index of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15526: delegate* unmanaged[SuppressGCTransition]<IntPtr, int>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Get__CPhysSrfcPrprts_m_nIndex(IntPtr self)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return 0;

        return property.Index;
    }

    /// <summary>
    /// Set the index of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15527: delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set__CPhysSrfcPrprts_m_nIndex(IntPtr self, int value)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return;

        property.Index = value;
    }

    /// <summary>
    /// Get the base index of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15528: delegate* unmanaged[SuppressGCTransition]<IntPtr, int>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Get__CPhysSrfcPrprts_m_nBaseIndex(IntPtr self)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return 0;

        return property.BaseIndex;
    }

    /// <summary>
    /// Set the base index of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15529: delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set__CPhysSrfcPrprts_m_nBaseIndex(IntPtr self, int value)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return;

        property.BaseIndex = value;
    }

    /// <summary>
    /// Get the audio surface index of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15530: delegate* unmanaged[SuppressGCTransition]<IntPtr, int>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Get__CPhysSrfcPrprts_m_AudioSurface(IntPtr self)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return 0;

        return property.AudioSurface;
    }

    /// <summary>
    /// Set the audio surface index of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15531: delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set__CPhysSrfcPrprts_m_AudioSurface(IntPtr self, int value)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return;

        property.AudioSurface = value;
    }

    /// <summary>
    /// Get the hidden flag of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15532: delegate* unmanaged[SuppressGCTransition]<IntPtr, int>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Get__CPhysSrfcPrprts_m_bHidden(IntPtr self)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return 0;

        return property.IsHidden ? 1 : 0;
    }

    /// <summary>
    /// Set the hidden flag of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15533: delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set__CPhysSrfcPrprts_m_bHidden(IntPtr self, int value)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return;

        property.IsHidden = value != 0;
    }

    /// <summary>
    /// Get the description of a surface property.
    /// Returns a cached UTF-8 string pointer.
    /// Signature exacte depuis Interop.Engine.cs ligne 15534: delegate* unmanaged[SuppressGCTransition]<IntPtr, IntPtr>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IntPtr Get__CPhysSrfcPrprts_m_description(IntPtr self)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return IntPtr.Zero;

        // Utiliser une clé différente pour le cache de description
        IntPtr descKey = new IntPtr(self.ToInt64() + 1); // Offset pour différencier du cache de name

        lock (_cacheLock)
        {
            if (_stringCache.TryGetValue(descKey, out var cachedPtr))
                return cachedPtr;

            // Allouer une nouvelle chaîne UTF-8
            string description = property.Description ?? "";
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(description);
            int size = utf8Bytes.Length + 1; // +1 pour le null terminator

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.Copy(utf8Bytes, 0, ptr, utf8Bytes.Length);
            Marshal.WriteByte(ptr, utf8Bytes.Length, 0); // Null terminator

            _stringCache[descKey] = ptr;
            _allocatedStrings.Add(ptr);

            return ptr;
        }
    }

    /// <summary>
    /// Set the description of a surface property.
    /// Signature exacte depuis Interop.Engine.cs ligne 15535: delegate* unmanaged[SuppressGCTransition]<IntPtr, IntPtr, void>
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Set__CPhysSrfcPrprts_m_description(IntPtr self, IntPtr descriptionPtr)
    {
        if (self == IntPtr.Zero || !PhysicsSystem.TryGetSurfaceProperty(self, out var property))
            return;

        string? description = Marshal.PtrToStringUTF8(descriptionPtr);
        if (description != null)
        {
            property.Description = description;

            // Invalider le cache de la description
            lock (_cacheLock)
            {
                IntPtr descKey = new IntPtr(self.ToInt64() + 1);
                if (_stringCache.TryGetValue(descKey, out var cachedPtr))
                {
                    _stringCache.Remove(descKey);
                    _allocatedStrings.Remove(cachedPtr);
                    Marshal.FreeHGlobal(cachedPtr);
                }
            }
        }
    }

}

