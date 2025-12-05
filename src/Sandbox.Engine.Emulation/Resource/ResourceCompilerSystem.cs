using System.Runtime.InteropServices;
using System.IO;
using NativeEngine;
using System;

namespace Sandbox.Engine.Emulation.Resource;

/// <summary>
/// Module d'émulation pour ResourceCompilerSystem (g_pRsrcCmplrSyst_*).
/// Gère la compilation des ressources depuis leur format source vers des fichiers compilés.
/// </summary>
public static unsafe class ResourceCompilerSystem
{
    /// <summary>
    /// Initialise le module ResourceCompilerSystem en patchant toutes les fonctions natives.
    /// </summary>
    public static void Init(void** native)
    {
        // ResourceCompilerSystem functions (indices 1512-1514 depuis Interop.Engine.cs ligne 16259-16261)
        native[1512] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, int>)&g_pRsrcCmplrSyst_GenerateResourceFile;
        native[1513] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int>)&g_pRsrcCmplrSyst_GenerateResourceFile_1;
        native[1514] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, IntPtr>)&g_pRsrcCmplrSyst_GenerateResourceBytes;
    }

    // ============================================================================
    // ResourceCompilerSystem Functions (g_pRsrcCmplrSyst_*)
    // Signatures exactes depuis Interop.Engine.cs ligne 7020-7022
    // ============================================================================

    /// <summary>
    /// Génère un fichier de ressource depuis des données binaires.
    /// Cette fonction compile des ressources (matériaux, modèles, etc.) en fichiers binaires compilés.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, IntPtr, int, int &gt;
    /// Retourne 1 en cas de succès, 0 en cas d'échec.
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
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, IntPtr, int &gt;
    /// Retourne 1 en cas de succès, 0 en cas d'échec.
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
    /// Cette fonction compile des ressources et retourne un CUtlBuffer contenant les données compilées.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, IntPtr, int, IntPtr &gt;
    /// Retourne un IntPtr vers un CUtlBuffer, ou IntPtr.Zero en cas d'échec.
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
            // Pour l'émulation, on crée un CUtlBuffer avec les données
            // Dans Source 2, cette fonction effectue une compilation réelle (compression, optimisation, etc.)
            // Signature exacte depuis Interop.Engine.cs ligne 7017: retourne un CUtlBuffer (IntPtr)
            // NOTE: CUtlBuffer.Create() retourne un objet CUtlBuffer qui a une conversion implicite vers IntPtr
            // Mais dans [UnmanagedCallersOnly], on ne peut pas utiliser using car le buffer doit survivre après le return
            // Pour l'émulation, on alloue directement la mémoire et on retourne le pointeur
            // L'appelant doit gérer la libération de la mémoire (ou utiliser le wrapper CUtlBuffer côté managé)
            IntPtr bufferPtr = Marshal.AllocHGlobal(size);
            unsafe
            {
                byte* src = (byte*)pData;
                byte* dst = (byte*)bufferPtr;
                Buffer.MemoryCopy(src, dst, size, size);
            }
            
            Console.WriteLine($"[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceBytes: Allocated {size} bytes at 0x{bufferPtr.ToInt64():X}");
            // NOTE: La mémoire allouée doit être libérée par l'appelant ou gérée par le système de ressources
            // Dans Source 2, le CUtlBuffer gère sa propre mémoire via le wrapper C#
            return bufferPtr;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] g_pRsrcCmplrSyst_GenerateResourceBytes: Exception: {ex.Message}");
            return IntPtr.Zero;
        }
    }
}

