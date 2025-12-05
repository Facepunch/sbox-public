using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Steam;

namespace Sandbox.Engine.Emulation.Steam;

/// <summary>
/// Module d'émulation pour CSteamInventoryResult (CStmnvntryRslt_*).
/// Gère les résultats d'inventaire Steam.
/// Sur Linux, Steam n'est pas toujours disponible, donc ces fonctions peuvent être désactivées.
/// </summary>
public static unsafe class SteamInventoryResult
{
    /// <summary>
    /// Données internes pour un résultat d'inventaire Steam émulé.
    /// </summary>
    internal class InventoryResultData
    {
        public bool IsPending { get; set; } = false;
        public bool IsOk { get; set; } = false;
        public uint Timestamp { get; set; } = 0;
        public int Count { get; set; } = 0;
        public List<IntPtr> Items { get; } = new(); // Liste des CSteamItemInstance
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero;
    }
    
    private static readonly Dictionary<IntPtr, InventoryResultData> _results = new();
    
    /// <summary>
    /// Initialise le module SteamInventoryResult en patchant les fonctions natives.
    /// </summary>
    public static void Init(void** native)
    {
        // Indices depuis Interop.Engine.cs lignes 16063-16069
        native[1198] = (void*)(delegate* unmanaged<IntPtr, void>)&CStmnvntryRslt_Destroy;
        native[1199] = (void*)(delegate* unmanaged<IntPtr, int>)&CStmnvntryRslt_IsPending;
        native[1200] = (void*)(delegate* unmanaged<IntPtr, int>)&CStmnvntryRslt_IsOk;
        native[1201] = (void*)(delegate* unmanaged<IntPtr, ulong, int>)&CStmnvntryRslt_CheckSteamId;
        native[1202] = (void*)(delegate* unmanaged<IntPtr, uint>)&CStmnvntryRslt_GetTimestamp;
        native[1203] = (void*)(delegate* unmanaged<IntPtr, int>)&CStmnvntryRslt_Count;
        native[1204] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr>)&CStmnvntryRslt_Get;
        
        Console.WriteLine("[NativeAOT] SteamInventoryResult module initialized");
    }
    
    /// <summary>
    /// Détruit un résultat d'inventaire Steam.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CStmnvntryRslt_Destroy(IntPtr self)
    {
        if (self == IntPtr.Zero) return;
        
        lock (_results)
        {
            if (_results.TryGetValue(self, out var resultData))
            {
                // Note: HandleManager.Unregister n'est pas nécessaire ici car BindingPtr est un IntPtr
                // et HandleManager utilise des int. Pour l'instant, on supprime juste du dictionnaire.
                _results.Remove(self);
            }
        }
    }
    
    /// <summary>
    /// Vérifie si le résultat est en attente.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CStmnvntryRslt_IsPending(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        
        if (!SteamAPI.IsSteamEnabled())
        {
            // Si Steam est désactivé, retourner 0 (pas d'exception)
            return 0;
        }
        
        lock (_results)
        {
            if (_results.TryGetValue(self, out var resultData))
            {
                return resultData.IsPending ? 1 : 0;
            }
        }
        
        // Résultat non trouvé, retourner 0 par défaut
        return 0;
    }
    
    /// <summary>
    /// Vérifie si le résultat est OK.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CStmnvntryRslt_IsOk(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        
        if (!SteamAPI.IsSteamEnabled())
        {
            // Si Steam est désactivé, retourner 0 (pas d'exception)
            return 0;
        }
        
        lock (_results)
        {
            if (_results.TryGetValue(self, out var resultData))
            {
                return resultData.IsOk ? 1 : 0;
            }
        }
        
        // Résultat non trouvé, retourner 0 par défaut
        return 0;
    }
    
    /// <summary>
    /// Vérifie si le SteamID correspond au résultat.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CStmnvntryRslt_CheckSteamId(IntPtr self, ulong steamid)
    {
        if (self == IntPtr.Zero) return 0;
        
        if (!SteamAPI.IsSteamEnabled())
        {
            // Si Steam est désactivé, retourner 0 (false) (pas d'exception)
            return 0;
        }
        
        lock (_results)
        {
            if (_results.TryGetValue(self, out _))
            {
                // Émulation simplifiée : retourne 0 (false) par défaut
                return 0;
            }
        }
        
        // Résultat non trouvé, retourner 0 par défaut
        return 0;
    }
    
    /// <summary>
    /// Obtient le timestamp du résultat.
    /// </summary>
    [UnmanagedCallersOnly]
    public static uint CStmnvntryRslt_GetTimestamp(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        
        if (!SteamAPI.IsSteamEnabled())
        {
            // Si Steam est désactivé, retourner 0 (pas d'exception)
            return 0;
        }
        
        lock (_results)
        {
            if (_results.TryGetValue(self, out var resultData))
            {
                return resultData.Timestamp;
            }
        }
        
        // Résultat non trouvé, retourner 0 par défaut
        return 0;
    }
    
    /// <summary>
    /// Obtient le nombre d'items dans le résultat.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CStmnvntryRslt_Count(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        
        if (!SteamAPI.IsSteamEnabled())
        {
            // Si Steam est désactivé, retourner 0 (pas d'exception)
            return 0;
        }
        
        lock (_results)
        {
            if (_results.TryGetValue(self, out var resultData))
            {
                return resultData.Count;
            }
        }
        
        // Résultat non trouvé, retourner 0 par défaut
        return 0;
    }
    
    /// <summary>
    /// Obtient un item à l'index spécifié.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CStmnvntryRslt_Get(IntPtr self, int index)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        if (!SteamAPI.IsSteamEnabled())
        {
            // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
            return IntPtr.Zero;
        }
        
        lock (_results)
        {
            if (_results.TryGetValue(self, out var resultData))
            {
                if (index >= 0 && index < resultData.Items.Count)
                {
                    return resultData.Items[index];
                }
            }
        }
        
        // Résultat non trouvé ou index invalide, retourner IntPtr.Zero par défaut
        return IntPtr.Zero;
    }
}

