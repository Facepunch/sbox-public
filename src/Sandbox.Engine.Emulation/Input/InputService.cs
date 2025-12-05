using System;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Platform;
using Silk.NET.GLFW;

namespace Sandbox.Engine.Emulation.Input;

/// <summary>
/// Module d'émulation pour InputService (g_pInputService_*).
/// Gère les fonctions d'entrée (clavier, souris, etc.) via GLFW.
/// </summary>
public static unsafe class InputService
{
    /// <summary>
    /// Initialise le module InputService en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 16295-16300 :
    /// - g_pInputService_IsAppActive: 1430
    /// - g_pInputService_HasMouseFocus: 1431
    /// - g_pInputService_Key_NameForBinding: 1432
    /// - g_pInputService_GetBinding: 1433
    /// - g_pInputService_SetCursorPosition: 1434
    /// - g_pInputService_Pump: 1435
    /// </summary>
    public static void Init(void** native)
    {
        // Indices 1430-1435
        native[1430] = (void*)(delegate* unmanaged<int>)&g_pInputService_IsAppActive;
        native[1431] = (void*)(delegate* unmanaged<int>)&g_pInputService_HasMouseFocus;
        native[1432] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&g_pInputService_Key_NameForBinding;
        native[1433] = (void*)(delegate* unmanaged<long, IntPtr>)&g_pInputService_GetBinding;
        native[1434] = (void*)(delegate* unmanaged<int, int, void>)&g_pInputService_SetCursorPosition;
        native[1435] = (void*)(delegate* unmanaged<void>)&g_pInputService_Pump;
        
        Console.WriteLine("[NativeAOT] InputService module initialized");
    }
    
    /// <summary>
    /// Check if the application is active (window has focus).
    /// 
    /// **Comportement Source 2** : Retourne si l'application a le focus.
    /// **Comportement émulation** : Utilise GLFW pour vérifier si la fenêtre a le focus.
    /// </summary>
    /// <returns>1 si l'app est active, 0 sinon</returns>
    [UnmanagedCallersOnly]
    public static int g_pInputService_IsAppActive()
    {
        var windowHandle = PlatformFunctions.GetWindowHandle();
        var glfw = PlatformFunctions.GetGlfw();
        
        if (windowHandle == null || glfw == null)
            return 0;
        
        bool focused = glfw.GetWindowAttrib(windowHandle, WindowAttributeGetter.Focused);
        return focused ? 1 : 0;
    }
    
    /// <summary>
    /// Check if the window has mouse focus.
    /// 
    /// **Comportement Source 2** : Retourne si la fenêtre a le focus de la souris.
    /// **Comportement émulation** : Utilise GLFW pour vérifier si la fenêtre a le focus de la souris.
    /// </summary>
    /// <returns>1 si la souris a le focus, 0 sinon</returns>
    [UnmanagedCallersOnly]
    public static int g_pInputService_HasMouseFocus()
    {
        var windowHandle = PlatformFunctions.GetWindowHandle();
        var glfw = PlatformFunctions.GetGlfw();
        
        if (windowHandle == null || glfw == null)
            return 0;
        
        // GLFW n'a pas de fonction directe pour vérifier le focus de la souris
        // On vérifie si la fenêtre a le focus et si le curseur est dans la fenêtre
        bool focused = glfw.GetWindowAttrib(windowHandle, WindowAttributeGetter.Focused);
        bool hovered = glfw.GetWindowAttrib(windowHandle, WindowAttributeGetter.Hovered);
        
        return (focused && hovered) ? 1 : 0;
    }
    
    /// <summary>
    /// Get the key name for a binding string.
    /// 
    /// **Comportement Source 2** : Retourne le nom de la touche correspondant à un binding.
    /// **Comportement émulation** : Retourne une chaîne vide pour l'instant (non implémenté).
    /// </summary>
    /// <param name="binding">Pointeur vers la chaîne de binding (UTF-8)</param>
    /// <returns>Pointeur vers la chaîne du nom de la touche (UTF-8), ou IntPtr.Zero</returns>
    [UnmanagedCallersOnly]
    public static IntPtr g_pInputService_Key_NameForBinding(IntPtr binding)
    {
        if (binding == IntPtr.Zero)
            return IntPtr.Zero;
        
        string? bindingStr = Marshal.PtrToStringUTF8(binding);
        if (string.IsNullOrEmpty(bindingStr))
            return IntPtr.Zero;
        
        // TODO: Implémenter la conversion binding -> nom de touche
        // Pour l'instant, retourner une chaîne vide
        // Cette fonction nécessite un système de mapping binding -> key code -> key name
        Console.WriteLine($"[NativeAOT] g_pInputService_Key_NameForBinding: binding={bindingStr} (not implemented)");
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Get the binding string for a button code.
    /// 
    /// **Comportement Source 2** : Retourne le binding correspondant à un code de bouton.
    /// **Comportement émulation** : Retourne une chaîne vide pour l'instant (non implémenté).
    /// </summary>
    /// <param name="button">Code du bouton (ButtonCode)</param>
    /// <returns>Pointeur vers la chaîne du binding (UTF-8), ou IntPtr.Zero</returns>
    [UnmanagedCallersOnly]
    public static IntPtr g_pInputService_GetBinding(long button)
    {
        // TODO: Implémenter la conversion button code -> binding string
        // Pour l'instant, retourner IntPtr.Zero
        // Cette fonction nécessite un système de mapping button code -> binding
        Console.WriteLine($"[NativeAOT] g_pInputService_GetBinding: button={button} (not implemented)");
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Set the cursor position in window coordinates.
    /// 
    /// **Comportement Source 2** : Définit la position du curseur dans les coordonnées de la fenêtre.
    /// **Comportement émulation** : Utilise GLFW pour définir la position du curseur.
    /// </summary>
    /// <param name="x">Position X en pixels</param>
    /// <param name="y">Position Y en pixels</param>
    [UnmanagedCallersOnly]
    public static void g_pInputService_SetCursorPosition(int x, int y)
    {
        var windowHandle = PlatformFunctions.GetWindowHandle();
        var glfw = PlatformFunctions.GetGlfw();
        
        if (windowHandle == null || glfw == null)
            return;
        
        glfw.SetCursorPos(windowHandle, x, y);
    }
    
    /// <summary>
    /// Pump input events (process keyboard, mouse, etc.).
    /// 
    /// **Comportement Source 2** : Traite les événements d'entrée en attente.
    /// **Comportement émulation** : Appelle glfw.PollEvents() pour traiter les événements GLFW.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pInputService_Pump()
    {
        var glfw = PlatformFunctions.GetGlfw();
        
        if (glfw == null)
            return;
        
        // PollEvents traite tous les événements en attente (clavier, souris, fenêtre, etc.)
        glfw.PollEvents();
    }
}

