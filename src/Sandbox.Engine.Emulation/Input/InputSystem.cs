using System;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Platform;
using Silk.NET.GLFW;

namespace Sandbox.Engine.Emulation.Input;

/// <summary>
/// Module d'émulation pour InputSystem (g_pInputSystem_*).
/// Gère les fonctions d'entrée système (SDL, Editor, IME, cursors, etc.) via GLFW.
/// </summary>
public static unsafe class InputSystem
{
    // État pour le mode relatif de la souris
    private static bool _relativeMouseMode = false;
    
    // État pour IME
    private static bool _imeAllowed = false;
    
    /// <summary>
    /// Initialise le module InputSystem en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 16301-16321 :
    /// - g_pInputSystem_RegisterWindowWithSDL: 1436
    /// - g_pInputSystem_UnregisterWindowFromSDL: 1437
    /// - g_pInputSystem_SetEditorMainWindow: 1438
    /// - g_pInputSystem_OnEditorGameFocusChange: 1439
    /// - g_pInputSystem_SetCursorPosition: 1440
    /// - g_pInputSystem_HasMouseFocus: 1441
    /// - g_pInputSystem_IsAppActive: 1442
    /// - g_pInputSystem_IsIMEAllowed: 1443
    /// - g_pInputSystem_SetIMEAllowed: 1444
    /// - g_pInputSystem_SetIMETextLocation: 1445
    /// - g_pInputSystem_DismissIME: 1446
    /// - g_pInputSystem_CodeToString: 1447
    /// - g_pInputSystem_StringToButtonCode: 1448
    /// - g_pInputSystem_VirtualKeyToButtonCode: 1449
    /// - g_pInputSystem_ButtonCodeToVirtualKey: 1450
    /// - g_pInputSystem_SetRelativeMouseMode: 1451
    /// - g_pInputSystem_GetRelativeMouseMode: 1452
    /// - g_pInputSystem_SetCursorStandard: 1453
    /// - g_pInputSystem_SetCursorUser: 1454
    /// - g_pInputSystem_LoadCursorFromFile: 1455
    /// - g_pInputSystem_ShutdownUserCursors: 1456
    /// </summary>
    public static void Init(void** native)
    {
        // Indices 1436-1456
        native[1436] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pInputSystem_RegisterWindowWithSDL;
        native[1437] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pInputSystem_UnregisterWindowFromSDL;
        native[1438] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pInputSystem_SetEditorMainWindow;
        native[1439] = (void*)(delegate* unmanaged<IntPtr, int, void>)&g_pInputSystem_OnEditorGameFocusChange;
        native[1440] = (void*)(delegate* unmanaged<int, int, IntPtr, void>)&g_pInputSystem_SetCursorPosition;
        native[1441] = (void*)(delegate* unmanaged<int>)&g_pInputSystem_HasMouseFocus;
        native[1442] = (void*)(delegate* unmanaged<int>)&g_pInputSystem_IsAppActive;
        native[1443] = (void*)(delegate* unmanaged<int>)&g_pInputSystem_IsIMEAllowed;
        native[1444] = (void*)(delegate* unmanaged<int, void>)&g_pInputSystem_SetIMEAllowed;
        native[1445] = (void*)(delegate* unmanaged<int, int, int, int, void>)&g_pInputSystem_SetIMETextLocation;
        native[1446] = (void*)(delegate* unmanaged<void>)&g_pInputSystem_DismissIME;
        native[1447] = (void*)(delegate* unmanaged<long, IntPtr>)&g_pInputSystem_CodeToString;
        native[1448] = (void*)(delegate* unmanaged<IntPtr, long>)&g_pInputSystem_StringToButtonCode;
        native[1449] = (void*)(delegate* unmanaged<int, long>)&g_pInputSystem_VirtualKeyToButtonCode;
        native[1450] = (void*)(delegate* unmanaged<long, int>)&g_pInputSystem_ButtonCodeToVirtualKey;
        native[1451] = (void*)(delegate* unmanaged<int, void>)&g_pInputSystem_SetRelativeMouseMode;
        native[1452] = (void*)(delegate* unmanaged<int>)&g_pInputSystem_GetRelativeMouseMode;
        native[1453] = (void*)(delegate* unmanaged<long, void>)&g_pInputSystem_SetCursorStandard;
        native[1454] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pInputSystem_SetCursorUser;
        native[1455] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, int, int>)&g_pInputSystem_LoadCursorFromFile;
        native[1456] = (void*)(delegate* unmanaged<void>)&g_pInputSystem_ShutdownUserCursors;
        
        Console.WriteLine("[NativeAOT] InputSystem module initialized");
    }
    
    /// <summary>
    /// Register a window with SDL input system.
    /// 
    /// **Source 2 behavior**: Registers a window handle with SDL for input handling.
    /// **Emulation behavior**: No-op (SDL not used on Linux, GLFW handles input).
    /// </summary>
    /// <param name="hwnd">Window handle</param>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_RegisterWindowWithSDL(IntPtr hwnd)
    {
        // SDL is not used on Linux, GLFW handles all input
        // This is a no-op but we log it for debugging
        Console.WriteLine($"[NativeAOT] g_pInputSystem_RegisterWindowWithSDL: hwnd={hwnd} (no-op on Linux)");
    }
    
    /// <summary>
    /// Unregister a window from SDL input system.
    /// 
    /// **Source 2 behavior**: Unregisters a window handle from SDL.
    /// **Emulation behavior**: No-op (SDL not used on Linux, GLFW handles input).
    /// </summary>
    /// <param name="hwnd">Window handle</param>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_UnregisterWindowFromSDL(IntPtr hwnd)
    {
        // SDL is not used on Linux, GLFW handles all input
        // This is a no-op but we log it for debugging
        Console.WriteLine($"[NativeAOT] g_pInputSystem_UnregisterWindowFromSDL: hwnd={hwnd} (no-op on Linux)");
    }
    
    /// <summary>
    /// Set the editor main window handle.
    /// 
    /// **Source 2 behavior**: Sets the main window for the editor.
    /// **Emulation behavior**: No-op (editor not supported on Linux).
    /// </summary>
    /// <param name="hwnd">Window handle</param>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_SetEditorMainWindow(IntPtr hwnd)
    {
        // Editor is not supported on Linux
        Console.WriteLine($"[NativeAOT] g_pInputSystem_SetEditorMainWindow: hwnd={hwnd} (no-op, editor not supported)");
    }
    
    /// <summary>
    /// Called when editor game focus changes.
    /// 
    /// **Source 2 behavior**: Notifies the input system of editor game focus changes.
    /// **Emulation behavior**: No-op (editor not supported on Linux).
    /// </summary>
    /// <param name="hwnd">Window handle</param>
    /// <param name="bIsFocused">1 if focused, 0 otherwise</param>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_OnEditorGameFocusChange(IntPtr hwnd, int bIsFocused)
    {
        // Editor is not supported on Linux
        Console.WriteLine($"[NativeAOT] g_pInputSystem_OnEditorGameFocusChange: hwnd={hwnd}, focused={bIsFocused} (no-op, editor not supported)");
    }
    
    /// <summary>
    /// Set the cursor position in window coordinates.
    /// 
    /// **Source 2 behavior**: Sets the cursor position for a specific window.
    /// **Emulation behavior**: Uses GLFW to set cursor position (window parameter ignored, uses main window).
    /// </summary>
    /// <param name="x">X position in pixels</param>
    /// <param name="y">Y position in pixels</param>
    /// <param name="window">Window handle (ignored, uses main window)</param>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_SetCursorPosition(int x, int y, IntPtr window)
    {
        var windowHandle = PlatformFunctions.GetWindowHandle();
        var glfw = PlatformFunctions.GetGlfw();
        
        if (windowHandle == null || glfw == null)
            return;
        
        // Note: window parameter is ignored, we use the main window
        glfw.SetCursorPos(windowHandle, x, y);
    }
    
    /// <summary>
    /// Check if the window has mouse focus.
    /// 
    /// **Source 2 behavior**: Returns if the window has mouse focus.
    /// **Emulation behavior**: Uses GLFW to check if window is focused and hovered.
    /// </summary>
    /// <returns>1 if mouse has focus, 0 otherwise</returns>
    [UnmanagedCallersOnly]
    public static int g_pInputSystem_HasMouseFocus()
    {
        var windowHandle = PlatformFunctions.GetWindowHandle();
        var glfw = PlatformFunctions.GetGlfw();
        
        if (windowHandle == null || glfw == null)
            return 0;
        
        bool focused = glfw.GetWindowAttrib(windowHandle, WindowAttributeGetter.Focused);
        bool hovered = glfw.GetWindowAttrib(windowHandle, WindowAttributeGetter.Hovered);
        
        return (focused && hovered) ? 1 : 0;
    }
    
    /// <summary>
    /// Check if the application is active (window has focus).
    /// 
    /// **Source 2 behavior**: Returns if the application is active.
    /// **Emulation behavior**: Uses GLFW to check if window has focus.
    /// </summary>
    /// <returns>1 if app is active, 0 otherwise</returns>
    [UnmanagedCallersOnly]
    public static int g_pInputSystem_IsAppActive()
    {
        var windowHandle = PlatformFunctions.GetWindowHandle();
        var glfw = PlatformFunctions.GetGlfw();
        
        if (windowHandle == null || glfw == null)
            return 0;
        
        bool focused = glfw.GetWindowAttrib(windowHandle, WindowAttributeGetter.Focused);
        return focused ? 1 : 0;
    }
    
    /// <summary>
    /// Check if IME (Input Method Editor) is allowed.
    /// 
    /// **Source 2 behavior**: Returns if IME input is allowed.
    /// **Emulation behavior**: Returns the stored IME allowed state.
    /// </summary>
    /// <returns>1 if IME is allowed, 0 otherwise</returns>
    [UnmanagedCallersOnly]
    public static int g_pInputSystem_IsIMEAllowed()
    {
        return _imeAllowed ? 1 : 0;
    }
    
    /// <summary>
    /// Set whether IME (Input Method Editor) is allowed.
    /// 
    /// **Source 2 behavior**: Enables or disables IME input.
    /// **Emulation behavior**: Stores the IME allowed state (not fully implemented, no actual IME support).
    /// </summary>
    /// <param name="bAllowed">1 to allow IME, 0 to disallow</param>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_SetIMEAllowed(int bAllowed)
    {
        throw new NotImplementedException("IME support is not implemented in the emulation layer");
    }
    
    /// <summary>
    /// Set the IME text input location rectangle.
    /// 
    /// **Source 2 behavior**: Sets the rectangle where IME text input should appear.
    /// **Emulation behavior**: No-op (IME not fully implemented).
    /// </summary>
    /// <param name="x">X position</param>
    /// <param name="y">Y position</param>
    /// <param name="nWidth">Width</param>
    /// <param name="nHeight">Height</param>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_SetIMETextLocation(int x, int y, int nWidth, int nHeight)
    {
        throw new NotImplementedException("IME text location is not implemented in the emulation layer");
    }
    
    /// <summary>
    /// Dismiss the IME (Input Method Editor).
    /// 
    /// **Source 2 behavior**: Dismisses any active IME input.
    /// **Emulation behavior**: No-op (IME not fully implemented).
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_DismissIME()
    {
        throw new NotImplementedException("IME dismissal is not implemented in the emulation layer");
    }
    
    /// <summary>
    /// Convert a button code to a string representation.
    /// 
    /// **Source 2 behavior**: Returns a string representation of a button code.
    /// **Emulation behavior**: Returns IntPtr.Zero (not implemented, requires button code mapping).
    /// </summary>
    /// <param name="code">Button code</param>
    /// <returns>Pointer to UTF-8 string, or IntPtr.Zero</returns>
    [UnmanagedCallersOnly]
    public static IntPtr g_pInputSystem_CodeToString(long code)
    {
        throw new NotImplementedException("g_pInputSystem_CodeToString requires button code mapping");
    }
    
    /// <summary>
    /// Convert a string to a button code.
    /// 
    /// **Source 2 behavior**: Returns the button code for a string representation.
    /// **Emulation behavior**: Returns 0 (not implemented, requires string to button code mapping).
    /// </summary>
    /// <param name="pString">Pointer to UTF-8 string</param>
    /// <returns>Button code, or 0 if not found</returns>
    [UnmanagedCallersOnly]
    public static long g_pInputSystem_StringToButtonCode(IntPtr pString)
    {
        if (pString == IntPtr.Zero)
            return 0;
        
        string? str = Marshal.PtrToStringUTF8(pString);
        if (string.IsNullOrEmpty(str))
            return 0;
        
        throw new NotImplementedException("g_pInputSystem_StringToButtonCode requires string->button mapping");
    }
    
    /// <summary>
    /// Convert a virtual key code to a button code.
    /// 
    /// **Source 2 behavior**: Returns the button code for a virtual key code.
    /// **Emulation behavior**: Returns 0 (not implemented, requires virtual key to button code mapping).
    /// </summary>
    /// <param name="nVirtualKey">Virtual key code</param>
    /// <returns>Button code, or 0 if not found</returns>
    [UnmanagedCallersOnly]
    public static long g_pInputSystem_VirtualKeyToButtonCode(int nVirtualKey)
    {
        throw new NotImplementedException("g_pInputSystem_VirtualKeyToButtonCode requires VK->button mapping");
    }
    
    /// <summary>
    /// Convert a button code to a virtual key code.
    /// 
    /// **Source 2 behavior**: Returns the virtual key code for a button code.
    /// **Emulation behavior**: Returns 0 (not implemented, requires button code to virtual key mapping).
    /// </summary>
    /// <param name="code">Button code</param>
    /// <returns>Virtual key code, or 0 if not found</returns>
    [UnmanagedCallersOnly]
    public static int g_pInputSystem_ButtonCodeToVirtualKey(long code)
    {
        throw new NotImplementedException("g_pInputSystem_ButtonCodeToVirtualKey requires button->VK mapping");
    }
    
    /// <summary>
    /// Set relative mouse mode (cursor hidden and locked to window center).
    /// 
    /// **Source 2 behavior**: Enables or disables relative mouse mode for FPS-style input.
    /// **Emulation behavior**: Uses GLFW SetInputMode with CursorModeValue.Disabled or Normal.
    /// </summary>
    /// <param name="bState">1 to enable relative mode, 0 to disable</param>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_SetRelativeMouseMode(int bState)
    {
        var windowHandle = PlatformFunctions.GetWindowHandle();
        var glfw = PlatformFunctions.GetGlfw();
        
        if (windowHandle == null || glfw == null)
            return;
        
        _relativeMouseMode = (bState != 0);
        
        if (_relativeMouseMode)
        {
            // Enable relative mouse mode (cursor hidden and locked)
            glfw.SetInputMode(windowHandle, CursorStateAttribute.Cursor, CursorModeValue.CursorDisabled);
        }
        else
        {
            // Disable relative mouse mode (cursor visible and free)
            glfw.SetInputMode(windowHandle, CursorStateAttribute.Cursor, CursorModeValue.CursorNormal);
        }
        
        Console.WriteLine($"[NativeAOT] g_pInputSystem_SetRelativeMouseMode: enabled={_relativeMouseMode}");
    }
    
    /// <summary>
    /// Get the current relative mouse mode state.
    /// 
    /// **Source 2 behavior**: Returns if relative mouse mode is enabled.
    /// **Emulation behavior**: Returns the stored relative mouse mode state.
    /// </summary>
    /// <returns>1 if relative mode is enabled, 0 otherwise</returns>
    [UnmanagedCallersOnly]
    public static int g_pInputSystem_GetRelativeMouseMode()
    {
        return _relativeMouseMode ? 1 : 0;
    }
    
    /// <summary>
    /// Set a standard cursor type.
    /// 
    /// **Source 2 behavior**: Sets a standard cursor type (arrow, hand, etc.).
    /// **Emulation behavior**: No-op (cursor types not fully implemented, would require GLFW cursor creation).
    /// </summary>
    /// <param name="cursor">Cursor type code</param>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_SetCursorStandard(long cursor)
    {
        throw new NotImplementedException("g_pInputSystem_SetCursorStandard is not implemented (GLFW cursors missing)");
    }
    
    /// <summary>
    /// Set a user-defined cursor by name.
    /// 
    /// **Source 2 behavior**: Sets a cursor from a user-defined cursor name.
    /// **Emulation behavior**: No-op (user cursors not fully implemented).
    /// </summary>
    /// <param name="pName">Pointer to UTF-8 cursor name</param>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_SetCursorUser(IntPtr pName)
    {
        if (pName == IntPtr.Zero)
            return;
        
        string? name = Marshal.PtrToStringUTF8(pName);
        if (string.IsNullOrEmpty(name))
            return;
        
        throw new NotImplementedException("g_pInputSystem_SetCursorUser is not implemented (user cursor loading missing)");
    }
    
    /// <summary>
    /// Load a cursor from a file.
    /// 
    /// **Source 2 behavior**: Loads a cursor image from a file and registers it with a name.
    /// **Emulation behavior**: Returns 0 (not implemented, would require image loading and GLFW cursor creation).
    /// </summary>
    /// <param name="pFileName">Pointer to UTF-8 file path</param>
    /// <param name="pName">Pointer to UTF-8 cursor name</param>
    /// <param name="nHotX">Hotspot X coordinate</param>
    /// <param name="nHotY">Hotspot Y coordinate</param>
    /// <returns>1 if successful, 0 otherwise</returns>
    [UnmanagedCallersOnly]
    public static int g_pInputSystem_LoadCursorFromFile(IntPtr pFileName, IntPtr pName, int nHotX, int nHotY)
    {
        if (pFileName == IntPtr.Zero || pName == IntPtr.Zero)
            return 0;
        
        string? fileName = Marshal.PtrToStringUTF8(pFileName);
        string? name = Marshal.PtrToStringUTF8(pName);
        
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(name))
            return 0;
        
        throw new NotImplementedException("g_pInputSystem_LoadCursorFromFile is not implemented");
    }
    
    /// <summary>
    /// Shutdown all user-defined cursors.
    /// 
    /// **Source 2 behavior**: Cleans up all user-defined cursors.
    /// **Emulation behavior**: No-op (user cursors not implemented).
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pInputSystem_ShutdownUserCursors()
    {
        throw new NotImplementedException("g_pInputSystem_ShutdownUserCursors is not implemented");
    }
}

