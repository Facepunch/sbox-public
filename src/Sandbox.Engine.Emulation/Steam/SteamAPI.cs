using System;
using System.Runtime.InteropServices;

namespace Sandbox.Engine.Emulation.Steam;

/// <summary>
/// Module d'émulation pour les fonctions Steam API globales (globalSteam_*).
/// Sur Linux, Steam n'est pas toujours disponible, donc ces fonctions retournent des valeurs par défaut.
/// Note: Les interfaces Steam retournent IntPtr.Zero pour indiquer qu'elles ne sont pas disponibles.
/// Le module peut être désactivé via la variable d'environnement SBOX_DISABLE_STEAM ou en appelant SetSteamEnabled(false).
/// </summary>
public static unsafe class SteamAPI
{
    /// <summary>
    /// Indique si Steam est activé. Par défaut, Steam est désactivé sur Linux (émulation).
    /// Peut être activé via la variable d'environnement SBOX_ENABLE_STEAM=1 ou SetSteamEnabled(true).
    /// </summary>
    private static bool _steamEnabled = false;
    
    /// <summary>
    /// Active ou désactive Steam. Si désactivé, toutes les fonctions retournent des valeurs par défaut.
    /// </summary>
    public static void SetSteamEnabled(bool enabled)
    {
        _steamEnabled = enabled;
        Console.WriteLine($"[NativeAOT] SteamAPI: Steam {(enabled ? "enabled" : "disabled")}");
    }
    
    /// <summary>
    /// Vérifie si Steam est activé.
    /// </summary>
    public static bool IsSteamEnabled() => _steamEnabled;
    
    static SteamAPI()
    {
        // Vérifier la variable d'environnement
        var envValue = Environment.GetEnvironmentVariable("SBOX_ENABLE_STEAM");
        if (envValue == "1" || envValue?.ToLower() == "true")
        {
            _steamEnabled = true;
            Console.WriteLine("[NativeAOT] SteamAPI: Steam enabled via SBOX_ENABLE_STEAM environment variable");
        }
    }
    /// <summary>
    /// Initialise le module Steam en patchant les fonctions natives.
    /// </summary>
    public static void Init(void** native)
    {
        // Indices depuis Interop.Engine.cs lignes 16472-16488
        native[1607] = (void*)(delegate* unmanaged<IntPtr>)&globalSteam_SteamHTMLSurface;
        native[1608] = (void*)(delegate* unmanaged<void>)&globalSteam_SteamAPI_RunCallbacks;
        native[1609] = (void*)(delegate* unmanaged<void>)&globalSteam_SteamGameServer_RunCallbacks;
        native[1610] = (void*)(delegate* unmanaged<IntPtr>)&globalSteam_SteamUser;
        native[1611] = (void*)(delegate* unmanaged<IntPtr>)&globalSteam_SteamFriends;
        native[1612] = (void*)(delegate* unmanaged<IntPtr>)&globalSteam_SteamNetworkingMessages;
        native[1613] = (void*)(delegate* unmanaged<IntPtr>)&globalSteam_SteamNetworkingUtils;
        native[1614] = (void*)(delegate* unmanaged<IntPtr>)&globalSteam_SteamMatchmaking;
        native[1615] = (void*)(delegate* unmanaged<IntPtr>)&globalSteam_SteamGameServer;
        native[1616] = (void*)(delegate* unmanaged<IntPtr>)&globalSteam_SteamApps;
        native[1617] = (void*)(delegate* unmanaged<IntPtr>)&globalSteam_SteamUtils;
        native[1618] = (void*)(delegate* unmanaged<int>)&globalSteam_SteamGameServer_BSecure;
        native[1619] = (void*)(delegate* unmanaged<ulong>)&globalSteam_SteamGameServer_GetSteamID;
        native[1620] = (void*)(delegate* unmanaged<void>)&globalSteam_SteamGameServer_Shutdown;
        native[1621] = (void*)(delegate* unmanaged<void>)&globalSteam_SteamGameServer_ReleaseCurrentThreadMemory;
        native[1622] = (void*)(delegate* unmanaged<IntPtr>)&globalSteam_SteamNetworkingSockets;
        native[1623] = (void*)(delegate* unmanaged<int, int, IntPtr, void>)&globalSteam_SteamGameServer_Init;
        
        Console.WriteLine("[NativeAOT] SteamAPI module initialized (Linux emulation - Steam not available)");
    }
    
    /// <summary>
    /// Retourne un pointeur vers l'interface SteamHTMLSurface.
    /// Sur Linux sans Steam, retourne IntPtr.Zero.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr globalSteam_SteamHTMLSurface()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Exécute les callbacks Steam API.
    /// Sur Linux sans Steam, c'est un no-op.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void globalSteam_SteamAPI_RunCallbacks()
    {
        if (!_steamEnabled)
        {
            // Steam désactivé, rien à faire (pas d'exception car c'est appelé fréquemment)
            return;
        }
        // Steam n'est pas disponible, rien à faire
    }
    
    /// <summary>
    /// Exécute les callbacks Steam Game Server API.
    /// Sur Linux sans Steam, c'est un no-op.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void globalSteam_SteamGameServer_RunCallbacks()
    {
        if (!_steamEnabled)
        {
            // Steam désactivé, rien à faire (pas d'exception car c'est appelé fréquemment)
            return;
        }
        // Steam n'est pas disponible, rien à faire
    }
    
    
    /// <summary>
    /// Retourne un pointeur vers l'interface SteamUser.
    /// Sur Linux sans Steam, retourne IntPtr.Zero.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr globalSteam_SteamUser()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Retourne un pointeur vers l'interface SteamFriends.
    /// Sur Linux sans Steam, retourne IntPtr.Zero.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr globalSteam_SteamFriends()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Retourne un pointeur vers l'interface SteamNetworkingMessages.
    /// Sur Linux sans Steam, retourne IntPtr.Zero.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr globalSteam_SteamNetworkingMessages()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Retourne un pointeur vers l'interface SteamNetworkingUtils.
    /// Sur Linux sans Steam, retourne IntPtr.Zero.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr globalSteam_SteamNetworkingUtils()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Retourne un pointeur vers l'interface SteamMatchmaking.
    /// Sur Linux sans Steam, retourne IntPtr.Zero.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr globalSteam_SteamMatchmaking()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Retourne un pointeur vers l'interface SteamGameServer.
    /// Sur Linux sans Steam, retourne IntPtr.Zero.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr globalSteam_SteamGameServer()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Retourne un pointeur vers l'interface SteamApps.
    /// Sur Linux sans Steam, retourne IntPtr.Zero.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr globalSteam_SteamApps()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Retourne un pointeur vers l'interface SteamUtils.
    /// Sur Linux sans Steam, retourne IntPtr.Zero.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr globalSteam_SteamUtils()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Retourne si le serveur Steam Game Server est sécurisé.
    /// Sur Linux sans Steam, retourne 0 (false).
    /// </summary>
    [UnmanagedCallersOnly]
    public static int globalSteam_SteamGameServer_BSecure()
    {
        // Si Steam est désactivé, retourner 0 (false) (pas d'exception)
        return 0; // false
    }
    
    /// <summary>
    /// Retourne le SteamID du serveur Steam Game Server.
    /// Sur Linux sans Steam, retourne 0.
    /// </summary>
    [UnmanagedCallersOnly]
    public static ulong globalSteam_SteamGameServer_GetSteamID()
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        return 0;
    }
    
    /// <summary>
    /// Arrête le serveur Steam Game Server.
    /// Sur Linux sans Steam, c'est un no-op.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void globalSteam_SteamGameServer_Shutdown()
    {
        // Si Steam est désactivé, rien à faire (no-op)
    }
    
    /// <summary>
    /// Libère la mémoire du thread courant pour Steam Game Server.
    /// Sur Linux sans Steam, c'est un no-op.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void globalSteam_SteamGameServer_ReleaseCurrentThreadMemory()
    {
        // Si Steam est désactivé, rien à faire (no-op)
    }
    
    /// <summary>
    /// Retourne un pointeur vers l'interface SteamNetworkingSockets.
    /// Sur Linux sans Steam, retourne IntPtr.Zero.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr globalSteam_SteamNetworkingSockets()
    {
        // Si Steam est désactivé, retourner IntPtr.Zero (pas d'exception)
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Initialise le serveur Steam Game Server.
    /// Sur Linux sans Steam, c'est un no-op.
    /// Signature: delegate* unmanaged&lt; int gamePort, int queryPort, IntPtr serverVersion, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void globalSteam_SteamGameServer_Init(int gamePort, int queryPort, IntPtr serverVersion)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        // Le moteur peut continuer sans Steam
    }
}

