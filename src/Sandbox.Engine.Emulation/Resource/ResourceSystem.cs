using System.Runtime.InteropServices;
using System.Collections.Generic;
using NativeEngine;

namespace Sandbox.Engine.Emulation.Resource;

/// <summary>
/// Module d'émulation pour ResourceSystem (g_pRsrcSystm_*) et ResourceCompilerSystem (g_pRsrcCmplrSyst_*).
/// Gère le chargement et la compilation des ressources.
/// </summary>
public static unsafe class ResourceSystem
{
    // État pour gérer les manifests de ressources
    private static readonly Dictionary<IntPtr, ResourceManifestData> _manifests = new();
    private static int _nextManifestId = 1;
    
    // Liste des manifests de code (pour GetAllCodeManifests)
    private static readonly List<string> _codeManifests = new();

    private class ResourceManifestData
    {
        public string Name { get; set; } = "";
        public bool IsLoaded { get; set; }
        public IntPtr Handle { get; set; }
        public bool IsCodeManifest { get; set; } // Indique si c'est un manifest de code
    }

    /// <summary>
    /// Initialise le module ResourceSystem en patchant toutes les fonctions natives.
    /// Signatures exactes depuis Interop.Engine.cs ligne 16262-16268.
    /// </summary>
    public static void Init(void** native)
    {
        // ResourceSystem functions (indices 1515-1521 depuis Interop.Engine.cs ligne 16262-16268)
        native[1515] = (void*)(delegate* unmanaged<void>)&g_pRsrcSystm_ReloadSymlinkedResidentResources;
        native[1516] = (void*)(delegate* unmanaged<void>)&g_pRsrcSystm_UpdateSimple;
        native[1517] = (void*)(delegate* unmanaged<int>)&g_pRsrcSystm_HasPendingWork;
        native[1518] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&g_pRsrcSystm_LoadResourceInManifest;
        native[1519] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pRsrcSystm_DestroyResourceManifest;
        native[1520] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pRsrcSystm_IsManifestLoaded;
        native[1521] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pRsrcSystm_GetAllCodeManifests;
    }

    // ============================================================================
    // ResourceCompilerSystem Functions (g_pRsrcCmplrSyst_*)
    // ============================================================================

    /// <summary>
    /// Génère un fichier de ressource depuis des données binaires.
    /// Cette fonction compile des ressources (matériaux, modèles, etc.) en fichiers binaires compilés.
    /// Signature: delegate* unmanaged&lt; IntPtr, IntPtr, int, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pRsrcCmplrSyst_GenerateResourceFile(IntPtr path, IntPtr pData, int size)
    {
        if (path == IntPtr.Zero || pData == IntPtr.Zero || size <= 0)
        {
            Console.WriteLine("[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceFile: Invalid parameters");
            return 0; // Échec
        }
        
        string? pathStr = Marshal.PtrToStringUTF8(path);
        Console.WriteLine($"[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceFile: path={pathStr}, size={size}");
        
        try
        {
            // Lire les données depuis le pointeur
            byte[] data = new byte[size];
            Marshal.Copy(pData, data, 0, size);
            
            // Écrire le fichier compilé
            if (!string.IsNullOrEmpty(pathStr))
            {
                // S'assurer que le répertoire existe
                string? directory = Path.GetDirectoryName(pathStr);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                File.WriteAllBytes(pathStr, data);
                Console.WriteLine($"[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceFile: Successfully wrote {size} bytes to {pathStr}");
                return 1; // Succès
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceFile: Exception: {ex.Message}");
        }
        
        return 0; // Échec
    }

    /// <summary>
    /// Génère un fichier de ressource depuis du texte (JSON, etc.).
    /// Cette fonction compile des ressources depuis leur format texte source vers un fichier compilé.
    /// Signature: delegate* unmanaged&lt; IntPtr, IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pRsrcCmplrSyst_GenerateResourceFile_1(IntPtr path, IntPtr text)
    {
        if (path == IntPtr.Zero || text == IntPtr.Zero)
        {
            Console.WriteLine("[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceFile_1: Invalid parameters");
            return 0; // Échec
        }
        
        string? pathStr = Marshal.PtrToStringUTF8(path);
        string? textStr = Marshal.PtrToStringUTF8(text);
        Console.WriteLine($"[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceFile_1: path={pathStr}, textLength={textStr?.Length ?? 0}");
        
        try
        {
            if (!string.IsNullOrEmpty(pathStr) && !string.IsNullOrEmpty(textStr))
            {
                // S'assurer que le répertoire existe
                string? directory = Path.GetDirectoryName(pathStr);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Écrire le texte dans le fichier
                File.WriteAllText(pathStr, textStr);
                Console.WriteLine($"[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceFile_1: Successfully wrote text to {pathStr}");
                return 1; // Succès
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceFile_1: Exception: {ex.Message}");
        }
        
        return 0; // Échec
    }

    /// <summary>
    /// Génère des bytes de ressource depuis des données binaires.
    /// Cette fonction compile des ressources et retourne un pointeur vers les données compilées.
    /// Signature: delegate* unmanaged&lt; IntPtr, IntPtr, int, IntPtr &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr g_pRsrcCmplrSyst_GenerateResourceBytes(IntPtr path, IntPtr pData, int size)
    {
        if (path == IntPtr.Zero || pData == IntPtr.Zero || size <= 0)
        {
            Console.WriteLine("[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceBytes: Invalid parameters");
            return IntPtr.Zero;
        }
        
        string? pathStr = Marshal.PtrToStringUTF8(path);
        Console.WriteLine($"[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceBytes: path={pathStr}, size={size}");
        
        try
        {
            // Pour l'émulation, on peut simplement retourner les données d'entrée
            // Dans Source 2, cette fonction effectue une compilation réelle (compression, optimisation, etc.)
            // Pour l'instant, on alloue une copie des données et on retourne le pointeur
            // NOTE: La mémoire doit être libérée par l'appelant (ou gérée par le système de ressources)
            IntPtr result = Marshal.AllocHGlobal(size);
            Buffer.MemoryCopy((void*)pData, (void*)result, size, size);
            
            Console.WriteLine($"[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceBytes: Allocated {size} bytes at 0x{result.ToInt64():X}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceBytes: Exception: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    // ============================================================================
    // ResourceSystem Functions (g_pRsrcSystm_*)
    // ============================================================================

    /// <summary>
    /// Recharge les ressources résidentes liées par symlink.
    /// Cette fonction est appelée pour mettre à jour les ressources après un changement de symlink.
    /// Signature: delegate* unmanaged&lt; void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pRsrcSystm_ReloadSymlinkedResidentResources()
    {
        Console.WriteLine("[NativeAOT] g_pRsrcSystm_ReloadSymlinkedResidentResources");
        // Stub implementation - sur Linux, les symlinks sont gérés par le système de fichiers
        // Cette fonction est principalement utilisée pour recharger les ressources après un changement de symlink
        // Pour l'émulation, on peut ignorer cette opération car le système de fichiers virtuel gère déjà les changements
    }

    /// <summary>
    /// Met à jour le système de ressources de manière simple (appelé chaque frame).
    /// Signature: delegate* unmanaged&lt; void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pRsrcSystm_UpdateSimple()
    {
        // Stub implementation - pas de mise à jour nécessaire pour l'émulation
        // Cette fonction est appelée chaque frame pour mettre à jour l'état des ressources
    }

    /// <summary>
    /// Vérifie s'il y a du travail en attente dans le système de ressources.
    /// Signature: delegate* unmanaged&lt; int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pRsrcSystm_HasPendingWork()
    {
        // Stub implementation - retourne 0 (pas de travail en attente)
        return 0;
    }

    /// <summary>
    /// Charge une ressource dans un manifest.
    /// Cette fonction charge un manifest de ressources depuis le système de fichiers.
    /// Signature: delegate* unmanaged&lt; IntPtr, IntPtr &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr g_pRsrcSystm_LoadResourceInManifest(IntPtr name)
    {
        if (name == IntPtr.Zero)
        {
            Console.WriteLine("[NativeAOT] g_pRsrcSystm_LoadResourceInManifest: name is null");
            return IntPtr.Zero;
        }
        
        string? nameStr = Marshal.PtrToStringUTF8(name);
        Console.WriteLine($"[NativeAOT] g_pRsrcSystm_LoadResourceInManifest: name={nameStr}");
        
        if (string.IsNullOrEmpty(nameStr))
            return IntPtr.Zero;
        
        // Vérifier si le manifest existe déjà
        foreach (var kvp in _manifests)
        {
            if (kvp.Value.Name == nameStr)
            {
                Console.WriteLine($"[NativeAOT] g_pRsrcSystm_LoadResourceInManifest: Manifest '{nameStr}' already exists, returning existing handle={kvp.Key}");
                return kvp.Key;
            }
        }
        
        // Créer un nouveau manifest
        var manifest = new ResourceManifestData
        {
            Name = nameStr,
            IsLoaded = true,
            Handle = (IntPtr)_nextManifestId++,
            IsCodeManifest = nameStr.Contains("code", StringComparison.OrdinalIgnoreCase) || 
                            nameStr.EndsWith(".code", StringComparison.OrdinalIgnoreCase)
        };
        
        _manifests[manifest.Handle] = manifest;
        
        // Si c'est un manifest de code, l'ajouter à la liste
        if (manifest.IsCodeManifest && !_codeManifests.Contains(nameStr))
        {
            _codeManifests.Add(nameStr);
        }
        
        Console.WriteLine($"[NativeAOT] g_pRsrcSystm_LoadResourceInManifest: created manifest handle={manifest.Handle}, isCode={manifest.IsCodeManifest}");
        return manifest.Handle;
    }

    /// <summary>
    /// Détruit un manifest de ressources.
    /// Signature: delegate* unmanaged&lt; IntPtr, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pRsrcSystm_DestroyResourceManifest(IntPtr manifest)
    {
        if (manifest == IntPtr.Zero) return;
        
        if (_manifests.Remove(manifest))
        {
            Console.WriteLine($"[NativeAOT] g_pRsrcSystm_DestroyResourceManifest: destroyed manifest handle={manifest}");
        }
    }

    /// <summary>
    /// Vérifie si un manifest est chargé.
    /// Signature: delegate* unmanaged&lt; IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pRsrcSystm_IsManifestLoaded(IntPtr manifest)
    {
        if (manifest == IntPtr.Zero) return 0;
        
        if (_manifests.TryGetValue(manifest, out var manifestData))
        {
            return manifestData.IsLoaded ? 1 : 0;
        }
        
        return 0;
    }

    /// <summary>
    /// Récupère tous les manifests de code.
    /// Cette fonction remplit un CUtlVectorString avec les noms des manifests de code.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, void &gt;
    /// Le paramètre values est un pointeur vers un CUtlVectorString natif.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pRsrcSystm_GetAllCodeManifests(IntPtr values)
    {
        if (values == IntPtr.Zero)
        {
            Console.WriteLine("[NativeAOT] g_pRsrcSystm_GetAllCodeManifests: values is null");
            return;
        }
        
        throw new NotImplementedException("g_pRsrcSystm_GetAllCodeManifests requires CUtlVectorString native append support");
    }
}

