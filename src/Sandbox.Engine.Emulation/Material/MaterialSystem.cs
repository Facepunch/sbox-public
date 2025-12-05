using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sandbox;
using Sandbox.Engine.Emulation.Common;
using Sandbox.Engine.Emulation.RenderAttributes;
using Sandbox.Engine.Emulation;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace Sandbox.Engine.Emulation.Material;

/// <summary>
/// Module d'émulation pour MaterialSystem2 et IMaterial2.
/// Gère la création et la gestion des matériaux.
/// </summary>
public static unsafe class MaterialSystem
{
    /// <summary>
    /// Données internes pour un matériau émulé.
    /// </summary>
    private class MaterialData
    {
        public string Name { get; set; } = "";
        public string Shader { get; set; } = "";
        public bool Anonymous { get; set; }
        public IntPtr RenderAttributes { get; set; } = IntPtr.Zero;
        public bool IsError { get; set; } = false;
        public bool IsLoaded { get; set; } = true; // Par défaut, considéré comme chargé
        public bool IsEdited { get; set; } = false;
        public ulong SimilarityKey { get; set; } = 0;
        public Dictionary<string, object> Parameters { get; } = new(); // Stocke les paramètres du matériau
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero; // Pointeur de binding unique
        public IntPtr MaterialMode { get; set; } = IntPtr.Zero; // Mode de rendu du matériau
    }
    
    /// <summary>
    /// Initialise les fonctions natives de MaterialSystem2 et IMaterial2.
    /// </summary>
    public static void Init(void** native)
    {
        // CMtrlSystm2ppSys functions (indices 498-540)
        native[498] = (void*)(delegate* unmanaged<NativeEngine.MaterialSystem2AppSystemDictCreateInfo*, IntPtr>)&CMtrlSystm2ppSys_Create;
        native[499] = (void*)(delegate* unmanaged<IntPtr, void>)&CMtrlSystm2ppSys_Destroy;
        native[500] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_Init;
        native[501] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_InitWithoutMaterialSystem;
        native[503] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CMtrlSystm2ppSys_GetAppWindow;
        native[504] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CMtrlSystm2ppSys_GetAppWindowSwapChain;
        native[505] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CMtrlSystm2ppSys_SetAppWindowTitle;
        native[506] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CMtrlSystm2ppSys_SetAppWindowIcon;
        native[507] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CMtrlSystm2ppSys_SetInitialAppWindowImage;
        native[508] = (void*)(delegate* unmanaged<IntPtr, int, void>)&CMtrlSystm2ppSys_SetAppWindowDiscardMouseFocusClick;
        native[509] = (void*)(delegate* unmanaged<IntPtr, void>)&CMtrlSystm2ppSys_DrawInitialWindowImage;
        native[511] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CMtrlSystm2ppSys_SetModuleSearchPath;
        native[512] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CMtrlSystm2ppSys_SetModGameSubdir;
        native[513] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, void>)&CMtrlSystm2ppSys_SetModFromFileName;
        native[514] = (void*)(delegate* unmanaged<IntPtr, void>)&CMtrlSystm2ppSys_DisableModPathCheck;
        native[515] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CMtrlSystm2ppSys_SetDefaultRenderSystemOption;
        native[516] = (void*)(delegate* unmanaged<IntPtr, int, void>)&CMtrlSystm2ppSys_SetInitializationPhase;
        native[502] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_InitFinishSetupMaterialSystem;
        native[510] = (void*)(delegate* unmanaged<IntPtr, int, void>)&CMtrlSystm2ppSys_SuppressStartupManifestLoad;
        native[532] = (void*)(delegate* unmanaged<IntPtr, void>)&CMtrlSystm2ppSys_SuppressCOMInitialization;
        native[521] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_IsConsoleApp;
        native[522] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_IsGameApp;
        native[523] = (void*)(delegate* unmanaged<IntPtr, int, void>)&CMtrlSystm2ppSys_SetDedicatedServer;
        native[524] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_IsDedicatedServer;
        native[525] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CMtrlSystm2ppSys_GetContentPath;
        native[526] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CMtrlSystm2ppSys_GetModGameSubdir;
        native[528] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_IsInToolsMode;
        native[529] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_IsInDeveloperMode;
        native[530] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_IsInVRMode;
        native[533] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_IsRunningOnCustomerMachine;
        native[536] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_IsInTestMode;
        native[517] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_GetInitializationPhase;
        native[518] = (void*)(delegate* unmanaged<IntPtr, void>)&CMtrlSystm2ppSys_PreShutdown;
        native[519] = (void*)(delegate* unmanaged<IntPtr, uint, int>)&CMtrlSystm2ppSys_InitSDL;
        native[520] = (void*)(delegate* unmanaged<IntPtr, void>)&CMtrlSystm2ppSys_ShutdownSDL;
        native[531] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, int, int, int, int, int, IntPtr>)&CMtrlSystm2ppSys_CreateAppWindow;
        native[534] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)&CMtrlSystm2ppSys_AddSystem;
        native[537] = (void*)(delegate* unmanaged<IntPtr, void>)&CMtrlSystm2ppSys_SetInStandaloneApp;
        native[538] = (void*)(delegate* unmanaged<IntPtr, int>)&CMtrlSystm2ppSys_IsStandaloneApp;
        native[535] = (void*)(delegate* unmanaged<IntPtr, void>)&CMtrlSystm2ppSys_SetInTestMode;
        native[527] = (void*)(delegate* unmanaged<IntPtr, void>)&CMtrlSystm2ppSys_SetInToolsMode;
        native[539] = (void*)(delegate* unmanaged<IntPtr, uint, void>)&CMtrlSystm2ppSys_SetSteamAppId;
        native[540] = (void*)(delegate* unmanaged<IntPtr, uint>)&CMtrlSystm2ppSys_GetSteamAppId;
        
        // g_pMtrlSystm2 functions (indices 1457-1460)
        native[1457] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, IntPtr>)&g_pMtrlSystm2_CreateRawMaterial;
        native[1458] = (void*)(delegate* unmanaged<IntPtr, int, int, IntPtr>)&g_pMtrlSystm2_CreateProceduralMaterialCopy;
        native[1459] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&g_pMtrlSystm2_FindOrCreateMaterialFromResource;
        native[1460] = (void*)(delegate* unmanaged<void>)&g_pMtrlSystm2_FrameUpdate;
        
        // IMaterial2 functions (indices depuis Interop.Engine.cs lignes 16654-16683)
        native[1789] = (void*)(delegate* unmanaged<IntPtr, void>)&IMaterial2_DestroyStrongHandle;
        native[1790] = (void*)(delegate* unmanaged<IntPtr, int>)&IMaterial2_IsStrongHandleValid;
        native[1791] = (void*)(delegate* unmanaged<IntPtr, int>)&IMaterial2_IsError;
        native[1792] = (void*)(delegate* unmanaged<IntPtr, int>)&IMaterial2_IsStrongHandleLoaded;
        native[1793] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&IMaterial2_CopyStrongHandle;
        native[1794] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&IMaterial2_GetBindingPtr;
        native[1795] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&IMaterial2_GetName;
        native[1796] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&IMaterial2_GetNameWithMod;
        native[1797] = (void*)(delegate* unmanaged<IntPtr, ulong>)&IMaterial2_GetSimilarityKey;
        native[1798] = (void*)(delegate* unmanaged<IntPtr, int>)&IMaterial2_IsLoaded;
        native[1799] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, IntPtr>)&IMaterial2_GetMode;
        native[1800] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&IMaterial2_GetMode_1;
        native[1801] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&IMaterial2_GetMode_2;
        native[1802] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&IMaterial2_GetRenderAttributes;
        native[1803] = (void*)(delegate* unmanaged<IntPtr, void>)&IMaterial2_RecreateAllStaticConstantAndCommandBuffers;
        native[1804] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&IMaterial2_GetFirstTextureAttribute;
        native[1805] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, int, int>)&IMaterial2_GetBoolAttributeOrDefault;
        native[1806] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, int, int>)&IMaterial2_GetIntAttributeOrDefault;
        native[1807] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, float, float>)&IMaterial2_GetFloatAttributeOrDefault;
        native[1808] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, IntPtr>)&IMaterial2_GetTextureAttributeOrDefault;
        native[1809] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int>)&IMaterial2_HasParam;
        native[1810] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&IMaterial2_Set;
        native[1811] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)&IMaterial2_GetString;
        native[1812] = (void*)(delegate* unmanaged<IntPtr, IntPtr, Vector4*, void>)&IMaterial2_Set_1;
        native[1813] = (void*)(delegate* unmanaged<IntPtr, IntPtr, Vector4>)&IMaterial2_GetVector4;
        native[1814] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&IMaterial2_Set_2;
        native[1815] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr>)&IMaterial2_GetTexture;
        native[1816] = (void*)(delegate* unmanaged<IntPtr, int, void>)&IMaterial2_SetEdited;
        native[1817] = (void*)(delegate* unmanaged<IntPtr, int>)&IMaterial2_IsEdited;
        native[1818] = (void*)(delegate* unmanaged<IntPtr, void>)&IMaterial2_ReloadStaticCombos;
    }
    
    // ========== CMtrlSystm2ppSys Functions ==========
    
    /// <summary>
    /// Crée un nouveau MaterialSystem2AppSystemDict.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CMtrlSystm2ppSys_Create(NativeEngine.MaterialSystem2AppSystemDictCreateInfo* createInfo)
    {
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_Create");
        return (IntPtr)1; // Return a dummy non-null pointer
    }
    
    /// <summary>
    /// Détruit le MaterialSystem2AppSystemDict.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_Destroy(IntPtr self)
    {
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_Destroy");
        // Cleanup if needed
    }
    
    /// <summary>
    /// Initialise le MaterialSystem2AppSystemDict.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_Init(IntPtr self)
    {
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_Init");
        return 1; // Success
    }
    
    // Référence à la fenêtre (sera initialisée depuis EngineExports ou PlatformFunctions)
    private static WindowHandle* _windowHandle = null;
    private static Glfw? _glfw;
    private static GL? _gl;
    
    /// <summary>
    /// Initialise la référence à la fenêtre (appelée depuis EngineExports ou PlatformFunctions).
    /// </summary>
    internal static void SetWindowHandle(WindowHandle* handle)
    {
        _windowHandle = handle;
    }
    
    /// <summary>
    /// Obtient la fenêtre de l'application.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CMtrlSystm2ppSys_GetAppWindow(IntPtr self)
    {
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_GetAppWindow");
        if (_windowHandle == null)
            return IntPtr.Zero;
        return (IntPtr)_windowHandle;
    }
    
    /// <summary>
    /// Obtient le SwapChain de la fenêtre de l'application.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CMtrlSystm2ppSys_GetAppWindowSwapChain(IntPtr self)
    {
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_GetAppWindowSwapChain");
        if (_windowHandle == null)
            return IntPtr.Zero;
        // Return window handle as SwapChain (GLFW window handle serves as SwapChain)
        return (IntPtr)_windowHandle;
    }
    
    /// <summary>
    /// Crée une fenêtre d'application.
    /// Signature depuis Interop.Engine.cs ligne 15278: delegate* unmanaged&lt; IntPtr, IntPtr, int, int, int, int, int, int, IntPtr &gt;
    /// Paramètres: self, pTitle, nPlatWindowFlags, x, y, w, h, nRefreshRateHz, icon
    /// Utilise les instances GLFW/GL de PlatformFunctions (partagées).
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CMtrlSystm2ppSys_CreateAppWindow(IntPtr self, IntPtr pTitle, int nPlatWindowFlags, int x, int y, int w, int h, int nRefreshRateHz)
    {
        string? title = Marshal.PtrToStringUTF8(pTitle) ?? "S&box NativeAOT";
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_CreateAppWindow: {title} ({w}x{h})");

        try
        {
            // Utiliser les instances de PlatformFunctions (partagées)
            var glfw = Platform.PlatformFunctions.GetGlfw();
            var existingWindowHandle = Platform.PlatformFunctions.GetWindowHandle();
            
            // Si une fenêtre existe déjà (créée dans SourceEngineInit), la réutiliser
            if (existingWindowHandle != null && glfw != null)
            {
                Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_CreateAppWindow: Reusing existing window");
                _windowHandle = existingWindowHandle;
                SetWindowHandle(_windowHandle);
                return (IntPtr)_windowHandle;
            }
            
            // Sinon, créer une nouvelle fenêtre (normalement ne devrait pas arriver car SourceEngineInit crée déjà une fenêtre)
            if (glfw == null)
            {
                glfw = Glfw.GetApi();
                if (!glfw.Init())
                {
                    Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_CreateAppWindow: GLFW Init failed!");
                    return IntPtr.Zero;
                }
                Platform.PlatformFunctions.SetSharedState(glfw, null, null);
            }

            glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGL);
            glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
            glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
            glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);

            _windowHandle = glfw.CreateWindow(w > 0 ? w : 1280, h > 0 ? h : 720, title, null, null);

            if (_windowHandle == null)
            {
                Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_CreateAppWindow: GLFW CreateWindow failed!");
                byte* errorDesc;
                var errorCode = glfw.GetError(out errorDesc);
                string? errorStr = Marshal.PtrToStringUTF8((IntPtr)errorDesc);
                Console.WriteLine($"[NativeAOT] GLFW Error: {errorCode} - {errorStr}");
                return IntPtr.Zero;
            }

            glfw.MakeContextCurrent(_windowHandle);

            // Initialize GL si pas déjà fait
            var gl = Platform.PlatformFunctions.GetGL();
            if (gl == null)
            {
                gl = GL.GetApi(new GlfwContext(glfw, _windowHandle));
                Platform.PlatformFunctions.SetSharedState(glfw, _windowHandle, gl);
            }

            SetWindowHandle(_windowHandle);
            Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_CreateAppWindow: Window Initialized (Handle: {(long)_windowHandle:X})");
            return (IntPtr)_windowHandle;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] Exception in CMtrlSystm2ppSys_CreateAppWindow: {ex}");
            return IntPtr.Zero;
        }
    }
    
    /// <summary>
    /// Définit le titre de la fenêtre de l'application.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetAppWindowTitle(IntPtr self, IntPtr titlePtr)
    {
        if (_windowHandle == null || titlePtr == IntPtr.Zero) return;
        
        string? title = Marshal.PtrToStringUTF8(titlePtr);
        if (!string.IsNullOrEmpty(title) && _glfw != null)
        {
            _glfw.SetWindowTitle(_windowHandle, title);
            Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_SetAppWindowTitle: {title}");
        }
    }
    
    // Cache pour GetContentPath (évite les allocations répétées)
    private static IntPtr? _cachedContentPath = null;
    
    /// <summary>
    /// Obtient le chemin du contenu du jeu.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CMtrlSystm2ppSys_GetContentPath(IntPtr self)
    {
        if (_cachedContentPath == null)
        {
            string contentPath = Directory.GetCurrentDirectory();
            _cachedContentPath = Marshal.StringToHGlobalAnsi(contentPath);
        }
        return _cachedContentPath.Value;
    }
    
    // État du MaterialSystem
    private static string? _modGameSubdir = null;
    private static string? _moduleSearchPath = null;
    private static string? _defaultRenderSystemOption = null;
    private static bool _isInTestMode = false;
    private static bool _isInToolsMode = false;
    private static bool _isDedicatedServer = false;
    private static bool _isConsoleApp = false;
    private static bool _isGameApp = true; // Par défaut, c'est une app de jeu
    private static bool _isInDeveloperMode = false;
    private static bool _isInVRMode = false;
    private static bool _isRunningOnCustomerMachine = true; // Par défaut, on assume que c'est une machine client
    private static bool _isStandaloneApp = false;
    private static bool _modPathCheckDisabled = false;
    private static bool _appWindowDiscardMouseFocusClick = false;
    private static int _initializationPhase = 0;
    private static bool _suppressCOMInitialization = false;
    private static bool _suppressStartupManifestLoad = false;
    private static uint _steamAppId = 0;
    private static bool _sdlInitialized = false;
    
    /// <summary>
    /// Définit le sous-répertoire du mod (ex: "core").
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetModGameSubdir(IntPtr self, IntPtr subdirPtr)
    {
        if (subdirPtr == IntPtr.Zero) return;
        
        _modGameSubdir = Marshal.PtrToStringUTF8(subdirPtr);
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_SetModGameSubdir: {_modGameSubdir}");
    }
    
    /// <summary>
    /// Finalise la configuration du MaterialSystem après l'initialisation.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_InitFinishSetupMaterialSystem(IntPtr self)
    {
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_InitFinishSetupMaterialSystem");
        return 1; // Success
    }
    
    /// <summary>
    /// Supprime le chargement du manifeste de démarrage.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SuppressStartupManifestLoad(IntPtr self, int suppress)
    {
        _suppressStartupManifestLoad = suppress != 0;
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_SuppressStartupManifestLoad: {_suppressStartupManifestLoad}");
    }
    
    /// <summary>
    /// Supprime l'initialisation COM.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SuppressCOMInitialization(IntPtr self)
    {
        _suppressCOMInitialization = true;
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_SuppressCOMInitialization");
    }
    
    /// <summary>
    /// Active le mode test.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetInTestMode(IntPtr self)
    {
        _isInTestMode = true;
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_SetInTestMode");
    }
    
    /// <summary>
    /// Active le mode outils.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetInToolsMode(IntPtr self)
    {
        _isInToolsMode = true;
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_SetInToolsMode");
    }
    
    /// <summary>
    /// Définit l'ID de l'application Steam.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetSteamAppId(IntPtr self, uint appId)
    {
        _steamAppId = appId;
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_SetSteamAppId: {appId}");
    }
    
    /// <summary>
    /// Obtient l'ID de l'application Steam.
    /// </summary>
    [UnmanagedCallersOnly]
    public static uint CMtrlSystm2ppSys_GetSteamAppId(IntPtr self)
    {
        return _steamAppId;
    }
    
    /// <summary>
    /// Définit si l'application est un serveur dédié.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetDedicatedServer(IntPtr self, int isDedicated)
    {
        _isDedicatedServer = isDedicated != 0;
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_SetDedicatedServer: {_isDedicatedServer}");
    }
    
    /// <summary>
    /// Vérifie si l'application est un serveur dédié.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_IsDedicatedServer(IntPtr self)
    {
        return _isDedicatedServer ? 1 : 0;
    }
    
    /// <summary>
    /// Vérifie si l'application est une application console.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_IsConsoleApp(IntPtr self)
    {
        return _isConsoleApp ? 1 : 0;
    }
    
    /// <summary>
    /// Vérifie si l'application est une application de jeu.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_IsGameApp(IntPtr self)
    {
        return _isGameApp ? 1 : 0;
    }
    
    /// <summary>
    /// Obtient le sous-répertoire du mod.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CMtrlSystm2ppSys_GetModGameSubdir(IntPtr self)
    {
        if (string.IsNullOrEmpty(_modGameSubdir))
            return IntPtr.Zero;
        
        // Cache le résultat pour éviter les allocations répétées
        // Note: Dans une implémentation réelle, il faudrait gérer la libération de cette mémoire
        return Marshal.StringToHGlobalAnsi(_modGameSubdir);
    }
    
    /// <summary>
    /// Vérifie si l'application est en mode outils.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_IsInToolsMode(IntPtr self)
    {
        return _isInToolsMode ? 1 : 0;
    }
    
    /// <summary>
    /// Vérifie si l'application est en mode développeur.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_IsInDeveloperMode(IntPtr self)
    {
        return _isInDeveloperMode ? 1 : 0;
    }
    
    /// <summary>
    /// Vérifie si l'application est en mode VR.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_IsInVRMode(IntPtr self)
    {
        return _isInVRMode ? 1 : 0;
    }
    
    /// <summary>
    /// Vérifie si l'application s'exécute sur une machine client.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_IsRunningOnCustomerMachine(IntPtr self)
    {
        return _isRunningOnCustomerMachine ? 1 : 0;
    }
    
    /// <summary>
    /// Vérifie si l'application est en mode test.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_IsInTestMode(IntPtr self)
    {
        return _isInTestMode ? 1 : 0;
    }
    
    /// <summary>
    /// Obtient la phase d'initialisation actuelle.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_GetInitializationPhase(IntPtr self)
    {
        return _initializationPhase;
    }
    
    /// <summary>
    /// Vérifie si l'application est une application standalone.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_IsStandaloneApp(IntPtr self)
    {
        return _isStandaloneApp ? 1 : 0;
    }
    
    /// <summary>
    /// Initialise le MaterialSystem sans le système de matériaux complet.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_InitWithoutMaterialSystem(IntPtr self)
    {
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_InitWithoutMaterialSystem");
        return 1; // Success
    }
    
    /// <summary>
    /// Définit l'icône de la fenêtre de l'application.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetAppWindowIcon(IntPtr self, IntPtr iconPtr)
    {
        // Note: GLFW ne supporte pas directement le changement d'icône après création
        // Cette fonction est principalement un stub pour la compatibilité
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_SetAppWindowIcon");
    }
    
    /// <summary>
    /// Définit l'image initiale de la fenêtre de l'application.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetInitialAppWindowImage(IntPtr self, IntPtr imagePtr)
    {
        // Cette fonction définit une image à afficher avant le premier rendu
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_SetInitialAppWindowImage");
    }
    
    /// <summary>
    /// Définit si la fenêtre doit ignorer le focus de la souris au clic.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetAppWindowDiscardMouseFocusClick(IntPtr self, int discard)
    {
        _appWindowDiscardMouseFocusClick = discard != 0;
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_SetAppWindowDiscardMouseFocusClick: {_appWindowDiscardMouseFocusClick}");
    }
    
    /// <summary>
    /// Dessine l'image initiale de la fenêtre.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_DrawInitialWindowImage(IntPtr self)
    {
        // Cette fonction dessine l'image initiale définie par SetInitialAppWindowImage
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_DrawInitialWindowImage");
    }
    
    /// <summary>
    /// Définit le chemin de recherche des modules.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetModuleSearchPath(IntPtr self, IntPtr pathPtr)
    {
        if (pathPtr == IntPtr.Zero) return;
        
        _moduleSearchPath = Marshal.PtrToStringUTF8(pathPtr);
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_SetModuleSearchPath: {_moduleSearchPath}");
    }
    
    /// <summary>
    /// Définit le mod depuis un nom de fichier.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetModFromFileName(IntPtr self, IntPtr fileNamePtr, int flags)
    {
        if (fileNamePtr == IntPtr.Zero) return;
        
        string? fileName = Marshal.PtrToStringUTF8(fileNamePtr);
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_SetModFromFileName: {fileName}, flags={flags}");
        // Note: Cette fonction pourrait extraire le nom du mod depuis le nom de fichier
    }
    
    /// <summary>
    /// Désactive la vérification du chemin du mod.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_DisableModPathCheck(IntPtr self)
    {
        _modPathCheckDisabled = true;
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_DisableModPathCheck");
    }
    
    /// <summary>
    /// Définit l'option par défaut du système de rendu (ex: "-vulkan", "-opengl").
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetDefaultRenderSystemOption(IntPtr self, IntPtr optionPtr)
    {
        if (optionPtr == IntPtr.Zero) return;
        
        _defaultRenderSystemOption = Marshal.PtrToStringUTF8(optionPtr);
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_SetDefaultRenderSystemOption: {_defaultRenderSystemOption}");
    }
    
    /// <summary>
    /// Définit la phase d'initialisation.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetInitializationPhase(IntPtr self, int phase)
    {
        _initializationPhase = phase;
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_SetInitializationPhase: {phase}");
    }
    
    /// <summary>
    /// Préparation avant l'arrêt du MaterialSystem.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_PreShutdown(IntPtr self)
    {
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_PreShutdown");
        // Nettoyer les ressources si nécessaire
    }
    
    /// <summary>
    /// Initialise SDL pour le MaterialSystem.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_InitSDL(IntPtr self, uint flags)
    {
        // Note: Sur Linux, on utilise GLFW au lieu de SDL
        // Cette fonction est principalement un stub pour la compatibilité
        _sdlInitialized = true;
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_InitSDL: flags={flags}");
        return 1; // Success
    }
    
    /// <summary>
    /// Arrête SDL pour le MaterialSystem.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_ShutdownSDL(IntPtr self)
    {
        _sdlInitialized = false;
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_ShutdownSDL");
    }
    
    /// <summary>
    /// Ajoute un système au MaterialSystem.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CMtrlSystm2ppSys_AddSystem(IntPtr self, IntPtr moduleNamePtr, IntPtr systemNamePtr)
    {
        if (moduleNamePtr == IntPtr.Zero || systemNamePtr == IntPtr.Zero)
            return IntPtr.Zero;
        
        string? moduleName = Marshal.PtrToStringUTF8(moduleNamePtr);
        string? systemName = Marshal.PtrToStringUTF8(systemNamePtr);
        Console.WriteLine($"[NativeAOT] CMtrlSystm2ppSys_AddSystem: module={moduleName}, system={systemName}");
        
        // Retourner un pointeur vers le système ajouté (stub pour l'instant)
        return (IntPtr)1;
    }
    
    /// <summary>
    /// Active le mode standalone.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CMtrlSystm2ppSys_SetInStandaloneApp(IntPtr self)
    {
        _isStandaloneApp = true;
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_SetInStandaloneApp");
    }
    
    // ========== g_pMtrlSystm2 Functions ==========
    
    /// <summary>
    /// Crée un matériau brut (raw material).
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr g_pMtrlSystm2_CreateRawMaterial(IntPtr materialNamePtr, IntPtr shaderPtr, int anonymous)
    {
        string? materialName = Marshal.PtrToStringUTF8(materialNamePtr);
        string? shader = Marshal.PtrToStringUTF8(shaderPtr);
        
        // Create render attributes for this material
        IntPtr renderAttributes = RenderAttributes.RenderAttributes.CreateRenderAttributesInternal();
        
        var materialData = new MaterialData
        {
            Name = materialName ?? "",
            Shader = shader ?? "",
            Anonymous = anonymous != 0,
            RenderAttributes = renderAttributes
        };
        
        // Enregistrer MaterialData dans HandleManager pour obtenir un handle unique
        int handle = HandleManager.Register(materialData);
        if (handle == 0) return IntPtr.Zero;
        
        int bindingHandle = HandleManager.GetBindingHandle(handle);
        materialData.BindingPtr = (IntPtr)bindingHandle;
        
        // Créer un mode de rendu par défaut (on utilise aussi HandleManager pour la cohérence)
        int modeHandle = HandleManager.Register(new object()); // Objet simple pour représenter le mode
        materialData.MaterialMode = (IntPtr)modeHandle;
        
        // Enregistrer dans l'index Name si le nom est fourni
        if (!string.IsNullOrEmpty(materialName))
        {
            HandleManager.RegisterNameIndex(materialName, bindingHandle);
        }
        
        Console.WriteLine($"[NativeAOT] g_pMtrlSystm2_CreateRawMaterial: name={materialName}, shader={shader}, handle={handle}");
        
        return (IntPtr)handle;
    }
    
    // ========== IMaterial2 Functions ==========
    
    /// <summary>
    /// Retourne les attributs de rendu d'un matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_GetRenderAttributes(IntPtr self)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        // Get the material data and return its render attributes
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            if (materialData.RenderAttributes != IntPtr.Zero)
                return materialData.RenderAttributes;
        }
        
        // If material not found or attributes not set, create new ones
        // This shouldn't happen in normal flow, but handle gracefully
        return RenderAttributes.RenderAttributes.CreateRenderAttributesInternal();
    }
    
    /// <summary>
    /// Obtient le nom d'un matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_GetName(IntPtr self)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Cache le nom en mémoire non managée pour éviter les allocations répétées
            // Note: Dans une implémentation réelle, il faudrait gérer la libération de cette mémoire
            return Marshal.StringToHGlobalAnsi(materialData.Name);
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Obtient le mode d'un matériau pour un token donné.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_GetMode(IntPtr self, Sandbox.StringToken token)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Retourner le mode du matériau (ou créer un mode par défaut si nécessaire)
            if (materialData.MaterialMode == IntPtr.Zero)
            {
                // Créer un mode par défaut via HandleManager
                int modeHandle = HandleManager.Register(new object());
                materialData.MaterialMode = (IntPtr)modeHandle;
            }
            return materialData.MaterialMode;
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Définit une valeur de paramètre de matériau (string).
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IMaterial2_Set(IntPtr self, IntPtr namePtr, IntPtr valuePtr)
    {
        if (self == IntPtr.Zero || namePtr == IntPtr.Zero || valuePtr == IntPtr.Zero) return;
        
        string? name = Marshal.PtrToStringUTF8(namePtr);
        string? value = Marshal.PtrToStringUTF8(valuePtr);
        
        if (string.IsNullOrEmpty(name)) return;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Stocker la valeur dans les render attributes
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                RenderAttributes.RenderAttributes.SetStringValueHelper(materialData.RenderAttributes, name, value ?? "");
            }
            
            // Stocker aussi dans les paramètres du matériau
            materialData.Parameters[name] = value ?? "";
        }
    }
    
    // ========== g_pMtrlSystm2 Functions supplémentaires ==========
    
    /// <summary>
    /// Crée une copie procédurale d'un matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr g_pMtrlSystm2_CreateProceduralMaterialCopy(IntPtr hSrcMaterial, int nResourceType, int bRecreateStaticBuffers)
    {
        if (hSrcMaterial == IntPtr.Zero) return IntPtr.Zero;
        
        var srcMaterial = HandleManager.Get<MaterialData>((int)hSrcMaterial);
        if (srcMaterial != null)
        {
            // Créer de nouveaux render attributes
            IntPtr renderAttributes = RenderAttributes.RenderAttributes.CreateRenderAttributesInternal();
            
            var newMaterial = new MaterialData
            {
                Name = srcMaterial.Name + "_copy",
                Shader = srcMaterial.Shader,
                Anonymous = srcMaterial.Anonymous,
                RenderAttributes = renderAttributes,
                IsLoaded = srcMaterial.IsLoaded,
                IsError = srcMaterial.IsError,
                SimilarityKey = srcMaterial.SimilarityKey
            };
            
            // Enregistrer le nouveau MaterialData dans HandleManager pour obtenir un handle unique
            int newHandle = HandleManager.Register(newMaterial);
            if (newHandle == 0) return IntPtr.Zero;
            
            int newBindingHandle = HandleManager.GetBindingHandle(newHandle);
            newMaterial.BindingPtr = (IntPtr)newBindingHandle;
            
            // Créer un nouveau mode de rendu pour la copie
            int newModeHandle = HandleManager.Register(new object());
            newMaterial.MaterialMode = (IntPtr)newModeHandle;
            
            // Copier les paramètres
            foreach (var kvp in srcMaterial.Parameters)
            {
                newMaterial.Parameters[kvp.Key] = kvp.Value;
            }
            
            // Enregistrer dans l'index Name si le nom est fourni
            if (!string.IsNullOrEmpty(newMaterial.Name))
            {
                HandleManager.RegisterNameIndex(newMaterial.Name, newBindingHandle);
            }
            
            Console.WriteLine($"[NativeAOT] g_pMtrlSystm2_CreateProceduralMaterialCopy: src={hSrcMaterial}, new={newHandle}");
            return (IntPtr)newHandle;
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Trouve ou crée un matériau depuis une ressource.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr g_pMtrlSystm2_FindOrCreateMaterialFromResource(IntPtr pResourceName)
    {
        if (pResourceName == IntPtr.Zero) return IntPtr.Zero;
        
        string? resourceName = Marshal.PtrToStringUTF8(pResourceName);
        if (string.IsNullOrEmpty(resourceName)) return IntPtr.Zero;
        
        return FindOrCreateMaterialFromResourceHelper(resourceName);
    }
    
    /// <summary>
    /// Helper pour trouver ou créer un matériau depuis une ressource (appelable depuis le code managé).
    /// Utilisé par EngineGlue.Glue_Resources_GetMaterial.
    /// </summary>
    public static IntPtr FindOrCreateMaterialFromResourceHelper(string resourceName)
    {
        if (string.IsNullOrEmpty(resourceName)) return IntPtr.Zero;
        
        // Chercher un matériau existant avec ce nom (O(1) via index)
        var existingMaterial = HandleManager.FindByName<MaterialData>(resourceName);
        if (existingMaterial != null && existingMaterial.BindingPtr != IntPtr.Zero)
        {
            // Trouver un handle existant pour ce matériau via le BindingHandle
            int existingBindingHandle = (int)existingMaterial.BindingPtr;
            int existingHandle = HandleManager.GetHandleByBindingHandle(existingBindingHandle);
            if (existingHandle != 0)
            {
                // Obtenir tous les handles pour ce matériau et retourner le premier
                var allHandles = HandleManager.GetAllHandles(existingHandle);
                if (allHandles.Length > 0)
                {
                    Console.WriteLine($"[NativeAOT] FindOrCreateMaterialFromResourceHelper: found existing {resourceName}");
                    return (IntPtr)allHandles[0];
                }
            }
        }
        
        // Créer un nouveau matériau
        IntPtr renderAttributes = RenderAttributes.RenderAttributes.CreateRenderAttributesInternal();
        
        var materialData = new MaterialData
        {
            Name = resourceName,
            Shader = "",
            Anonymous = false,
            RenderAttributes = renderAttributes
        };
        
        // Enregistrer MaterialData dans HandleManager pour obtenir un handle unique
        int handle = HandleManager.Register(materialData);
        if (handle == 0) return IntPtr.Zero;
        
        int bindingHandle = HandleManager.GetBindingHandle(handle);
        materialData.BindingPtr = (IntPtr)bindingHandle;
        
        // Créer un mode de rendu par défaut
        int modeHandle = HandleManager.Register(new object());
        materialData.MaterialMode = (IntPtr)modeHandle;
        
        // Enregistrer dans l'index Name pour recherche O(1)
        HandleManager.RegisterNameIndex(resourceName, bindingHandle);
        
        Console.WriteLine($"[NativeAOT] FindOrCreateMaterialFromResourceHelper: created new {resourceName}, handle={handle}");
        return (IntPtr)handle;
    }
    
    /// <summary>
    /// Mise à jour par frame du MaterialSystem.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pMtrlSystm2_FrameUpdate()
    {
        // Mise à jour par frame (pour l'instant, rien à faire)
        // Cette fonction pourrait être utilisée pour nettoyer les ressources ou mettre à jour les états
    }
    
    // ========== IMaterial2 Functions supplémentaires ==========
    
    /// <summary>
    /// Détruit un handle fort de matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IMaterial2_DestroyStrongHandle(IntPtr self)
    {
        if (self == IntPtr.Zero) return;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Libérer le MaterialMode depuis HandleManager
            if (materialData.MaterialMode != IntPtr.Zero)
            {
                HandleManager.Unregister((int)materialData.MaterialMode);
            }
            
            // Nettoyer les index secondaires avant Unregister
            if (!string.IsNullOrEmpty(materialData.Name))
            {
                HandleManager.UnindexName(materialData.Name);
            }
        }
        
        HandleManager.Unregister((int)self);
        Console.WriteLine($"[NativeAOT] IMaterial2_DestroyStrongHandle: {self}");
    }
    
    /// <summary>
    /// Vérifie si un handle fort de matériau est valide.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IMaterial2_IsStrongHandleValid(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        return HandleManager.Exists((int)self) ? 1 : 0;
    }
    
    /// <summary>
    /// Vérifie si un matériau est en erreur.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IMaterial2_IsError(IntPtr self)
    {
        if (self == IntPtr.Zero) return 1; // Null = erreur
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            return materialData.IsError ? 1 : 0;
        }
        
        return 1; // Matériau non trouvé = erreur
    }
    
    /// <summary>
    /// Vérifie si un handle fort de matériau est chargé.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IMaterial2_IsStrongHandleLoaded(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            return materialData.IsLoaded ? 1 : 0;
        }
        
        return 0;
    }
    
    /// <summary>
    /// Copie un handle fort de matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_CopyStrongHandle(IntPtr self)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        int newHandle = HandleManager.CopyHandle((int)self);
        if (newHandle != 0)
        {
            Console.WriteLine($"[NativeAOT] IMaterial2_CopyStrongHandle: {self} -> {newHandle} (refs={HandleManager.GetReferenceCount((int)self)})");
            return (IntPtr)newHandle;
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Obtient le pointeur de binding d'un matériau.
    /// Le binding pointer est un identifiant unique qui identifie le matériau natif,
    /// utilisé pour comparer si deux handles pointent vers le même matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_GetBindingPtr(IntPtr self)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        int bindingHandle = HandleManager.GetBindingHandle((int)self);
        return bindingHandle != 0 ? (IntPtr)bindingHandle : IntPtr.Zero;
    }
    
    /// <summary>
    /// Obtient le nom d'un matériau avec le mod.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_GetNameWithMod(IntPtr self)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Ajouter le préfixe du mod si disponible
            string nameWithMod = materialData.Name;
            if (!string.IsNullOrEmpty(_modGameSubdir))
            {
                nameWithMod = $"{_modGameSubdir}/{nameWithMod}";
            }
            
            return Marshal.StringToHGlobalAnsi(nameWithMod);
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Obtient la clé de similarité d'un matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static ulong IMaterial2_GetSimilarityKey(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            if (materialData.SimilarityKey == 0)
            {
                // Générer une clé de similarité basée sur le nom et le shader
                // Utilise MurmurHash2 pour garantir la compatibilité avec Source 2
                string keyString = $"{materialData.Name}:{materialData.Shader}";
                materialData.SimilarityKey = HashUtils.MurmurHash2(keyString, lowercase: true);
            }
            return materialData.SimilarityKey;
        }
        
        return 0;
    }
    
    /// <summary>
    /// Vérifie si un matériau est chargé.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IMaterial2_IsLoaded(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            return materialData.IsLoaded ? 1 : 0;
        }
        
        return 0;
    }
    
    /// <summary>
    /// Obtient le mode d'un matériau (sans paramètre).
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_GetMode_1(IntPtr self)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Retourner le mode du matériau (ou créer un mode par défaut si nécessaire)
            if (materialData.MaterialMode == IntPtr.Zero)
            {
                // Créer un mode par défaut via HandleManager
                int modeHandle = HandleManager.Register(new object());
                materialData.MaterialMode = (IntPtr)modeHandle;
            }
            return materialData.MaterialMode;
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Obtient le mode d'un matériau pour une couche de scène donnée.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_GetMode_2(IntPtr self, IntPtr layer)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Pour l'instant, retourner le même mode pour toutes les couches
            // Dans une implémentation complète, on pourrait avoir des modes différents par couche
            if (materialData.MaterialMode == IntPtr.Zero)
            {
                // Créer un mode par défaut via HandleManager
                int modeHandle = HandleManager.Register(new object());
                materialData.MaterialMode = (IntPtr)modeHandle;
            }
            return materialData.MaterialMode;
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Recrée tous les buffers statiques constants et de commandes.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IMaterial2_RecreateAllStaticConstantAndCommandBuffers(IntPtr self)
    {
        // Pour l'instant, rien à faire car on n'utilise pas de buffers statiques
        // Dans une implémentation complète, on recréerait les buffers GPU
    }
    
    /// <summary>
    /// Obtient le premier attribut de texture d'un matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_GetFirstTextureAttribute(IntPtr self)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                return RenderAttributes.RenderAttributes.GetFirstTextureAttributeHelper(materialData.RenderAttributes);
            }
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Obtient un attribut booléen d'un matériau avec valeur par défaut.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IMaterial2_GetBoolAttributeOrDefault(IntPtr self, Sandbox.StringToken name, int defaultValue)
    {
        if (self == IntPtr.Zero) return defaultValue;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                string? nameStr = Sandbox.StringToken.GetValue(name.Value);
                if (!string.IsNullOrEmpty(nameStr))
                {
                    bool value = RenderAttributes.RenderAttributes.GetBoolValueHelper(materialData.RenderAttributes, nameStr, defaultValue != 0);
                    return value ? 1 : 0;
                }
            }
        }
        
        return defaultValue;
    }
    
    /// <summary>
    /// Obtient un attribut entier d'un matériau avec valeur par défaut.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IMaterial2_GetIntAttributeOrDefault(IntPtr self, Sandbox.StringToken name, int defaultValue)
    {
        if (self == IntPtr.Zero) return defaultValue;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                string? nameStr = Sandbox.StringToken.GetValue(name.Value);
                if (!string.IsNullOrEmpty(nameStr))
                {
                    return RenderAttributes.RenderAttributes.GetIntValueHelper(materialData.RenderAttributes, nameStr, defaultValue);
                }
            }
        }
        
        return defaultValue;
    }
    
    /// <summary>
    /// Obtient un attribut float d'un matériau avec valeur par défaut.
    /// </summary>
    [UnmanagedCallersOnly]
    public static float IMaterial2_GetFloatAttributeOrDefault(IntPtr self, Sandbox.StringToken name, float defaultValue)
    {
        if (self == IntPtr.Zero) return defaultValue;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                string? nameStr = Sandbox.StringToken.GetValue(name.Value);
                if (!string.IsNullOrEmpty(nameStr))
                {
                    return RenderAttributes.RenderAttributes.GetFloatValueHelper(materialData.RenderAttributes, nameStr, defaultValue);
                }
            }
        }
        
        return defaultValue;
    }
    
    /// <summary>
    /// Obtient un attribut de texture d'un matériau avec valeur par défaut.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_GetTextureAttributeOrDefault(IntPtr self, Sandbox.StringToken name)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                string? nameStr = Sandbox.StringToken.GetValue(name.Value);
                if (!string.IsNullOrEmpty(nameStr))
                {
                    return RenderAttributes.RenderAttributes.GetTextureValueHelper(materialData.RenderAttributes, nameStr, IntPtr.Zero);
                }
            }
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Vérifie si un matériau a un paramètre donné.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IMaterial2_HasParam(IntPtr self, IntPtr namePtr)
    {
        if (self == IntPtr.Zero || namePtr == IntPtr.Zero) return 0;
        
        string? name = Marshal.PtrToStringUTF8(namePtr);
        if (string.IsNullOrEmpty(name)) return 0;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Vérifier dans les paramètres
            if (materialData.Parameters.ContainsKey(name))
                return 1;
            
            // Vérifier aussi dans les render attributes
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                if (RenderAttributes.RenderAttributes.HasParamHelper(materialData.RenderAttributes, name))
                    return 1;
            }
        }
        
        return 0;
    }
    
    /// <summary>
    /// Obtient une valeur de paramètre string d'un matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_GetString(IntPtr self, IntPtr namePtr, IntPtr defaultValuePtr)
    {
        if (self == IntPtr.Zero || namePtr == IntPtr.Zero) return defaultValuePtr;
        
        string? name = Marshal.PtrToStringUTF8(namePtr);
        if (string.IsNullOrEmpty(name)) return defaultValuePtr;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Chercher dans les paramètres
            if (materialData.Parameters.TryGetValue(name, out var value) && value is string strValue)
            {
                // Allouer et retourner la valeur
                return Marshal.StringToHGlobalAnsi(strValue);
            }
            
            // Chercher aussi dans les render attributes
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                string? attrValue = RenderAttributes.RenderAttributes.GetStringValueHelper(materialData.RenderAttributes, name);
                if (!string.IsNullOrEmpty(attrValue))
                {
                    return Marshal.StringToHGlobalAnsi(attrValue);
                }
            }
        }
        
        return defaultValuePtr;
    }
    
    /// <summary>
    /// Définit une valeur de paramètre Vector4 d'un matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IMaterial2_Set_1(IntPtr self, IntPtr namePtr, Vector4* value)
    {
        if (self == IntPtr.Zero || namePtr == IntPtr.Zero || value == null) return;
        
        string? name = Marshal.PtrToStringUTF8(namePtr);
        if (string.IsNullOrEmpty(name)) return;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Stocker dans les paramètres
            materialData.Parameters[name] = *value;
            
            // Stocker aussi dans les render attributes
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                RenderAttributes.RenderAttributes.SetVector4DValueHelper(materialData.RenderAttributes, name, *value);
            }
        }
    }
    
    /// <summary>
    /// Obtient une valeur de paramètre Vector4 d'un matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static Vector4 IMaterial2_GetVector4(IntPtr self, IntPtr namePtr)
    {
        if (self == IntPtr.Zero || namePtr == IntPtr.Zero) return default;
        
        string? name = Marshal.PtrToStringUTF8(namePtr);
        if (string.IsNullOrEmpty(name)) return default;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Chercher dans les paramètres
            if (materialData.Parameters.TryGetValue(name, out var value))
            {
                if (value is Vector4 vec4)
                    return vec4;
            }
            
            // Chercher aussi dans les render attributes
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                return RenderAttributes.RenderAttributes.GetVector4DValueHelper(materialData.RenderAttributes, name, default);
            }
        }
        
        return default;
    }
    
    /// <summary>
    /// Définit une valeur de paramètre texture d'un matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IMaterial2_Set_2(IntPtr self, IntPtr namePtr, IntPtr texture)
    {
        if (self == IntPtr.Zero || namePtr == IntPtr.Zero) return;
        
        string? name = Marshal.PtrToStringUTF8(namePtr);
        if (string.IsNullOrEmpty(name)) return;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Stocker dans les paramètres
            materialData.Parameters[name] = texture;
            
            // Stocker aussi dans les render attributes
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                RenderAttributes.RenderAttributes.SetTextureValueHelper(materialData.RenderAttributes, name, texture);
            }
        }
    }
    
    /// <summary>
    /// Obtient une valeur de paramètre texture d'un matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr IMaterial2_GetTexture(IntPtr self, IntPtr namePtr)
    {
        if (self == IntPtr.Zero || namePtr == IntPtr.Zero) return IntPtr.Zero;
        
        string? name = Marshal.PtrToStringUTF8(namePtr);
        if (string.IsNullOrEmpty(name)) return IntPtr.Zero;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            // Chercher dans les paramètres
            if (materialData.Parameters.TryGetValue(name, out var value))
            {
                if (value is IntPtr texturePtr)
                    return texturePtr;
            }
            
            // Chercher aussi dans les render attributes
            if (materialData.RenderAttributes != IntPtr.Zero)
            {
                return RenderAttributes.RenderAttributes.GetTextureValueHelper(materialData.RenderAttributes, name, IntPtr.Zero);
            }
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Définit si un matériau est édité.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IMaterial2_SetEdited(IntPtr self, int edited)
    {
        if (self == IntPtr.Zero) return;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            materialData.IsEdited = edited != 0;
        }
    }
    
    /// <summary>
    /// Vérifie si un matériau est édité.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int IMaterial2_IsEdited(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        
        var materialData = HandleManager.Get<MaterialData>((int)self);
        if (materialData != null)
        {
            return materialData.IsEdited ? 1 : 0;
        }
        
        return 0;
    }
    
    /// <summary>
    /// Recharge les combos statiques d'un matériau.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void IMaterial2_ReloadStaticCombos(IntPtr self)
    {
        // Pour l'instant, rien à faire car on n'utilise pas de combos statiques
        // Dans une implémentation complète, on rechargerait les combinaisons de shaders statiques
    }
}

