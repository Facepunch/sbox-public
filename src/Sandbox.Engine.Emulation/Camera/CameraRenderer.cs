using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sandbox;
using Sandbox.Engine.Emulation.Common;

namespace Sandbox.Engine.Emulation.Camera;

// Structure publique compatible avec NativeRect (qui est internal dans Sandbox.System)
// Layout identique pour compatibilité binaire avec les signatures natives
// Les signatures natives utilisent NativeRect, mais comme il est internal,
// on utilise cette structure publique avec le même layout (marshalling compatible)
[StructLayout(LayoutKind.Sequential)]
public struct CameraNativeRect
{
    public int x;
    public int y;
    public int w;
    public int h;
}

/// <summary>
/// Module d'émulation pour CCameraRenderer.
/// Gère le rendu de caméra pour les vues de scène.
/// </summary>
public static unsafe class CameraRenderer
{
    /// <summary>
    /// Initialise les fonctions natives de CCameraRenderer.
    /// </summary>
    public static void Init(void** native)
    {
        // Fonctions principales (indices 74-89)
        native[74] = (void*)(delegate* unmanaged<IntPtr, void>)&CCameraRenderer_DeleteThis;
        native[75] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr>)&CCameraRenderer_Create;
        native[76] = (void*)(delegate* unmanaged<IntPtr, void>)&CCameraRenderer_ClearSceneWorlds;
        native[77] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CCameraRenderer_AddSceneWorld;
        native[78] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CCameraRenderer_SetRenderAttributes;
        native[79] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&CCameraRenderer_Render;
        native[80] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&CCameraRenderer_RenderToTexture;
        native[81] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, void>)&CCameraRenderer_RenderToCubeTexture;
        native[82] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, int, int, void>)&CCameraRenderer_RenderToBitmap;
        native[83] = (void*)(delegate* unmanaged<IntPtr, int, int, int, int, void>)&CCameraRenderer_RenderStereo;
        native[84] = (void*)(delegate* unmanaged<IntPtr, int, int, void>)&CCameraRenderer_SubmitStereo;
        native[85] = (void*)(delegate* unmanaged<IntPtr, int, int, void>)&CCameraRenderer_BlitStereo;
        native[86] = (void*)(delegate* unmanaged<IntPtr, void>)&CCameraRenderer_ClearRenderTags;
        native[87] = (void*)(delegate* unmanaged<IntPtr, void>)&CCameraRenderer_ClearExcludeTags;
        native[88] = (void*)(delegate* unmanaged<IntPtr, uint, void>)&CCameraRenderer_AddRenderTag;
        native[89] = (void*)(delegate* unmanaged<IntPtr, uint, void>)&CCameraRenderer_AddExcludeTag;
        
        // Propriétés Get/Set (indices 90-133)
        native[90] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CCameraRenderer_ViewUniqueId;
        native[91] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CCameraRenderer_ViewUniqueId;
        native[92] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Vector3>)&Get__CCameraRenderer_CameraPosition;
        native[93] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Vector3, void>)&Set__CCameraRenderer_CameraPosition;
        native[94] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Angles>)&Get__CCameraRenderer_CameraRotation;
        native[95] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Angles, void>)&Set__CCameraRenderer_CameraRotation;
        native[96] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, float>)&Get__CCameraRenderer_FieldOfView;
        native[97] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, float, void>)&Set__CCameraRenderer_FieldOfView;
        native[98] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, float>)&Get__CCameraRenderer_ZNear;
        native[99] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, float, void>)&Set__CCameraRenderer_ZNear;
        native[100] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, float>)&Get__CCameraRenderer_ZFar;
        native[101] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, float, void>)&Set__CCameraRenderer_ZFar;
        // Note: Les signatures natives utilisent NativeRect (internal), mais comme nos fonctions sont publiques,
        // on utilise CameraNativeRect qui a le même layout (marshalling compatible via StructLayout)
        native[102] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, CameraNativeRect>)&Get__CCameraRenderer_Rect;
        native[103] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, CameraNativeRect, void>)&Set__CCameraRenderer_Rect;
        native[104] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Vector4>)&Get__CCameraRenderer_Viewport;
        native[105] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Vector4, void>)&Set__CCameraRenderer_Viewport;
        native[106] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Vector4>)&Get__CCameraRenderer_ClipSpaceBounds;
        native[107] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Vector4, void>)&Set__CCameraRenderer_ClipSpaceBounds;
        native[108] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CCameraRenderer_EnablePostprocessing;
        native[109] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CCameraRenderer_EnablePostprocessing;
        native[110] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CCameraRenderer_EnableEngineOverlays;
        native[111] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CCameraRenderer_EnableEngineOverlays;
        native[112] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CCameraRenderer_Ortho;
        native[113] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CCameraRenderer_Ortho;
        native[114] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, float>)&Get__CCameraRenderer_OrthoSize;
        native[115] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, float, void>)&Set__CCameraRenderer_OrthoSize;
        native[116] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CCameraRenderer_NeedTonemapRenderer;
        native[117] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CCameraRenderer_NeedTonemapRenderer;
        native[118] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, long>)&Get__CCameraRenderer_SceneViewFlags;
        native[119] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, long, void>)&Set__CCameraRenderer_SceneViewFlags;
        native[120] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CCameraRenderer_IsRenderingStereo;
        native[121] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CCameraRenderer_IsRenderingStereo;
        native[122] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Vector3>)&Get__CCameraRenderer_MiddleEyePosition;
        native[123] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Vector3, void>)&Set__CCameraRenderer_MiddleEyePosition;
        native[124] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Angles>)&Get__CCameraRenderer_MiddleEyeRotation;
        native[125] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Angles, void>)&Set__CCameraRenderer_MiddleEyeRotation;
        native[126] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Matrix>)&Get__CCameraRenderer_OverrideProjection;
        native[127] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, Matrix, void>)&Set__CCameraRenderer_OverrideProjection;
        native[128] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CCameraRenderer_HasOverrideProjection;
        native[129] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CCameraRenderer_HasOverrideProjection;
        native[130] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CCameraRenderer_FlipX;
        native[131] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CCameraRenderer_FlipX;
        native[132] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__CCameraRenderer_FlipY;
        native[133] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__CCameraRenderer_FlipY;
    }
    
    /// <summary>
    /// Représentation émulée d'un CCameraRenderer.
    /// Stocke l'état de la caméra pour le rendu.
    /// </summary>
    private class EmulatedCameraRenderer
    {
        public int CameraId { get; set; }
        public string Name { get; set; } = "";
        
        // Propriétés de caméra
        public int ViewUniqueId { get; set; }
        public Vector3 CameraPosition { get; set; }
        public Angles CameraRotation { get; set; }
        public float FieldOfView { get; set; } = 75.0f;
        public float ZNear { get; set; } = 0.1f;
        public float ZFar { get; set; } = 10000.0f;
        public CameraNativeRect Rect { get; set; }
        public Vector4 Viewport { get; set; }
        public Vector4 ClipSpaceBounds { get; set; } = new Vector4(-1, -1, 1, 1);
        public bool EnablePostprocessing { get; set; } = true;
        public bool EnableEngineOverlays { get; set; } = true;
        public bool Ortho { get; set; } = false;
        public float OrthoSize { get; set; } = 1.0f;
        public bool NeedTonemapRenderer { get; set; } = true;
        public long SceneViewFlags { get; set; }
        public bool IsRenderingStereo { get; set; } = false;
        public Vector3 MiddleEyePosition { get; set; }
        public Angles MiddleEyeRotation { get; set; }
        public Matrix OverrideProjection { get; set; }
        public bool HasOverrideProjection { get; set; } = false;
        public bool FlipX { get; set; } = false;
        public bool FlipY { get; set; } = false;
        
        // État de rendu
        public IntPtr RenderAttributes { get; set; } = IntPtr.Zero;
        public List<IntPtr> SceneWorlds { get; set; } = new();
        public HashSet<uint> RenderTags { get; set; } = new();
        public HashSet<uint> ExcludeTags { get; set; } = new();
    }
    
    // ========== Fonctions principales ==========
    
    /// <summary>
    /// Supprime un CCameraRenderer.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_DeleteThis(IntPtr self)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null)
        {
            HandleManager.Unregister(handle);
            Console.WriteLine($"[NativeAOT] CCameraRenderer_DeleteThis: handle={handle}");
        }
    }
    
    /// <summary>
    /// Crée un nouveau CCameraRenderer.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CCameraRenderer_Create(IntPtr name, int cameraId)
    {
        var renderer = new EmulatedCameraRenderer
        {
            CameraId = cameraId
        };
        
        // Convertir le nom depuis IntPtr (StringToken)
        if (name != IntPtr.Zero)
        {
            // TODO: Convertir StringToken en string si nécessaire
            renderer.Name = $"Camera_{cameraId}";
        }
        
        int handle = HandleManager.Register(renderer);
        Console.WriteLine($"[NativeAOT] CCameraRenderer_Create: cameraId={cameraId}, handle={handle}");
        return (IntPtr)handle;
    }
    
    /// <summary>
    /// Efface tous les mondes de scène associés.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_ClearSceneWorlds(IntPtr self)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null)
        {
            renderer.SceneWorlds.Clear();
        }
    }
    
    /// <summary>
    /// Ajoute un monde de scène au rendu.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_AddSceneWorld(IntPtr self, IntPtr world)
    {
        if (self == IntPtr.Zero || world == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null)
        {
            renderer.SceneWorlds.Add(world);
        }
    }
    
    /// <summary>
    /// Définit les attributs de rendu.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_SetRenderAttributes(IntPtr self, IntPtr attributes)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null)
        {
            renderer.RenderAttributes = attributes;
        }
    }
    
    /// <summary>
    /// Rend la caméra vers une swap chain.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_Render(IntPtr self, IntPtr targetSwapChain)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer == null) return;
        
        // TODO: Implémenter le rendu réel vers la swap chain
        // Pour l'instant, c'est un stub qui permet au moteur de fonctionner
    }
    
    /// <summary>
    /// Rend la caméra vers une texture.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_RenderToTexture(IntPtr self, IntPtr hTexture, IntPtr parentView)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer == null) return;
        
        // TODO: Implémenter le rendu vers texture
    }
    
    /// <summary>
    /// Rend la caméra vers une texture cubemap.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_RenderToCubeTexture(IntPtr self, IntPtr hTexture, int nSlice)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer == null) return;
        
        // TODO: Implémenter le rendu vers cubemap
    }
    
    /// <summary>
    /// Rend la caméra vers un bitmap.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_RenderToBitmap(IntPtr self, IntPtr pixels, int width, int height, int bytesPerPixel)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer == null) return;
        
        // TODO: Implémenter le rendu vers bitmap
    }
    
    /// <summary>
    /// Rend en mode stéréo.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_RenderStereo(IntPtr self, int eye, int eyeWidth, int eyeHeight, int bSubmitThisEye)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer == null) return;
        
        // TODO: Implémenter le rendu stéréo
    }
    
    /// <summary>
    /// Soumet le rendu stéréo.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_SubmitStereo(IntPtr self, int eyeWidth, int eyeHeight)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer == null) return;
        
        // TODO: Implémenter la soumission stéréo
    }
    
    /// <summary>
    /// Blit le rendu stéréo.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_BlitStereo(IntPtr self, int eyeWidth, int eyeHeight)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer == null) return;
        
        // TODO: Implémenter le blit stéréo
    }
    
    /// <summary>
    /// Efface tous les tags de rendu.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_ClearRenderTags(IntPtr self)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null)
        {
            renderer.RenderTags.Clear();
        }
    }
    
    /// <summary>
    /// Efface tous les tags d'exclusion.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_ClearExcludeTags(IntPtr self)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null)
        {
            renderer.ExcludeTags.Clear();
        }
    }
    
    /// <summary>
    /// Ajoute un tag de rendu.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_AddRenderTag(IntPtr self, uint hash)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null)
        {
            renderer.RenderTags.Add(hash);
        }
    }
    
    /// <summary>
    /// Ajoute un tag d'exclusion.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CCameraRenderer_AddExcludeTag(IntPtr self, uint hash)
    {
        if (self == IntPtr.Zero) return;
        
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null)
        {
            renderer.ExcludeTags.Add(hash);
        }
    }
    
    // ========== Propriétés Get/Set ==========
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__CCameraRenderer_ViewUniqueId(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.ViewUniqueId ?? 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_ViewUniqueId(IntPtr self, int value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.ViewUniqueId = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static Vector3 Get__CCameraRenderer_CameraPosition(IntPtr self)
    {
        if (self == IntPtr.Zero) return Vector3.Zero;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.CameraPosition ?? Vector3.Zero;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_CameraPosition(IntPtr self, Vector3 value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.CameraPosition = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static Angles Get__CCameraRenderer_CameraRotation(IntPtr self)
    {
        if (self == IntPtr.Zero) return Angles.Zero;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.CameraRotation ?? Angles.Zero;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_CameraRotation(IntPtr self, Angles value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.CameraRotation = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static float Get__CCameraRenderer_FieldOfView(IntPtr self)
    {
        if (self == IntPtr.Zero) return 75.0f;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.FieldOfView ?? 75.0f;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_FieldOfView(IntPtr self, float value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.FieldOfView = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static float Get__CCameraRenderer_ZNear(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0.1f;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.ZNear ?? 0.1f;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_ZNear(IntPtr self, float value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.ZNear = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static float Get__CCameraRenderer_ZFar(IntPtr self)
    {
        if (self == IntPtr.Zero) return 10000.0f;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.ZFar ?? 10000.0f;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_ZFar(IntPtr self, float value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.ZFar = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static CameraNativeRect Get__CCameraRenderer_Rect(IntPtr self)
    {
        if (self == IntPtr.Zero) return default;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.Rect ?? default;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_Rect(IntPtr self, CameraNativeRect value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.Rect = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static Vector4 Get__CCameraRenderer_Viewport(IntPtr self)
    {
        if (self == IntPtr.Zero) return Vector4.Zero;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.Viewport ?? Vector4.Zero;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_Viewport(IntPtr self, Vector4 value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.Viewport = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static Vector4 Get__CCameraRenderer_ClipSpaceBounds(IntPtr self)
    {
        if (self == IntPtr.Zero) return new Vector4(-1, -1, 1, 1);
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.ClipSpaceBounds ?? new Vector4(-1, -1, 1, 1);
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_ClipSpaceBounds(IntPtr self, Vector4 value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.ClipSpaceBounds = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__CCameraRenderer_EnablePostprocessing(IntPtr self)
    {
        if (self == IntPtr.Zero) return 1;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return (renderer?.EnablePostprocessing ?? true) ? 1 : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_EnablePostprocessing(IntPtr self, int value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.EnablePostprocessing = value != 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__CCameraRenderer_EnableEngineOverlays(IntPtr self)
    {
        if (self == IntPtr.Zero) return 1;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return (renderer?.EnableEngineOverlays ?? true) ? 1 : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_EnableEngineOverlays(IntPtr self, int value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.EnableEngineOverlays = value != 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__CCameraRenderer_Ortho(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return (renderer?.Ortho ?? false) ? 1 : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_Ortho(IntPtr self, int value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.Ortho = value != 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static float Get__CCameraRenderer_OrthoSize(IntPtr self)
    {
        if (self == IntPtr.Zero) return 1.0f;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.OrthoSize ?? 1.0f;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_OrthoSize(IntPtr self, float value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.OrthoSize = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__CCameraRenderer_NeedTonemapRenderer(IntPtr self)
    {
        if (self == IntPtr.Zero) return 1;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return (renderer?.NeedTonemapRenderer ?? true) ? 1 : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_NeedTonemapRenderer(IntPtr self, int value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.NeedTonemapRenderer = value != 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static long Get__CCameraRenderer_SceneViewFlags(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.SceneViewFlags ?? 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_SceneViewFlags(IntPtr self, long value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.SceneViewFlags = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__CCameraRenderer_IsRenderingStereo(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return (renderer?.IsRenderingStereo ?? false) ? 1 : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_IsRenderingStereo(IntPtr self, int value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.IsRenderingStereo = value != 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static Vector3 Get__CCameraRenderer_MiddleEyePosition(IntPtr self)
    {
        if (self == IntPtr.Zero) return Vector3.Zero;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.MiddleEyePosition ?? Vector3.Zero;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_MiddleEyePosition(IntPtr self, Vector3 value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.MiddleEyePosition = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static Angles Get__CCameraRenderer_MiddleEyeRotation(IntPtr self)
    {
        if (self == IntPtr.Zero) return Angles.Zero;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.MiddleEyeRotation ?? Angles.Zero;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_MiddleEyeRotation(IntPtr self, Angles value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.MiddleEyeRotation = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static Matrix Get__CCameraRenderer_OverrideProjection(IntPtr self)
    {
        if (self == IntPtr.Zero) return Matrix.Identity;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return renderer?.OverrideProjection ?? Matrix.Identity;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_OverrideProjection(IntPtr self, Matrix value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.OverrideProjection = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__CCameraRenderer_HasOverrideProjection(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return (renderer?.HasOverrideProjection ?? false) ? 1 : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_HasOverrideProjection(IntPtr self, int value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.HasOverrideProjection = value != 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__CCameraRenderer_FlipX(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return (renderer?.FlipX ?? false) ? 1 : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_FlipX(IntPtr self, int value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.FlipX = value != 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__CCameraRenderer_FlipY(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        return (renderer?.FlipY ?? false) ? 1 : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__CCameraRenderer_FlipY(IntPtr self, int value)
    {
        if (self == IntPtr.Zero) return;
        int handle = (int)self;
        var renderer = HandleManager.Get<EmulatedCameraRenderer>(handle);
        if (renderer != null) renderer.FlipY = value != 0;
    }
}

