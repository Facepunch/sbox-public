using System;
using System.IO;
using System.Runtime.InteropServices;
using Silk.NET.GLFW;
using Silk.NET.OpenGL;

namespace Sandbox.Engine.Emulation.Platform;

/// <summary>
/// Module d'émulation pour les fonctions Platform (Plat_*, GetGameRootFolder, SourceEngine*).
/// Gère les fonctions système de plateforme Linux.
/// </summary>
public static unsafe class PlatformFunctions
{
    // État global partagé avec EngineExports (sera initialisé depuis EngineExports)
    private static Glfw? _glfw;
    private static WindowHandle* _windowHandle;
    private static GL? _gl;
    
    // Cache pour GetGameRootFolder (évite les allocations répétées)
    private static IntPtr? _cachedGameRootFolder = null;
    
    // Cache pour le nom du module (chemin vers l'exécutable)
    private static string? _moduleFilename = null;
    private static string? _moduleDirectory = null;
    
    /// <summary>
    /// Obtient le répertoire du module (déduit du module filename).
    /// </summary>
    public static string? GetModuleDirectory()
    {
        if (_moduleDirectory != null) return _moduleDirectory;
        
        if (!string.IsNullOrEmpty(_moduleFilename))
        {
            try
            {
                _moduleDirectory = Path.GetDirectoryName(_moduleFilename);
                return _moduleDirectory;
            }
            catch
            {
                // Si le parsing échoue, utiliser le répertoire courant
                _moduleDirectory = Directory.GetCurrentDirectory();
                return _moduleDirectory;
            }
        }
        
        return Directory.GetCurrentDirectory();
    }
    
    /// <summary>
    /// Initialise les références partagées depuis EngineExports.
    /// </summary>
    internal static void SetSharedState(Glfw? glfw, WindowHandle* windowHandle, GL? gl)
    {
        _glfw = glfw;
        _windowHandle = windowHandle;
        _gl = gl;
    }
    
    /// <summary>
    /// Obtient l'instance OpenGL (pour utilisation par EngineExports et autres modules).
    /// </summary>
    public static GL? GetGL()
    {
        return _gl;
    }
    
    /// <summary>
    /// Obtient l'instance GLFW (pour utilisation par EngineExports et autres modules).
    /// </summary>
    public static Glfw? GetGlfw()
    {
        return _glfw;
    }
    
    /// <summary>
    /// Obtient le handle de la fenêtre (pour utilisation par EngineExports et autres modules).
    /// </summary>
    public static WindowHandle* GetWindowHandle()
    {
        return _windowHandle;
    }
    
    /// <summary>
    /// Initialise les fonctions natives de Platform.
    /// </summary>
    public static void Init(void** native)
    {
        // global_Plat_* functions (indices 1569-1599)
        // Note: Les signatures exactes viennent de Interop.Engine.cs lignes 16316-16346
        native[1569] = (void*)(delegate* unmanaged<IntPtr, int*, int*, void>)&global_Plat_ScreenToWindowCoords;
        native[1570] = (void*)(delegate* unmanaged<IntPtr, int*, int*, void>)&global_Plat_WindowToScreenCoords;
        native[1571] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&Plat_MessageBox;
        native[1572] = (void*)(delegate* unmanaged<int, int*, int*, uint*, int>)&global_Plat_GetDesktopResolution;
        native[1573] = (void*)(delegate* unmanaged<int>)&global_Plat_GetDefaultMonitorIndex;
        native[1574] = (void*)(delegate* unmanaged<IntPtr, int>)&global_Plat_SafeRemoveFile;
        native[1575] = (void*)(delegate* unmanaged<IntPtr, void>)&global_Plat_SetModuleFilename;
        native[1576] = (void*)(delegate* unmanaged<IntPtr, void>)&global_Plat_SetCurrentDirectory;
        native[1577] = (void*)(delegate* unmanaged<ulong>)&global_Plat_GetCurrentFrame;
        native[1578] = (void*)(delegate* unmanaged<ulong, void>)&Plat_SetCurrentFrame;
        native[1579] = (void*)(delegate* unmanaged<long, void>)&global_Plat_ChangeCurrentFrame;
        native[1580] = (void*)(delegate* unmanaged<int>)&global_Plat_IsRunningOnCustomerMachine;
        native[1581] = (void*)(delegate* unmanaged<int>)&global_Plat_HasClipboardText;
        native[1582] = (void*)(delegate* unmanaged<IntPtr, void>)&global_Plat_SetClipboardText;
        native[1583] = (void*)(delegate* unmanaged<IntPtr>)&global_Plat_GetClipboardText;
        native[1584] = (void*)(delegate* unmanaged<void>)&global_Plat_ClearClipboardText;
        native[1585] = (void*)(delegate* unmanaged<int>)&global_IsWindowFocused;
        native[1586] = (void*)(delegate* unmanaged<int>)&global_IsRetail;
        native[1587] = (void*)(delegate* unmanaged<IntPtr, int>)&global_HasLaunchParameter;
        native[1588] = (void*)(delegate* unmanaged<void>)&global_Plat_SetNoAssert;
        
        // global_GetGameRootFolder (index 1589)
        native[1589] = (void*)(delegate* unmanaged<IntPtr>)&GetGameRootFolder;
        native[1590] = (void*)(delegate* unmanaged<IntPtr>)&global_GetGameSearchPath;
        native[1591] = (void*)(delegate* unmanaged<int>)&global_SourceEngineUnitTestInit;
        
        // global_SourceEngine* functions (indices 1592-1596)
        native[1592] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int>)&SourceEnginePreInit;
        native[1593] = (void*)(delegate* unmanaged<IntPtr, int>)&SourceEngineInit;
        // SourceEngineFrame reste dans EngineExports.cs car il a une logique de rendu complexe
        // native[1594] sera patché depuis EngineExports.cs
        native[1595] = (void*)(delegate* unmanaged<IntPtr, int, void>)&global_SourceEngineShutdown;
        native[1596] = (void*)(delegate* unmanaged<void>)&UpdateWindowSize;
        native[1597] = (void*)(delegate* unmanaged<float>)&global_GetDiagonalDpi;
        native[1598] = (void*)(delegate* unmanaged<int>)&global_AppIsDedicatedServer;
        native[1599] = (void*)(delegate* unmanaged<void>)&global_ToolsStallMonitor_IndicateActivity;
        
        // TODO: Ajouter les autres fonctions Platform selon le plan
        // Voir plan.md section "Inventaire Complet des Stubs par Module > Platform" (27 fonctions)
    }
    
    // ========== global_Plat_* Functions ==========
    
    /// <summary>
    /// Affiche une boîte de dialogue de message.
    /// Signature: delegate* unmanaged&lt; IntPtr, IntPtr, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void Plat_MessageBox(IntPtr title, IntPtr message)
    {
        string? t = Marshal.PtrToStringUTF8(title);
        string? m = Marshal.PtrToStringUTF8(message);
        Console.WriteLine($"[NativeAOT MESSAGEBOX] {t}: {m}");
    }
    
    /// <summary>
    /// Définit le nom de fichier du module (chemin vers l'exécutable).
    /// Cette fonction est appelée au démarrage pour définir le chemin vers l'exécutable (sbox ou sbox.exe).
    /// </summary>
    [UnmanagedCallersOnly]
    public static void global_Plat_SetModuleFilename(IntPtr filename)
    {
        if (filename == IntPtr.Zero) return;
        
        _moduleFilename = Marshal.PtrToStringUTF8(filename);
        
        // Réinitialiser le cache du répertoire du module
        _moduleDirectory = null;
        
        // Si le module filename est défini, mettre à jour le répertoire courant si nécessaire
        if (!string.IsNullOrEmpty(_moduleFilename))
        {
            try
            {
                string? moduleDir = Path.GetDirectoryName(_moduleFilename);
                if (!string.IsNullOrEmpty(moduleDir) && Directory.Exists(moduleDir))
                {
                    // Ne pas changer le répertoire courant automatiquement, mais le stocker
                    _moduleDirectory = moduleDir;
                }
            }
            catch
            {
                // Ignorer les erreurs de parsing
            }
        }
        
        Console.WriteLine($"[NativeAOT] global_Plat_SetModuleFilename: {_moduleFilename}");
    }
    
    /// <summary>
    /// Définit le répertoire courant.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void global_Plat_SetCurrentDirectory(IntPtr filename)
    {
        if (filename == IntPtr.Zero) return;
        
        string? path = Marshal.PtrToStringUTF8(filename);
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
        {
            Directory.SetCurrentDirectory(path);
            Console.WriteLine($"[NativeAOT] global_Plat_SetCurrentDirectory: {path}");
        }
        else
        {
            Console.WriteLine($"[NativeAOT] WARNING: global_Plat_SetCurrentDirectory: path does not exist: {path}");
        }
    }
    
    private static ulong _currentFrame = 0;
    
    /// <summary>
    /// Obtient le numéro de frame actuel.
    /// </summary>
    [UnmanagedCallersOnly]
    public static ulong global_Plat_GetCurrentFrame()
    {
        return _currentFrame;
    }
    
    /// <summary>
    /// Définit le numéro de frame actuel.
    /// Signature: delegate* unmanaged&lt; ulong, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void Plat_SetCurrentFrame(ulong frame)
    {
        _currentFrame = frame;
    }
    
    // ========== global_GetGameRootFolder ==========
    
    /// <summary>
    /// Obtient le répertoire racine du jeu.
    /// Utilise le répertoire du module si disponible, sinon le répertoire courant.
    /// Signature: delegate* unmanaged&lt; IntPtr &gt; (retourne IntPtr, pas void*)
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr GetGameRootFolder()
    {
        if (_cachedGameRootFolder == null)
        {
            // Essayer d'utiliser le répertoire du module d'abord
            string? rootFolder = GetModuleDirectory();
            
            // Si le module directory n'est pas disponible, utiliser le répertoire courant
            if (string.IsNullOrEmpty(rootFolder) || !Directory.Exists(rootFolder))
            {
                rootFolder = Directory.GetCurrentDirectory();
            }
            
            _cachedGameRootFolder = Marshal.StringToHGlobalAnsi(rootFolder);
        }
        return _cachedGameRootFolder.Value;
    }
    
    // ========== global_SourceEngine* Functions ==========
    
    /// <summary>
    /// Initialisation préalable du moteur Source.
    /// Cette fonction est appelée avant SourceEngineInit pour préparer l'environnement.
    /// Signature: delegate* unmanaged&lt; IntPtr, IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int SourceEnginePreInit(IntPtr lpCmdLine, IntPtr appDict)
    {
        Console.WriteLine("[NativeAOT] SourceEnginePreInit");
        
        // Réinitialiser le cache du répertoire racine au cas où le module filename aurait changé
        if (_cachedGameRootFolder != null)
        {
            Marshal.FreeHGlobal(_cachedGameRootFolder.Value);
            _cachedGameRootFolder = null;
        }
        
        return 1; // Success
    }
    
    /// <summary>
    /// Initialise le moteur Source.
    /// Cette fonction est appelée après SourceEnginePreInit pour initialiser le moteur.
    /// Initialise OpenGL ici pour que les textures puissent être créées dans InitStaticTextures().
    /// Signature: delegate* unmanaged&lt; IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int SourceEngineInit(IntPtr appDict)
    {
        Console.WriteLine("[NativeAOT] SourceEngineInit");
        
        // Initialiser OpenGL ici pour que FindOrCreateTexture2 puisse fonctionner dans InitStaticTextures()
        // La fenêtre sera créée plus tard par CMtrlSystm2ppSys_CreateAppWindow, mais on initialise OpenGL maintenant
        try
        {
            if (_glfw == null)
            {
                _glfw = Glfw.GetApi();
                if (!_glfw.Init())
                {
                    Console.WriteLine("[NativeAOT] SourceEngineInit: GLFW Init failed!");
                    return 0; // Failure
                }
            }
            
            // Créer une fenêtre invisible pour avoir un contexte OpenGL
            // Cette fenêtre sera remplacée plus tard par CMtrlSystm2ppSys_CreateAppWindow
            if (_windowHandle == null)
            {
                _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGL);
                _glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
                _glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
                _glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
                _glfw.WindowHint(WindowHintBool.Visible, false); // Fenêtre invisible
                
                _windowHandle = _glfw.CreateWindow(1, 1, "S&box NativeAOT (Init)", null, null);
                
                if (_windowHandle == null)
                {
                    Console.WriteLine("[NativeAOT] SourceEngineInit: GLFW CreateWindow failed!");
                    byte* errorDesc;
                    var errorCode = _glfw.GetError(out errorDesc);
                    string? errorStr = Marshal.PtrToStringUTF8((IntPtr)errorDesc);
                    Console.WriteLine($"[NativeAOT] GLFW Error: {errorCode} - {errorStr}");
                    return 0; // Failure
                }
                
                _glfw.MakeContextCurrent(_windowHandle);
                
                // Initialize GL
                _gl = GL.GetApi(new GlfwContext(_glfw, _windowHandle));
                
                Console.WriteLine("[NativeAOT] SourceEngineInit: OpenGL initialized");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] SourceEngineInit: Exception: {ex}");
            return 0; // Failure
        }
        
        return 1; // Success
    }
    
    /// <summary>
    /// Traite une frame du moteur Source (version simplifiée pour PlatformFunctions).
    /// Note: La version complète avec rendu reste dans EngineExports.SourceEngineFrame.
    /// Signature: delegate* unmanaged&lt; IntPtr, double, double, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int SourceEngineFrame_Platform(IntPtr appDict, double currentTime, double previousTime)
    {
        if (_windowHandle == null || _glfw == null) return 0;
        
        _glfw.PollEvents();
        
        if (_glfw.WindowShouldClose(_windowHandle)) return 0;
        
        _currentFrame++;
        
        return 1; // Success
    }
    
    /// <summary>
    /// Met à jour la taille de la fenêtre.
    /// Cette fonction est appelée quand la fenêtre est redimensionnée.
    /// Signature: delegate* unmanaged&lt; void &gt; (pas de paramètres, pas de retour)
    /// </summary>
    [UnmanagedCallersOnly]
    public static void UpdateWindowSize()
    {
        if (_windowHandle == null || _glfw == null) return;
        
        // La taille de la fenêtre est automatiquement mise à jour par GLFW
        // Cette fonction est principalement un callback pour notifier le moteur
        // Pour l'instant, on ne fait rien de spécial car GLFW gère déjà le redimensionnement
    }
    
    // État pour les fonctions Platform
    private static bool _isRetail = false; // Par défaut, ce n'est pas une version retail
    private static bool _noAssert = false;
    
    /// <summary>
    /// Vérifie si la fenêtre a le focus.
    /// Signature: delegate* unmanaged&lt; int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int global_IsWindowFocused()
    {
        if (_windowHandle == null || _glfw == null) return 0;
        
        return _glfw.GetWindowAttrib(_windowHandle, WindowAttributeGetter.Focused) ? 1 : 0;
    }
    
    /// <summary>
    /// Vérifie si l'application est en version retail.
    /// Signature: delegate* unmanaged&lt; int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int global_IsRetail()
    {
        return _isRetail ? 1 : 0;
    }
    
    /// <summary>
    /// Vérifie si un paramètre de lancement est présent.
    /// Signature: delegate* unmanaged&lt; IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int global_HasLaunchParameter(IntPtr paramPtr)
    {
        if (paramPtr == IntPtr.Zero) return 0;
        
        string? param = Marshal.PtrToStringUTF8(paramPtr);
        if (string.IsNullOrEmpty(param)) return 0;
        
        // Vérifier dans les arguments de ligne de commande
        string[] args = Environment.GetCommandLineArgs();
        foreach (string arg in args)
        {
            if (arg.Equals(param, StringComparison.OrdinalIgnoreCase) || 
                arg.StartsWith(param + "=", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }
        }
        
        return 0;
    }
    
    /// <summary>
    /// Désactive les assertions.
    /// Signature: delegate* unmanaged&lt; void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void global_Plat_SetNoAssert()
    {
        _noAssert = true;
        Console.WriteLine("[NativeAOT] global_Plat_SetNoAssert");
    }
    
    // Cache pour GetGameSearchPath
    private static IntPtr? _cachedGameSearchPath = null;
    
    /// <summary>
    /// Obtient le chemin de recherche du jeu.
    /// Signature: delegate* unmanaged&lt; IntPtr &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr global_GetGameSearchPath()
    {
        if (_cachedGameSearchPath == null)
        {
            // Utiliser le même chemin que GetGameRootFolder pour l'instant
            string? searchPath = GetModuleDirectory();
            if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
            {
                searchPath = Directory.GetCurrentDirectory();
            }
            
            _cachedGameSearchPath = Marshal.StringToHGlobalAnsi(searchPath);
        }
        return _cachedGameSearchPath.Value;
    }
    
    /// <summary>
    /// Initialise le moteur Source pour les tests unitaires.
    /// Signature: delegate* unmanaged&lt; int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int global_SourceEngineUnitTestInit()
    {
        Console.WriteLine("[NativeAOT] global_SourceEngineUnitTestInit");
        return 1; // Success
    }
    
    /// <summary>
    /// Arrête le moteur Source.
    /// Signature: delegate* unmanaged&lt; IntPtr, int, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void global_SourceEngineShutdown(IntPtr appDict, int forced)
    {
        Console.WriteLine($"[NativeAOT] global_SourceEngineShutdown: forced={forced}");
        
        // Nettoyer les ressources si nécessaire
        if (_cachedGameRootFolder != null)
        {
            Marshal.FreeHGlobal(_cachedGameRootFolder.Value);
            _cachedGameRootFolder = null;
        }
        
        if (_cachedGameSearchPath != null)
        {
            Marshal.FreeHGlobal(_cachedGameSearchPath.Value);
            _cachedGameSearchPath = null;
        }
    }
    
    /// <summary>
    /// Vérifie si l'application est un serveur dédié.
    /// Signature: delegate* unmanaged&lt; int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int global_AppIsDedicatedServer()
    {
        // Cette fonction vérifie si l'application est un serveur dédié
        // On peut utiliser MaterialSystem pour obtenir cette information si nécessaire
        return 0; // Par défaut, ce n'est pas un serveur dédié
    }
    
    /// <summary>
    /// Obtient le DPI diagonal de l'écran.
    /// Signature: delegate* unmanaged&lt; float &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static float global_GetDiagonalDpi()
    {
        // Retourner un DPI par défaut (96 DPI est standard)
        // Dans une implémentation complète, on pourrait utiliser GLFW pour obtenir le DPI réel
        return 96.0f;
    }
    
    /// <summary>
    /// Indique une activité pour le moniteur de blocage des outils.
    /// Signature: delegate* unmanaged&lt; void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void global_ToolsStallMonitor_IndicateActivity()
    {
        // Cette fonction est appelée pour indiquer qu'il y a une activité
        // Utile pour éviter que les outils ne soient considérés comme bloqués
        // Pour l'instant, c'est un stub
    }
    
    // ========== Fonctions Platform supplémentaires ==========
    
    /// <summary>
    /// Convertit les coordonnées écran vers coordonnées fenêtre.
    /// Signature: delegate* unmanaged&lt; IntPtr, int*, int*, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void global_Plat_ScreenToWindowCoords(IntPtr window, int* x, int* y)
    {
        if (_windowHandle == null || _glfw == null || x == null || y == null) return;
        
        // Obtenir la position de la fenêtre
        _glfw.GetWindowPos(_windowHandle, out int winX, out int winY);
        
        // Convertir les coordonnées écran vers coordonnées fenêtre
        *x -= winX;
        *y -= winY;
    }
    
    /// <summary>
    /// Convertit les coordonnées fenêtre vers coordonnées écran.
    /// Signature: delegate* unmanaged&lt; IntPtr, int*, int*, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void global_Plat_WindowToScreenCoords(IntPtr window, int* x, int* y)
    {
        if (_windowHandle == null || _glfw == null || x == null || y == null) return;
        
        // Obtenir la position de la fenêtre
        _glfw.GetWindowPos(_windowHandle, out int winX, out int winY);
        
        // Convertir les coordonnées fenêtre vers coordonnées écran
        *x += winX;
        *y += winY;
    }
    
    /// <summary>
    /// Obtient la résolution du bureau pour un moniteur donné.
    /// Signature: delegate* unmanaged&lt; int, int*, int*, uint*, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int global_Plat_GetDesktopResolution(int nMonitorIndex, int* pWidth, int* pHeight, uint* pRefreshRate)
    {
        if (_glfw == null || pWidth == null || pHeight == null || pRefreshRate == null) return 0;
        
        try
        {
            // Obtenir le moniteur (0 = moniteur principal)
            Silk.NET.GLFW.Monitor* monitor = nMonitorIndex == 0 ? _glfw.GetPrimaryMonitor() : null;
            if (monitor == null) return 0;
            
            // Obtenir la résolution du moniteur
            VideoMode* mode = _glfw.GetVideoMode(monitor);
            if (mode == null) return 0;
            
            *pWidth = (int)mode->Width;
            *pHeight = (int)mode->Height;
            *pRefreshRate = (uint)mode->RefreshRate;
            
            return 1; // Success
        }
        catch
        {
            // En cas d'erreur, retourner des valeurs par défaut
            *pWidth = 1920;
            *pHeight = 1080;
            *pRefreshRate = 60;
            return 0;
        }
    }
    
    /// <summary>
    /// Obtient l'index du moniteur par défaut.
    /// Signature: delegate* unmanaged&lt; int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int global_Plat_GetDefaultMonitorIndex()
    {
        // Retourner 0 pour le moniteur principal
        return 0;
    }
    
    /// <summary>
    /// Supprime un fichier de manière sécurisée.
    /// Signature: delegate* unmanaged&lt; IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int global_Plat_SafeRemoveFile(IntPtr filePathPtr)
    {
        if (filePathPtr == IntPtr.Zero) return 0;
        
        string? filePath = Marshal.PtrToStringUTF8(filePathPtr);
        if (string.IsNullOrEmpty(filePath)) return 0;
        
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Console.WriteLine($"[NativeAOT] global_Plat_SafeRemoveFile: {filePath}");
                return 1; // Success
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] ERROR: global_Plat_SafeRemoveFile failed: {ex.Message}");
        }
        
        return 0; // Failed
    }
    
    /// <summary>
    /// Change le numéro de frame actuel par un delta.
    /// Signature: delegate* unmanaged&lt; long, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void global_Plat_ChangeCurrentFrame(long delta)
    {
        _currentFrame = (ulong)((long)_currentFrame + delta);
    }
    
    /// <summary>
    /// Vérifie si l'application s'exécute sur une machine client.
    /// Signature: delegate* unmanaged&lt; int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int global_Plat_IsRunningOnCustomerMachine()
    {
        // Par défaut, on assume que c'est une machine client (pas un serveur de développement)
        return 1;
    }
    
    // Gestion du presse-papiers (stubs pour l'instant, nécessiterait une intégration système)
    private static string? _clipboardText = null;
    
    /// <summary>
    /// Vérifie si le presse-papiers contient du texte.
    /// Signature: delegate* unmanaged&lt; int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int global_Plat_HasClipboardText()
    {
        // Note: Dans une implémentation complète, on utiliserait GLFW.GetClipboardString()
        return _clipboardText != null ? 1 : 0;
    }
    
    /// <summary>
    /// Définit le texte du presse-papiers.
    /// Signature: delegate* unmanaged&lt; IntPtr, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void global_Plat_SetClipboardText(IntPtr textPtr)
    {
        if (textPtr == IntPtr.Zero) return;
        
        string? text = Marshal.PtrToStringUTF8(textPtr);
        _clipboardText = text;
        
        // Note: Dans une implémentation complète, on utiliserait GLFW.SetClipboardString()
        if (_glfw != null && _windowHandle != null)
        {
            try
            {
                _glfw.SetClipboardString(_windowHandle, text ?? "");
            }
            catch
            {
                // Ignorer les erreurs de presse-papiers
            }
        }
    }
    
    /// <summary>
    /// Obtient le texte du presse-papiers.
    /// Signature: delegate* unmanaged&lt; IntPtr &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr global_Plat_GetClipboardText()
    {
        string? text = null;
        
        // Essayer d'obtenir depuis GLFW
        if (_glfw != null && _windowHandle != null)
        {
            try
            {
                text = _glfw.GetClipboardString(_windowHandle);
            }
            catch
            {
                // Ignorer les erreurs de presse-papiers
            }
        }
        
        // Utiliser le cache si GLFW échoue
        if (string.IsNullOrEmpty(text))
        {
            text = _clipboardText;
        }
        
        if (string.IsNullOrEmpty(text))
            return IntPtr.Zero;
        
        // Allouer de la mémoire pour le texte (l'appelant doit libérer)
        return Marshal.StringToHGlobalAnsi(text);
    }
    
    /// <summary>
    /// Efface le texte du presse-papiers.
    /// Signature: delegate* unmanaged&lt; void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void global_Plat_ClearClipboardText()
    {
        _clipboardText = null;
        
        // Essayer d'effacer via GLFW
        if (_glfw != null && _windowHandle != null)
        {
            try
            {
                _glfw.SetClipboardString(_windowHandle, "");
            }
            catch
            {
                // Ignorer les erreurs de presse-papiers
            }
        }
    }
}

