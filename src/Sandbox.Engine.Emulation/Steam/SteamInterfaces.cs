using System;
using System.Runtime.InteropServices;

namespace Sandbox.Engine.Emulation.Steam;

/// <summary>
/// Module d'émulation pour les interfaces Steam (ISteamUser_*, ISteamFriends_*, ISteamApps_*, etc.).
/// Ces fonctions retournent des valeurs par défaut si Steam est désactivé.
/// </summary>
public static unsafe class SteamInterfaces
{
    /// <summary>
    /// Initialise le module SteamInterfaces en patchant les fonctions natives.
    /// </summary>
    public static void Init(void** native)
    {
        // ISteamUser functions (indices depuis Interop.Engine.cs lignes 17146-17157)
        native[2281] = (void*)(delegate* unmanaged<IntPtr, int>)&ISteamUser_BLoggedOn;
        native[2282] = (void*)(delegate* unmanaged<IntPtr, ulong>)&ISteamUser_GetSteamID;
        native[2283] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr, uint, IntPtr, int>)&ISteamUser_GetVoice;
        native[2284] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int>)&ISteamUser_GetAvailableVoice;
        native[2285] = (void*)(delegate* unmanaged<IntPtr, uint>)&ISteamUser_GetVoiceOptimalSampleRate;
        native[2286] = (void*)(delegate* unmanaged<IntPtr, IntPtr, uint, IntPtr, uint, IntPtr, uint, int>)&ISteamUser_DecompressVoice;
        native[2287] = (void*)(delegate* unmanaged<IntPtr, void>)&ISteamUser_StartVoiceRecording;
        native[2288] = (void*)(delegate* unmanaged<IntPtr, void>)&ISteamUser_StopVoiceRecording;
        native[2289] = (void*)(delegate* unmanaged<IntPtr, ulong, IntPtr, IntPtr, void>)&ISteamUser_GetAuthSessionTicket;
        native[2290] = (void*)(delegate* unmanaged<IntPtr, ulong, IntPtr, int, long>)&ISteamUser_BeginAuthSession;
        native[2291] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISteamUser_CancelAuthTicket;
        native[2292] = (void*)(delegate* unmanaged<IntPtr, ulong, void>)&ISteamUser_EndAuthSession;
        
        // ISteamUtils functions (indices depuis Interop.Engine.cs lignes 17158-17159)
        native[2293] = (void*)(delegate* unmanaged<IntPtr, uint, int>)&ISteamUtils_InitFilterText;
        native[2294] = (void*)(delegate* unmanaged<IntPtr, long, ulong, IntPtr, IntPtr, uint, int>)&ISteamUtils_FilterText;
        
        // ISteamApps functions (indices depuis Interop.Engine.cs lignes 17067-17078)
        native[2202] = (void*)(delegate* unmanaged<IntPtr, int, int>)&ISteamApps_BIsAppInstalled;
        native[2203] = (void*)(delegate* unmanaged<IntPtr, int>)&ISteamApps_BIsCybercafe;
        native[2204] = (void*)(delegate* unmanaged<IntPtr, int, int>)&ISteamApps_BIsDlcInstalled;
        native[2205] = (void*)(delegate* unmanaged<IntPtr, int>)&ISteamApps_BIsLowViolence;
        native[2206] = (void*)(delegate* unmanaged<IntPtr, int>)&ISteamApps_BIsSubscribed;
        native[2207] = (void*)(delegate* unmanaged<IntPtr, int, int>)&ISteamApps_BIsSubscribedApp;
        native[2208] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISteamApps_GetAvailableGameLanguages;
        native[2209] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISteamApps_GetCurrentGameLanguage;
        native[2210] = (void*)(delegate* unmanaged<IntPtr, int>)&ISteamApps_GetAppBuildId;
        native[2211] = (void*)(delegate* unmanaged<IntPtr, int>)&ISteamApps_BIsVACBanned;
        native[2212] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISteamApps_GetCommandLine;
        native[2213] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr>)&ISteamApps_GetAppInstallDir;
        
        // ISteamFriends functions (indices depuis Interop.Engine.cs lignes 17079-17083)
        native[2214] = (void*)(delegate* unmanaged<IntPtr, ulong, int, int, IntPtr>)&ISteamFriends_GetProfileItemPropertyString;
        native[2215] = (void*)(delegate* unmanaged<IntPtr, ulong, ulong>)&ISteamFriends_RequestEquippedProfileItems;
        native[2216] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISteamFriends_GetPersonaName;
        native[2217] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int>)&ISteamFriends_SetRichPresence;
        native[2218] = (void*)(delegate* unmanaged<IntPtr, void>)&ISteamFriends_ClearRichPresence;
        
        // ISteamGameServer functions (indices depuis Interop.Engine.cs lignes 17084-17100)
        native[2219] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISteamGameServer_SetServerName;
        native[2220] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISteamGameServer_SetMapName;
        native[2221] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISteamGameServer_SetGameTags;
        native[2222] = (void*)(delegate* unmanaged<IntPtr, int, void>)&ISteamGameServer_SetDedicatedServer;
        native[2223] = (void*)(delegate* unmanaged<IntPtr, int, void>)&ISteamGameServer_SetAdvertiseServerActive;
        native[2224] = (void*)(delegate* unmanaged<IntPtr, int, void>)&ISteamGameServer_SetMaxPlayerCount;
        native[2225] = (void*)(delegate* unmanaged<IntPtr, void>)&ISteamGameServer_LogOnAnonymous;
        native[2226] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISteamGameServer_LogOn;
        native[2227] = (void*)(delegate* unmanaged<IntPtr, void>)&ISteamGameServer_LogOff;
        native[2228] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISteamGameServer_SetGameDescription;
        native[2229] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISteamGameServer_SetProduct;
        native[2230] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISteamGameServer_SetModDir;
        native[2231] = (void*)(delegate* unmanaged<IntPtr, int>)&ISteamGameServer_BLoggedOn;
        native[2232] = (void*)(delegate* unmanaged<IntPtr, ulong, IntPtr, IntPtr, void>)&ISteamGameServer_GetAuthSessionTicket;
        native[2233] = (void*)(delegate* unmanaged<IntPtr, ulong, IntPtr, int, long>)&ISteamGameServer_BeginAuthSession;
        native[2234] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISteamGameServer_CancelAuthTicket;
        native[2235] = (void*)(delegate* unmanaged<IntPtr, ulong, void>)&ISteamGameServer_EndAuthSession;
        
        Console.WriteLine("[NativeAOT] SteamInterfaces module initialized");
    }
    
    // ============================================
    // ISteamUser functions
    // ============================================
    
    [UnmanagedCallersOnly]
    public static int ISteamUser_BLoggedOn(IntPtr self)
    {
        // Si Steam est désactivé, retourner 0 (false) (pas d'exception)
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamUser_BLoggedOn: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static ulong ISteamUser_GetSteamID(IntPtr self)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamUser_GetSteamID: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamUser_GetVoice(IntPtr self, int bWantCompressed, IntPtr pDestBuffer, uint cbDestBufferSize, IntPtr nBytesWritten)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamUser_GetVoice: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamUser_GetAvailableVoice(IntPtr self, IntPtr availableData)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamUser_GetAvailableVoice: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static uint ISteamUser_GetVoiceOptimalSampleRate(IntPtr self)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamUser_GetVoiceOptimalSampleRate: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamUser_DecompressVoice(IntPtr self, IntPtr pCompressed, uint cbCompressed, IntPtr pDestBuffer, uint cbDestBufferSize, IntPtr nBytesWritten, uint nDesiredSampleRate)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamUser_DecompressVoice: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamUser_StartVoiceRecording(IntPtr self)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamUser_StartVoiceRecording: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamUser_StopVoiceRecording(IntPtr self)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamUser_StopVoiceRecording: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamUser_GetAuthSessionTicket(IntPtr self, ulong targetSteamId, IntPtr buffer, IntPtr ticketLength)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamUser_GetAuthSessionTicket: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static long ISteamUser_BeginAuthSession(IntPtr self, ulong senderSteamId, IntPtr buffer, int length)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamUser_BeginAuthSession: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamUser_CancelAuthTicket(IntPtr self, IntPtr ticket)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamUser_CancelAuthTicket: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamUser_EndAuthSession(IntPtr self, ulong steamId)
    {
        // Si Steam est désactivé, rien à faire (no-op)
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamUser_EndAuthSession: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    // ============================================
    // ISteamUtils functions
    // ============================================
    
    [UnmanagedCallersOnly]
    public static int ISteamUtils_InitFilterText(IntPtr self, uint unFilterOptions)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamUtils_InitFilterText: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamUtils_FilterText(IntPtr self, long eContext, ulong sourceSteamID, IntPtr pchInputMessage, IntPtr pchOutFilteredText, uint nByteSizeOutFilteredText)
    {
        // Si Steam est désactivé, retourner 0 (pas d'exception)
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamUtils_FilterText: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    // ============================================
    // ISteamApps functions
    // ============================================
    
    [UnmanagedCallersOnly]
    public static int ISteamApps_BIsAppInstalled(IntPtr self, int appid)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamApps_BIsAppInstalled: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamApps_BIsCybercafe(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamApps_BIsCybercafe: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamApps_BIsDlcInstalled(IntPtr self, int appID)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamApps_BIsDlcInstalled: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamApps_BIsLowViolence(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamApps_BIsLowViolence: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamApps_BIsSubscribed(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamApps_BIsSubscribed: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamApps_BIsSubscribedApp(IntPtr self, int appID)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamApps_BIsSubscribedApp: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr ISteamApps_GetAvailableGameLanguages(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("ISteamApps_GetAvailableGameLanguages: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr ISteamApps_GetCurrentGameLanguage(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("ISteamApps_GetCurrentGameLanguage: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamApps_GetAppBuildId(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamApps_GetAppBuildId: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamApps_BIsVACBanned(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamApps_BIsVACBanned: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr ISteamApps_GetCommandLine(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("ISteamApps_GetCommandLine: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr ISteamApps_GetAppInstallDir(IntPtr self, int appid)
    {
        if (!SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("ISteamApps_GetAppInstallDir: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    // ============================================
    // ISteamFriends functions
    // ============================================
    
    [UnmanagedCallersOnly]
    public static IntPtr ISteamFriends_GetProfileItemPropertyString(IntPtr self, ulong steamId, int itemType, int itemProperty)
    {
        if (!SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("ISteamFriends_GetProfileItemPropertyString: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static ulong ISteamFriends_RequestEquippedProfileItems(IntPtr self, ulong steamId)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamFriends_RequestEquippedProfileItems: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr ISteamFriends_GetPersonaName(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return IntPtr.Zero;
        throw new NotImplementedException("ISteamFriends_GetPersonaName: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamFriends_SetRichPresence(IntPtr self, IntPtr pchKey, IntPtr pchValue)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamFriends_SetRichPresence: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamFriends_ClearRichPresence(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamFriends_ClearRichPresence: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    // ============================================
    // ISteamGameServer functions
    // ============================================
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_SetServerName(IntPtr self, IntPtr pszServerName)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_SetServerName: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_SetMapName(IntPtr self, IntPtr pszMapName)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_SetMapName: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_SetGameTags(IntPtr self, IntPtr pszGameTags)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_SetGameTags: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_SetDedicatedServer(IntPtr self, int bDedicated)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_SetDedicatedServer: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_SetAdvertiseServerActive(IntPtr self, int bActive)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_SetAdvertiseServerActive: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_SetMaxPlayerCount(IntPtr self, int cPlayersMax)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_SetMaxPlayerCount: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_LogOnAnonymous(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_LogOnAnonymous: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_LogOn(IntPtr self, IntPtr pszToken)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_LogOn: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_LogOff(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_LogOff: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_SetGameDescription(IntPtr self, IntPtr pszGameDescription)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_SetGameDescription: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_SetProduct(IntPtr self, IntPtr pszProduct)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_SetProduct: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_SetModDir(IntPtr self, IntPtr pszModDir)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_SetModDir: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static int ISteamGameServer_BLoggedOn(IntPtr self)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamGameServer_BLoggedOn: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_GetAuthSessionTicket(IntPtr self, ulong targetSteamId, IntPtr buffer, IntPtr ticketLength)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_GetAuthSessionTicket: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static long ISteamGameServer_BeginAuthSession(IntPtr self, ulong senderSteamId, IntPtr buffer, int length)
    {
        if (!SteamAPI.IsSteamEnabled()) return 0;
        throw new NotImplementedException("ISteamGameServer_BeginAuthSession: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_CancelAuthTicket(IntPtr self, IntPtr ticket)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_CancelAuthTicket: Steam Networking not yet implemented in the linux emulation layer");
    }
    
    [UnmanagedCallersOnly]
    public static void ISteamGameServer_EndAuthSession(IntPtr self, ulong steamId)
    {
        if (!SteamAPI.IsSteamEnabled()) return;
        throw new NotImplementedException("ISteamGameServer_EndAuthSession: Steam Networking not yet implemented in the linux emulation layer");
    }
}

