using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using Sandbox;
using Sandbox.Rendering;
using NativeEngine;
using Sandbox.Engine.Emulation.Platform;
using Sandbox.Engine.Emulation.Texture;
using Sandbox.Engine.Emulation.Common;
using Sandbox.Engine;

namespace Sandbox.Engine.Emulation.Rendering;

/// <summary>
/// Structure pour les informations du device de rendu (copie de RenderDeviceInfo_t depuis engine).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RenderDeviceInfo_t
{
    public int m_nVersion;
    public RenderDisplayMode_t m_DisplayMode;
    public int m_nBackBufferCount;
    public RenderMultisampleType m_nMultisampleType;
    public byte m_nModeUsage;
    public byte m_bUseStencil;
    public byte m_bWaitForVSync;
    public byte m_bUsingMultipleWindows;
    public byte m_bIsMainWindow;
    public byte m_padding01;
}

/// <summary>
/// Structure pour le mode d'affichage (copie de RenderDisplayMode_t depuis engine).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RenderDisplayMode_t
{
    public int m_nVersion;
    public int m_nWidth;
    public int m_nHeight;
    public ImageFormat m_Format;
    public int m_nRefreshRateNumerator;
    public int m_nRefreshRateDenominator;
    public uint m_nFlags;
}

/// <summary>
/// Module d'émulation pour RenderDevice (g_pRenderDevice_*).
/// Gère les opérations de rendu de base : textures, shaders, buffers, swap chains, etc.
/// </summary>
public static unsafe class RenderDevice
{
    private static int _nextBindlessIndex = 1;
    private static IntPtr _swapChainTextureHandle = IntPtr.Zero;
    private static uint _swapChainTextureGl = 0;
    private static uint _swapChainFbo = 0;
    private static int _swapChainWidth = 0;
    private static int _swapChainHeight = 0;
    private static readonly object _swapChainLock = new();
    
    /// <summary>
    /// Données internes pour un sampler state émulé.
    /// Pattern identique à TextureData dans TextureSystem.cs et MaterialData dans MaterialSystem.cs
    /// </summary>
    internal class SamplerStateData
    {
        public uint OpenGLSamplerHandle { get; set; } = 0; // Handle OpenGL du sampler object
        public int BindlessIndex { get; set; } = 0; // Index bindless pour les shaders
        public CSamplerStateDesc Desc { get; set; } // Description du sampler pour comparaison
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero; // Pointeur de binding unique (handle HandleManager)
    }
    
    /// <summary>
    /// Données internes pour un buffer GPU émulé.
    /// </summary>
    internal class BufferData
    {
        public uint OpenGLBufferHandle { get; set; } = 0; // Handle OpenGL du buffer
        public GLEnum BufferType { get; set; } = GLEnum.ArrayBuffer; // Type de buffer (VBO, IBO, UBO, etc.)
        public long Size { get; set; } = 0; // Taille du buffer en octets
    }
    
    // Cache pour éviter de créer des sampler states identiques (utilise BindingHandle comme valeur)
    private static readonly Dictionary<CSamplerStateDesc, int> _samplerCache = new();
    
    /// <summary>
    /// Initialise les fonctions natives de RenderDevice.
    /// </summary>
    public static void Init(void** native)
    {
        // Indices depuis Interop.Engine.cs lignes 16217-16257
        native[1470] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&g_pRenderDevice_FindOrCreateSamplerState;
        native[1471] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pRenderDevice_GetSamplerIndex;
        native[1472] = (void*)(delegate* unmanaged<IntPtr, RenderDeviceInfo_t>)&g_pRenderDevice_GetSwapChainInfo;
        native[1473] = (void*)(delegate* unmanaged<IntPtr, long, IntPtr>)&g_pRenderDevice_FindOrCreateFileTexture;
        // native[1474] est g_pRenderDevice_FindOrCreateTexture2 - déjà dans EngineExports.cs
        native[1475] = (void*)(delegate* unmanaged<IntPtr, Color32, void>)&g_pRenderDevice_ClearTexture;
        native[1476] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, IntPtr, void>)&g_pRenderDevice_AsyncSetTextureData2;
        native[1477] = (void*)(delegate* unmanaged<IntPtr, long, IntPtr>)&g_pRenderDevice_GetSwapChainTexture;
        native[1478] = (void*)(delegate* unmanaged<IntPtr, float*, uint*, int>)&g_pRenderDevice_GetGPUFrameTimeMS;
        native[1479] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&g_pRenderDevice_GetTextureDesc;
        native[1480] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&g_pRenderDevice_GetOnDiskTextureDesc;
        native[1481] = (void*)(delegate* unmanaged<IntPtr, long>)&g_pRenderDevice_GetTextureMultisampleType;
        native[1482] = (void*)(delegate* unmanaged<uint, IntPtr>)&g_pRenderDevice_CreateRenderContext;
        native[1483] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pRenderDevice_ReleaseRenderContext;
        native[1484] = (void*)(delegate* unmanaged<IntPtr, NativeRect*, int, int, NativeRect*, IntPtr, long, int, int>)&g_pRenderDevice_ReadTexturePixels;
        native[1485] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pRenderDevice_DestroySwapChain;
        native[1486] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pRenderDevice_Present;
        native[1487] = (void*)(delegate* unmanaged<void>)&g_pRenderDevice_Flush;
        native[1488] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pRenderDevice_CanRenderToSwapChain;
        native[1489] = (void*)(delegate* unmanaged<int>)&g_pRenderDevice_IsUsing32BitDepthBuffer;
        native[1490] = (void*)(delegate* unmanaged<IntPtr, Vector2>)&g_pRenderDevice_GetBackbufferDimensions;
        native[1491] = (void*)(delegate* unmanaged<long, IntPtr, uint, IntPtr, IntPtr, IntPtr>)&g_pRenderDevice_CompileAndCreateShader;
        native[1492] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pRenderDevice_GetTextureLastUsed;
        native[1493] = (void*)(delegate* unmanaged<uint, void>)&g_pRenderDevice_UnThrottleTextureStreamingForNFrames;
        native[1494] = (void*)(delegate* unmanaged<int>)&g_pRenderDevice_GetNumTextureLoadsInFlight;
        native[1495] = (void*)(delegate* unmanaged<int, void>)&g_pRenderDevice_SetForcePreloadStreamingData;
        native[1496] = (void*)(delegate* unmanaged<long>)&g_pRenderDevice_GetRenderDeviceAPI;
        native[1497] = (void*)(delegate* unmanaged<IntPtr, int, void>)&g_pRenderDevice_MarkTextureUsed;
        native[1498] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pRenderDevice_IsTextureRenderTarget;
        native[1499] = (void*)(delegate* unmanaged<int>)&g_pRenderDevice_IsRayTracingSupported;
        native[1500] = (void*)(delegate* unmanaged<long, IntPtr, long, IntPtr, IntPtr>)&g_pRenderDevice_CreateGPUBuffer;
        native[1501] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pRenderDevice_DestroyGPUBuffer;
        native[1502] = (void*)(delegate* unmanaged<IntPtr, uint, IntPtr, uint, int>)&g_pRenderDevice_ReadBuffer;
        native[1503] = (void*)(delegate* unmanaged<long, IntPtr>)&g_pRenderDevice_GetDeviceSpecificInfo;
        native[1504] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&g_pRenderDevice_GetGraphicsAPISpecificTextureHandle;
        native[1505] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&g_pRenderDevice_GetDeviceSpecificTexture;
        native[1506] = (void*)(delegate* unmanaged<IntPtr, byte, long, int>)&g_pRenderDevice_GetTextureViewIndex;
        native[1507] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&g_pRenderDevice_GetTextureResidencyInfo;
        native[1508] = (void*)(delegate* unmanaged<IntPtr, Vector4>)&g_pRenderDevice_GetSheetInfo;
        native[1509] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pRenderDevice_GetSequenceCount;
        native[1510] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr>)&g_pRenderDevice_GetSequence;
    }
    
    // ========== Sampler State Functions ==========
    
    /// <summary>
    /// Convertit FilterMode vers GLEnum pour OpenGL.
    /// </summary>
    private static GLEnum ConvertFilterMode(FilterMode filterMode)
    {
        return filterMode switch
        {
            FilterMode.Point => GLEnum.Nearest,
            FilterMode.Bilinear => GLEnum.Linear,
            FilterMode.Trilinear => GLEnum.LinearMipmapLinear,
            FilterMode.Anisotropic => GLEnum.LinearMipmapLinear, // Anisotropic sera géré séparément
            _ => GLEnum.Linear
        };
    }
    
    /// <summary>
    /// Convertit TextureAddressMode vers GLEnum pour OpenGL.
    /// </summary>
    private static GLEnum ConvertAddressMode(TextureAddressMode addressMode)
    {
        return addressMode switch
        {
            TextureAddressMode.Wrap => GLEnum.Repeat,
            TextureAddressMode.Mirror => GLEnum.MirroredRepeat,
            TextureAddressMode.Clamp => GLEnum.ClampToEdge,
            TextureAddressMode.Border => GLEnum.ClampToBorder,
            TextureAddressMode.MirrorOnce => GLEnum.MirrorClampToEdge,
            _ => GLEnum.Repeat
        };
    }
    
    /// <summary>
    /// Crée un sampler object OpenGL avec les paramètres spécifiés.
    /// </summary>
    private static uint CreateOpenGLSampler(CSamplerStateDesc desc, GL gl)
    {
        if (gl == null) return 0;
        
        uint samplerHandle = 0;
        gl.GenSamplers(1, &samplerHandle);
        
        if (samplerHandle == 0)
        {
            Console.WriteLine("[NativeAOT] Failed to create OpenGL sampler object");
            return 0;
        }
        
        // Convertir FilterMode
        FilterMode filterMode = (FilterMode)desc.m_nFilterMode;
        GLEnum minFilter = ConvertFilterMode(filterMode);
        GLEnum magFilter = filterMode == FilterMode.Point ? GLEnum.Nearest : GLEnum.Linear;
        
        // Configurer les paramètres de filtrage
        gl.SamplerParameter(samplerHandle, GLEnum.TextureMinFilter, (int)minFilter);
        gl.SamplerParameter(samplerHandle, GLEnum.TextureMagFilter, (int)magFilter);
        
        // Configurer les modes d'adressage
        TextureAddressMode addressU = (TextureAddressMode)desc.m_nAddressU;
        TextureAddressMode addressV = (TextureAddressMode)desc.m_nAddressV;
        TextureAddressMode addressW = (TextureAddressMode)desc.m_nAddressW;
        
        gl.SamplerParameter(samplerHandle, GLEnum.TextureWrapS, (int)ConvertAddressMode(addressU));
        gl.SamplerParameter(samplerHandle, GLEnum.TextureWrapT, (int)ConvertAddressMode(addressV));
        gl.SamplerParameter(samplerHandle, GLEnum.TextureWrapR, (int)ConvertAddressMode(addressW));
        
        // Configurer l'anisotropie si disponible
        if (filterMode == FilterMode.Anisotropic && desc.m_nAnisoExp > 0)
        {
            float maxAniso = MathF.Pow(2.0f, desc.m_nAnisoExp);
            gl.SamplerParameter(samplerHandle, GLEnum.TextureMaxAnisotropy, maxAniso);
        }
        
        // Configurer le MIP LOD bias
        if (desc.m_nMipLodBias != 0)
        {
            float lodBias = desc.m_nMipLodBias / 8.0f; // Convertir depuis le format 8-bit
            if (desc.m_nMipLodBiasSign != 0) lodBias = -lodBias;
            gl.SamplerParameter(samplerHandle, GLEnum.TextureLodBias, lodBias);
        }
        
        // Configurer les limites LOD
        gl.SamplerParameter(samplerHandle, GLEnum.TextureMinLod, desc.m_nMinLod);
        gl.SamplerParameter(samplerHandle, GLEnum.TextureMaxLod, desc.m_nMaxLod);
        
        // Configurer la couleur de bordure si nécessaire
        if (addressU == TextureAddressMode.Border || addressV == TextureAddressMode.Border || addressW == TextureAddressMode.Border)
        {
            // Extraire la couleur depuis m_nBorderColor8Bit (format 0xAABBGGRR)
            Color32 borderColor = new Color32(desc.m_nBorderColor8Bit);
            float[] borderColorF = new float[4] 
            { 
                borderColor.r / 255.0f, 
                borderColor.g / 255.0f, 
                borderColor.b / 255.0f, 
                borderColor.a / 255.0f 
            };
            fixed (float* colorPtr = borderColorF)
            {
                gl.SamplerParameter(samplerHandle, GLEnum.TextureBorderColor, colorPtr);
            }
        }
        
        return samplerHandle;
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_FindOrCreateSamplerState(IntPtr samplerDescPtr)
    {
        if (samplerDescPtr == IntPtr.Zero)
        {
            Console.WriteLine("[NativeAOT] g_pRenderDevice_FindOrCreateSamplerState: samplerDescPtr is null");
            return IntPtr.Zero;
        }
        
        var gl = PlatformFunctions.GetGL();
        if (gl == null)
        {
            Console.WriteLine("[NativeAOT] g_pRenderDevice_FindOrCreateSamplerState: OpenGL not initialized");
            return IntPtr.Zero;
        }
        
        // Lire la structure CSamplerStateDesc depuis le pointeur
        CSamplerStateDesc desc = *(CSamplerStateDesc*)samplerDescPtr;
        
        // Vérifier le cache pour éviter de créer des sampler states identiques
        lock (_samplerCache)
        {
            if (_samplerCache.TryGetValue(desc, out int cachedBindingHandle))
            {
                // Trouver un handle existant pour ce sampler via le BindingHandle
                int existingHandle = HandleManager.GetHandleByBindingHandle(cachedBindingHandle);
                if (existingHandle != 0)
                {
                    // Obtenir tous les handles pour ce sampler et retourner le premier
                    var allHandles = HandleManager.GetAllHandles(existingHandle);
                    if (allHandles.Length > 0)
                    {
                        // Copier le handle pour incrémenter le compteur de références
                        int newHandle = HandleManager.CopyHandle(allHandles[0]);
                        if (newHandle != 0)
                        {
                            return (IntPtr)newHandle;
                        }
                    }
                }
            }
        }
        
        // Créer un nouveau sampler object OpenGL
        uint openGLSamplerHandle = CreateOpenGLSampler(desc, gl);
        if (openGLSamplerHandle == 0)
        {
            Console.WriteLine("[NativeAOT] g_pRenderDevice_FindOrCreateSamplerState: Failed to create OpenGL sampler");
            return IntPtr.Zero;
        }
        
        // Créer les données du sampler state
        var samplerData = new SamplerStateData
        {
            OpenGLSamplerHandle = openGLSamplerHandle,
            Desc = desc,
            BindlessIndex = System.Threading.Interlocked.Increment(ref _nextBindlessIndex)
        };
        
        // Enregistrer dans HandleManager pour obtenir un handle unique
        int handle = HandleManager.Register(samplerData);
        if (handle == 0) return IntPtr.Zero;
        
        int bindingHandle = HandleManager.GetBindingHandle(handle);
        samplerData.BindingPtr = (IntPtr)bindingHandle;
        
        // Enregistrer dans l'index OpenGLHandle pour recherche O(1)
        HandleManager.RegisterOpenGLHandleIndex(openGLSamplerHandle, bindingHandle);
        
        // Mettre en cache avec le BindingHandle
        lock (_samplerCache)
        {
            _samplerCache[desc] = bindingHandle;
        }
        
        Console.WriteLine($"[NativeAOT] g_pRenderDevice_FindOrCreateSamplerState: Created sampler {handle} with OpenGL handle {openGLSamplerHandle}, bindless index {samplerData.BindlessIndex}");
        
        return (IntPtr)handle;
    }
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_GetSamplerIndex(IntPtr samplerState)
    {
        if (samplerState == IntPtr.Zero)
        {
            Console.WriteLine("[NativeAOT] g_pRenderDevice_GetSamplerIndex: samplerState is null");
            return 0;
        }
        
        var samplerData = HandleManager.Get<SamplerStateData>((int)samplerState);
        if (samplerData != null)
        {
            return samplerData.BindlessIndex;
        }
        
        Console.WriteLine($"[NativeAOT] g_pRenderDevice_GetSamplerIndex: Sampler state {samplerState} not found");
        return 0;
    }
    
    // ========== Swap Chain Functions ==========
    
    [UnmanagedCallersOnly]
    internal static RenderDeviceInfo_t g_pRenderDevice_GetSwapChainInfo(IntPtr swapChain)
    {
        var glfw = PlatformFunctions.GetGlfw();
        var windowHandle = PlatformFunctions.GetWindowHandle();
        
        if (glfw == null || windowHandle == null)
        {
            return default;
        }
        
        glfw.GetFramebufferSize(windowHandle, out int width, out int height);
        
        var info = new RenderDeviceInfo_t
        {
            m_nVersion = 1,
            m_DisplayMode = new RenderDisplayMode_t
            {
                m_nVersion = 1,
                m_nWidth = width,
                m_nHeight = height,
                m_Format = ImageFormat.RGBA8888,
                m_nRefreshRateNumerator = 60,
                m_nRefreshRateDenominator = 1,
                m_nFlags = 0
            },
            m_nBackBufferCount = 2, // double buffering effectif sur GLFW
            m_nMultisampleType = RenderMultisampleType.RENDER_MULTISAMPLE_NONE,
            m_nModeUsage = 0,
            m_bUseStencil = 0,
            m_bWaitForVSync = 0,
            m_bUsingMultipleWindows = 0,
            m_bIsMainWindow = 1,
            m_padding01 = 0
        };
        
        return info;
    }
    
    // Handle spécial pour le backbuffer du swap chain
    private static IntPtr _swapChainBackbufferHandle = IntPtr.Zero;
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_GetSwapChainTexture(IntPtr swapChain, long bufferType)
    {
        return EnsureSwapChainTexture();
    }
    
    [UnmanagedCallersOnly]
    public static void g_pRenderDevice_DestroySwapChain(IntPtr hSwapChain)
    {
        lock (_swapChainLock)
        {
            var gl = PlatformFunctions.GetGL();

            if (gl != null)
            {
                if (_swapChainTextureGl != 0)
                {
                    gl.DeleteTexture(_swapChainTextureGl);
                    _swapChainTextureGl = 0;
                }
                if (_swapChainFbo != 0)
                {
                    gl.DeleteFramebuffer(_swapChainFbo);
                    _swapChainFbo = 0;
                }
            }

            if (_swapChainTextureHandle != IntPtr.Zero)
            {
                HandleManager.Unregister((int)_swapChainTextureHandle);
                _swapChainTextureHandle = IntPtr.Zero;
            }
        }
    }
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_Present(IntPtr chain)
    {
        PresentManaged();
        return 1;
    }
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_CanRenderToSwapChain(IntPtr chain)
    {
        // Toujours possible avec OpenGL
        return 1; // Oui, on peut rendre vers le swap chain
    }

    /// <summary>
    /// Présente le FBO de swapchain vers le framebuffer par défaut (appel managé).
    /// </summary>
    internal static void PresentManaged()
    {
        var glfw = PlatformFunctions.GetGlfw();
        var windowHandle = PlatformFunctions.GetWindowHandle();
        var gl = PlatformFunctions.GetGL();

        if (glfw == null || windowHandle == null || gl == null) return;

        EnsureSwapChainTexture();

        if (_swapChainFbo != 0 && _swapChainTextureGl != 0)
        {
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _swapChainFbo);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            gl.BlitFramebuffer(0, 0, _swapChainWidth, _swapChainHeight,
                               0, 0, _swapChainWidth, _swapChainHeight,
                               (uint)ClearBufferMask.ColorBufferBit, (GLEnum)BlitFramebufferFilter.Linear);
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        }

        glfw.SwapBuffers(windowHandle);
    }
    
    [UnmanagedCallersOnly]
    public static Vector2 g_pRenderDevice_GetBackbufferDimensions(IntPtr chain)
    {
        var glfw = PlatformFunctions.GetGlfw();
        var windowHandle = PlatformFunctions.GetWindowHandle();
        
        if (glfw == null || windowHandle == null)
        {
            return new Vector2(1280, 720); // Default
        }
        
        glfw.GetFramebufferSize(windowHandle, out int width, out int height);
        return new Vector2(width, height);
    }
    
    // ========== Texture Functions ==========
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_FindOrCreateFileTexture(IntPtr pFileName, long nLoadMode)
    {
        var gl = PlatformFunctions.GetGL();
        if (gl == null || pFileName == IntPtr.Zero)
            return IntPtr.Zero;
        
        string fileName = Marshal.PtrToStringUTF8(pFileName) ?? "";
        if (string.IsNullOrEmpty(fileName))
            return IntPtr.Zero;
        
        // Simple 1x1 texture placeholder until real loader exists
        uint texHandle = 0;
        gl.GenTextures(1, &texHandle);
        if (texHandle == 0)
            return IntPtr.Zero;
        
        gl.BindTexture(GLEnum.Texture2D, texHandle);
        byte[] pixel = new byte[] { 255, 255, 255, 255 };
        fixed (byte* p = pixel)
        {
            gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba, 1, 1, 0, GLEnum.Rgba, GLEnum.UnsignedByte, p);
        }
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        
        var textureHandle = Texture.TextureSystem.CreateTextureWithOpenGLHandle(fileName, texHandle);
        Console.WriteLine($"[NativeAOT] g_pRenderDevice_FindOrCreateFileTexture: {fileName}, loadMode={nLoadMode}, gl={texHandle}, handle={textureHandle}");
        return textureHandle;
    }
    
    [UnmanagedCallersOnly]
    public static void g_pRenderDevice_ClearTexture(IntPtr hTexture, Color32 color)
    {
        var gl = PlatformFunctions.GetGL();
        if (gl == null) return;
        
        // Récupérer le handle OpenGL depuis TextureSystem
        var textureData = TextureSystem.GetTextureData(hTexture);
        if (textureData == null || textureData.OpenGLHandle == 0) return;
        
        gl.BindTexture(GLEnum.Texture2D, textureData.OpenGLHandle);
        
        // Créer un buffer temporaire avec la couleur
        byte[] colorData = new byte[4] { color.r, color.g, color.b, color.a };
        
        fixed (byte* data = colorData)
        {
            gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, 1, 1, GLEnum.Rgba, GLEnum.UnsignedByte, data);
        }
    }
    
    [UnmanagedCallersOnly]
    public static void g_pRenderDevice_AsyncSetTextureData2(IntPtr hTexture, IntPtr pData, int nDataSize, IntPtr rect)
    {
        // TODO: Implémenter la mise à jour asynchrone des données de texture
        // Pour l'instant, utiliser une mise à jour synchrone
        var gl = PlatformFunctions.GetGL();
        if (gl == null) return;
        
        var textureData = TextureSystem.GetTextureData(hTexture);
        if (textureData == null || textureData.OpenGLHandle == 0) return;
        
        if (pData == IntPtr.Zero || nDataSize <= 0)
            return;
        
        gl.BindTexture(GLEnum.Texture2D, textureData.OpenGLHandle);
        
        // Lire Rect3D depuis rect si non null
        if (rect != IntPtr.Zero)
        {
            Rect3D* rectPtr = (Rect3D*)rect;
            // TODO: Utiliser rectPtr pour mettre à jour seulement une partie de la texture
        }
        
        // Mise à jour synchrone pour l'instant (TODO: rendre asynchrone)
        // Note: Pour une vraie implémentation asynchrone, il faudrait utiliser des command buffers
        gl.TexSubImage2D(GLEnum.Texture2D, 0, 0, 0, 1, 1, GLEnum.Rgba, GLEnum.UnsignedByte, (void*)pData);
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_GetTextureDesc(IntPtr hTexture)
    {
        var textureData = TextureSystem.GetTextureData(hTexture);
        if (textureData == null)
        {
            return IntPtr.Zero;
        }
        
        // Allouer une structure CTextureDesc temporaire
        // Note: Cette allocation pourrait fuir, mais c'est temporaire
        var desc = new CTextureDesc
        {
            m_nWidth = 1,
            m_nHeight = 1,
            m_nDepth = 1,
            m_nNumMipLevels = 1,
            m_nImageFormat = ImageFormat.RGBA8888
        };
        
        // TODO: Stocker les vraies dimensions dans TextureData
        return (IntPtr)Unsafe.AsPointer(ref desc);
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_GetOnDiskTextureDesc(IntPtr hTexture)
    {
        // Pour l'instant, identique à GetTextureDesc
        // Note: On ne peut pas appeler directement une fonction [UnmanagedCallersOnly]
        // Donc on duplique le code
        var textureData = TextureSystem.GetTextureData(hTexture);
        if (textureData == null)
        {
            return IntPtr.Zero;
        }
        
        var desc = new CTextureDesc
        {
            m_nWidth = 1,
            m_nHeight = 1,
            m_nDepth = 1,
            m_nNumMipLevels = 1,
            m_nImageFormat = ImageFormat.RGBA8888
        };
        
        return (IntPtr)Unsafe.AsPointer(ref desc);
    }
    
    [UnmanagedCallersOnly]
    public static long g_pRenderDevice_GetTextureMultisampleType(IntPtr hTexture)
    {
        // OpenGL n'utilise généralement pas de multisampling pour les textures 2D simples
        // Retourner RENDER_MULTISAMPLE_NONE par défaut pour permettre au moteur de fonctionner
        return (long)RenderMultisampleType.RENDER_MULTISAMPLE_NONE;
    }
    
    // Frame counter pour suivre l'utilisation des textures
    private static uint _frameCounter = 0;
    
    // Dictionary pour stocker le dernier frame d'utilisation par texture
    private static readonly Dictionary<IntPtr, uint> _textureLastUsed = new();
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_GetTextureLastUsed(IntPtr hTexture)
    {
        if (hTexture == IntPtr.Zero)
            return 0;
        
        // Retourner le frame number de la dernière utilisation
        lock (_textureLastUsed)
        {
            if (_textureLastUsed.TryGetValue(hTexture, out uint lastFrame))
                return (int)lastFrame;
        }
        
        return 0; // Texture jamais utilisée
    }
    
    [UnmanagedCallersOnly]
    public static void g_pRenderDevice_MarkTextureUsed(IntPtr hTexture, int requiredMipSize)
    {
        if (hTexture == IntPtr.Zero)
            return;
        
        // Incrémenter le frame counter et marquer la texture comme utilisée
        lock (_textureLastUsed)
        {
            _frameCounter++;
            _textureLastUsed[hTexture] = _frameCounter;
        }
    }
    
    // Dictionary pour stocker les textures qui sont des render targets
    private static readonly HashSet<IntPtr> _renderTargetTextures = new();
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_IsTextureRenderTarget(IntPtr hTexture)
    {
        if (hTexture == IntPtr.Zero)
            return 0;
        
        // Vérifier si la texture est un render target
        lock (_renderTargetTextures)
        {
            return _renderTargetTextures.Contains(hTexture) ? 1 : 0;
        }
    }
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_GetTextureViewIndex(IntPtr hTexture, byte nViewType, long nDimension)
    {
        // Pour OpenGL, retourner 0 par défaut (pas de système de vue comme Vulkan)
        // TODO: Implémenter un système de tracking des vues de texture si nécessaire
        return 0;
    }
    
    [UnmanagedCallersOnly]
    public static void g_pRenderDevice_GetTextureResidencyInfo(IntPtr hTexture, IntPtr pResidencyInfo)
    {
        // No-op pour OpenGL (pas de système de residency comme certaines APIs)
        // Si pResidencyInfo n'est pas null, on pourrait remplir une structure, mais pour l'instant c'est un no-op
        // TODO: Implémenter si nécessaire pour compatibilité avec d'autres APIs
    }
    
    /// <summary>
    /// Helper interne pour obtenir le nombre de séquences d'une texture.
    /// </summary>
    private static int GetSequenceCountInternal(IntPtr texture)
    {
        // Retourner 0 car la plupart des textures n'ont pas de séquences d'animation
        // Les textures animées (sprite sheets) auront un nombre > 0
        // TODO: Implémenter la lecture des métadonnées de texture pour détecter les textures animées
        // Pour l'instant, toutes les textures sont considérées comme non-animées
        if (texture == IntPtr.Zero) return 0;
        
        // Vérifier si la texture existe dans TextureSystem
        var textureData = TextureSystem.GetTextureData(texture);
        if (textureData == null) return 0;
        
        // Pour l'instant, retourner 0 (textures non-animées par défaut)
        // Plus tard, on pourra ajouter un champ SequenceCount dans TextureData
        // et le lire depuis les métadonnées de la texture lors du chargement
        return 0;
    }
    
    [UnmanagedCallersOnly]
    public static Vector4 g_pRenderDevice_GetSheetInfo(IntPtr texture)
    {
        // Retourner des valeurs par défaut pour les sprite sheets
        // Vector4 contient les informations de sheet (uv offset, scale, etc.)
        // Format: (baseV, width, height, sequenceCount)
        // Pour les textures non-animées, retourner (0, 1, 1, 0)
        // TODO: Implémenter la lecture des métadonnées de texture pour les textures animées
        int sequenceCount = GetSequenceCountInternal(texture);
        return new Vector4(0.0f, 1.0f, 1.0f, sequenceCount);
    }
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_GetSequenceCount(IntPtr texture)
    {
        return GetSequenceCountInternal(texture);
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_GetSequence(IntPtr texture, int index)
    {
        // Si pas de séquences, retourner IntPtr.Zero
        int sequenceCount = GetSequenceCountInternal(texture);
        if (sequenceCount <= 0 || index < 0 || index >= sequenceCount)
        {
            return IntPtr.Zero;
        }
        
        // TODO: Implémenter la lecture des métadonnées de texture pour les textures animées
        // Pour l'instant, retourner IntPtr.Zero car toutes les textures sont non-animées
        // Plus tard, on pourra allouer une structure SheetSequence_t avec Marshal.AllocHGlobal
        // et la remplir avec les données de la séquence depuis les métadonnées
        throw new NotImplementedException(
            "g_pRenderDevice_GetSequence: Animated textures not yet implemented in the linux emulation layer");
    }
    
    // ========== Render Context Functions ==========
    
    // Dictionary pour stocker les render contexts
    private static readonly Dictionary<IntPtr, object> _renderContexts = new();
    private static int _nextRenderContextHandle = 10000;
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_CreateRenderContext(uint flags)
    {
        // Créer un nouveau render context (pour OpenGL, c'est généralement le même contexte)
        // Retourner un handle unique pour le contexte
        int handle = System.Threading.Interlocked.Increment(ref _nextRenderContextHandle);
        IntPtr contextHandle = (IntPtr)handle;
        
        lock (_renderContexts)
        {
            _renderContexts[contextHandle] = new object(); // Placeholder
        }
        
        Console.WriteLine($"[NativeAOT] g_pRenderDevice_CreateRenderContext: flags={flags}, handle={handle}");
        return contextHandle;
    }
    
    [UnmanagedCallersOnly]
    public static void g_pRenderDevice_ReleaseRenderContext(IntPtr context)
    {
        if (context == IntPtr.Zero)
            return;
        
        // Retirer le contexte du dictionnaire
        lock (_renderContexts)
        {
            _renderContexts.Remove(context);
        }
    }
    
    // ========== Shader Functions ==========
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_CompileAndCreateShader(long nType, IntPtr pProgram, uint nBufLen, IntPtr pShaderVersion, IntPtr pDebugName)
    {
        var gl = PlatformFunctions.GetGL();
        if (gl == null) return IntPtr.Zero;
        
        string debugName = pDebugName != IntPtr.Zero ? Marshal.PtrToStringUTF8(pDebugName) ?? "" : "";
        
        // Program string comes as a buffer; interpret as UTF8
        string? source = pProgram != IntPtr.Zero && nBufLen > 0
            ? Marshal.PtrToStringUTF8(pProgram, (int)nBufLen)
            : null;
        
        if (string.IsNullOrEmpty(source))
        {
            Console.WriteLine($"[NativeAOT] g_pRenderDevice_CompileAndCreateShader: empty source ({debugName})");
            return IntPtr.Zero;
        }
        
        ShaderType shaderType = nType switch
        {
            0 => ShaderType.VertexShader,
            1 => ShaderType.FragmentShader,
            _ => ShaderType.FragmentShader
        };
        
        uint shader = gl.CreateShader(shaderType);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compileStatus);
        if (compileStatus == 0)
        {
            string info = gl.GetShaderInfoLog(shader);
            Console.WriteLine($"[NativeAOT] Shader compile failed ({debugName}): {info}");
            gl.DeleteShader(shader);
            return IntPtr.Zero;
        }
        
        uint program = gl.CreateProgram();
        gl.AttachShader(program, shader);
        gl.LinkProgram(program);
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linkStatus);
        gl.DeleteShader(shader);
        
        if (linkStatus == 0)
        {
            string info = gl.GetProgramInfoLog(program);
            Console.WriteLine($"[NativeAOT] Program link failed ({debugName}): {info}");
            gl.DeleteProgram(program);
            return IntPtr.Zero;
        }
        
        // Register as handle
        int handle = HandleManager.Register(program);
        Console.WriteLine($"[NativeAOT] g_pRenderDevice_CompileAndCreateShader: type={nType}, debugName={debugName}, program={program}, handle={handle}");
        return (IntPtr)handle;
    }
    
    // ========== GPU Buffer Functions ==========
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_CreateGPUBuffer(long nType, IntPtr pDesc, long nInitialDataSize, IntPtr pInitialData)
    {
        var gl = PlatformFunctions.GetGL();
        if (gl == null || pDesc == IntPtr.Zero)
            return IntPtr.Zero;
        
        BufferDesc* desc = (BufferDesc*)pDesc;
        
        uint bufferHandle = 0;
        gl.GenBuffers(1, &bufferHandle);
        if (bufferHandle == 0) return IntPtr.Zero;
        
        GLEnum bufferType = nType switch
        {
            0 => GLEnum.ArrayBuffer,
            1 => GLEnum.ElementArrayBuffer,
            2 => GLEnum.UniformBuffer,
            3 => GLEnum.ShaderStorageBuffer,
            _ => GLEnum.ArrayBuffer
        };
        
        gl.BindBuffer(bufferType, bufferHandle);
        
        long bufferSize = (long)desc->m_nElementCount * desc->m_nElementSizeInBytes;
        if (bufferSize < 0) bufferSize = 0;
        
        if (pInitialData != IntPtr.Zero && nInitialDataSize > 0)
        {
            gl.BufferData(bufferType, (nuint)nInitialDataSize, (void*)pInitialData, GLEnum.StaticDraw);
        }
        else
        {
            gl.BufferData(bufferType, (nuint)bufferSize, null, GLEnum.DynamicDraw);
        }
        
        var bufferData = new BufferData
        {
            OpenGLBufferHandle = bufferHandle,
            BufferType = bufferType,
            Size = bufferSize
        };
        int handle = HandleManager.Register(bufferData);
        
        Console.WriteLine($"[NativeAOT] g_pRenderDevice_CreateGPUBuffer: type={nType}, OpenGL handle={bufferHandle}, handle={handle}, size={bufferSize}");
        return (IntPtr)handle;
    }
    
    [UnmanagedCallersOnly]
    public static void g_pRenderDevice_DestroyGPUBuffer(IntPtr hBuffer)
    {
        if (hBuffer == IntPtr.Zero)
            return;
        
        var gl = PlatformFunctions.GetGL();
        if (gl == null) return;
        
        int handle = (int)hBuffer;
        var bufferData = HandleManager.Get<BufferData>(handle);
        
        if (bufferData != null && bufferData.OpenGLBufferHandle != 0)
        {
            uint bufferHandle = bufferData.OpenGLBufferHandle;
            gl.DeleteBuffers(1, &bufferHandle);
        }
        
        HandleManager.Unregister(handle);
        Console.WriteLine($"[NativeAOT] g_pRenderDevice_DestroyGPUBuffer: handle={handle}");
    }
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_ReadBuffer(IntPtr hBuffer, uint nOffset, IntPtr pData, uint nDataSize)
    {
        if (hBuffer == IntPtr.Zero || pData == IntPtr.Zero || nDataSize == 0)
            return 0;
        
        var gl = PlatformFunctions.GetGL();
        if (gl == null) return 0;
        
        int handle = (int)hBuffer;
        var bufferData = HandleManager.Get<BufferData>(handle);
        
        if (bufferData == null || bufferData.OpenGLBufferHandle == 0)
            return 0;
        
        // Bind le buffer et lire les données
        gl.BindBuffer(bufferData.BufferType, bufferData.OpenGLBufferHandle);
        
        // Utiliser glMapBufferRange pour lire les données (OpenGL 3.0+)
        // Note: Cela nécessite que le buffer soit mappé, ce qui peut être coûteux
        void* mappedData = gl.MapBufferRange(bufferData.BufferType, (nint)nOffset, (nuint)nDataSize, (uint)GLEnum.MapReadBit);
        
        if (mappedData == null)
            return 0;
        
        // Copier les données vers le buffer de destination
        System.Buffer.MemoryCopy(mappedData, (void*)pData, nDataSize, nDataSize);
        
        // Unmap le buffer
        gl.UnmapBuffer(bufferData.BufferType);
        
        return 1; // Succès
    }
    
    // ========== Utility Functions ==========
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_GetGPUFrameTimeMS(IntPtr swapChain, float* pGpuFrameTimeMsOut, uint* pFrameNumberOut)
    {
        // Retourner des valeurs fictives pour permettre au moteur de fonctionner
        // TODO: Implémenter un vrai système de mesure de frame time GPU
        if (pGpuFrameTimeMsOut != null)
            *pGpuFrameTimeMsOut = 16.67f; // ~60 FPS par défaut
        if (pFrameNumberOut != null)
            *pFrameNumberOut = 0;
        
        return 1; // Succès
    }
    
    [UnmanagedCallersOnly]
    public static void g_pRenderDevice_Flush()
    {
        var gl = PlatformFunctions.GetGL();
        gl?.Flush();
    }
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_IsUsing32BitDepthBuffer()
    {
        // OpenGL utilise généralement 24 bits pour le depth buffer (pas 32)
        // Retourner 0 pour indiquer qu'on n'utilise pas 32 bits
        return 0;
    }
    
    // Frame counter pour le throttling de texture streaming
    private static uint _textureStreamingThrottleFrames = 0;
    
    [UnmanagedCallersOnly]
    public static void g_pRenderDevice_UnThrottleTextureStreamingForNFrames(uint nNumberOfFramesForUnthrottledTextureLoading)
    {
        // Marquer le nombre de frames pendant lesquelles le streaming de texture ne sera pas throttlé
        _textureStreamingThrottleFrames = nNumberOfFramesForUnthrottledTextureLoading;
    }
    
    // Compteur de textures en cours de chargement
    private static long _textureLoadsInFlight = 0;
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_GetNumTextureLoadsInFlight()
    {
        // Retourner le nombre de textures en cours de chargement
        return (int)System.Threading.Interlocked.Read(ref _textureLoadsInFlight);
    }
    
    // Flag pour forcer le preload des données de streaming
    private static bool _forcePreloadStreamingData = false;
    
    [UnmanagedCallersOnly]
    public static void g_pRenderDevice_SetForcePreloadStreamingData(int bForcePreload)
    {
        _forcePreloadStreamingData = bForcePreload != 0;
    }
    
    [UnmanagedCallersOnly]
    public static long g_pRenderDevice_GetRenderDeviceAPI()
    {
        // Retourner OpenGL comme API
        // Note: RenderDeviceAPI_t n'a pas de valeur pour OpenGL, donc on retourne une valeur personnalisée
        // ou on utilise RENDER_DEVICE_API_EMPTY comme fallback
        // Pour l'instant, retourner -1 pour indiquer OpenGL (non standard mais fonctionnel)
        return -1; // OpenGL (non défini dans RenderDeviceAPI_t)
    }
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_IsRayTracingSupported()
    {
        // OpenGL ne supporte pas le ray tracing natif (contrairement à Vulkan/D3D12)
        return 0; // Non supporté
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_GetDeviceSpecificInfo(long nInfoType)
    {
        // Retourner null pour OpenGL (pas d'info spécifique au device comme D3D12/Vulkan)
        // TODO: Implémenter si nécessaire pour compatibilité avec d'autres APIs
        return IntPtr.Zero;
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_GetGraphicsAPISpecificTextureHandle(IntPtr hTexture)
    {
        var textureData = TextureSystem.GetTextureData(hTexture);
        if (textureData == null)
            return IntPtr.Zero;
        
        // Retourner un pointeur vers le handle OpenGL
        // Note: On ne peut pas prendre l'adresse d'une propriété, donc on utilise une variable locale
        uint handle = textureData.OpenGLHandle;
        return (IntPtr)(&handle);
    }
    
    [UnmanagedCallersOnly]
    public static IntPtr g_pRenderDevice_GetDeviceSpecificTexture(IntPtr hTexture)
    {
        // Identique à GetGraphicsAPISpecificTextureHandle pour OpenGL
        // Note: On ne peut pas appeler directement une fonction [UnmanagedCallersOnly]
        // Donc on duplique le code
        var textureData = TextureSystem.GetTextureData(hTexture);
        if (textureData == null)
            return IntPtr.Zero;
        
        // Retourner un pointeur vers le handle OpenGL
        // Note: On ne peut pas prendre l'adresse d'une propriété, donc on utilise une variable locale
        uint handle = textureData.OpenGLHandle;
        return (IntPtr)(&handle);
    }
    
    [UnmanagedCallersOnly]
    public static int g_pRenderDevice_ReadTexturePixels(IntPtr hTexture, NativeRect* pSrcRect, int nSrcSlice, int nSrcMip, NativeRect* pDstRect, IntPtr pData, long dstFormat, int nDstStride)
    {
        if (hTexture == IntPtr.Zero || pData == IntPtr.Zero)
            return 0;
        
        var gl = PlatformFunctions.GetGL();
        if (gl == null) return 0;
        
        var textureData = TextureSystem.GetTextureData(hTexture);
        if (textureData == null || textureData.OpenGLHandle == 0)
            return 0;
        
        // TODO: Implémenter la lecture des pixels de la texture
        // Cela nécessite de bind la texture, de lire avec glReadPixels, et de convertir le format
        
        throw new NotImplementedException(
            "g_pRenderDevice_ReadTexturePixels not yet implemented in the linux emulation layer");
    }

    /// <summary>
    /// Garantit qu'une texture/FBO de swapchain réelle existe et retourne le handle TextureSystem.
    /// </summary>
    private static IntPtr EnsureSwapChainTexture()
    {
        lock (_swapChainLock)
        {
            var gl = PlatformFunctions.GetGL();
            var glfw = PlatformFunctions.GetGlfw();
            var windowHandle = PlatformFunctions.GetWindowHandle();

            if (gl == null || glfw == null || windowHandle == null)
                return IntPtr.Zero;

            glfw.GetFramebufferSize(windowHandle, out int width, out int height);
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            bool needRecreate = _swapChainTextureGl == 0 || _swapChainFbo == 0 ||
                                width != _swapChainWidth || height != _swapChainHeight;

            if (needRecreate)
            {
                if (_swapChainTextureGl != 0)
                {
                    gl.DeleteTexture(_swapChainTextureGl);
                    _swapChainTextureGl = 0;
                }
                if (_swapChainFbo != 0)
                {
                    gl.DeleteFramebuffer(_swapChainFbo);
                    _swapChainFbo = 0;
                }
                if (_swapChainTextureHandle != IntPtr.Zero)
                {
                    HandleManager.Unregister((int)_swapChainTextureHandle);
                    _swapChainTextureHandle = IntPtr.Zero;
                }

                gl.GenTextures(1, out _swapChainTextureGl);
                gl.BindTexture(GLEnum.Texture2D, _swapChainTextureGl);
                gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8, (uint)width, (uint)height, 0,
                              GLEnum.Rgba, GLEnum.UnsignedByte, null);
                gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
                gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
                gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
                gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);

                gl.GenFramebuffers(1, out _swapChainFbo);
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, _swapChainFbo);
                gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                                        GLEnum.Texture2D, _swapChainTextureGl, 0);
                var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
                if (status != GLEnum.FramebufferComplete)
                {
                    Console.WriteLine($"[NativeAOT] RenderDevice: Swapchain FBO incomplete: {status}");
                }
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

                _swapChainWidth = width;
                _swapChainHeight = height;

                _swapChainTextureHandle = TextureSystem.CreateTextureWithOpenGLHandle("swapchain-backbuffer", _swapChainTextureGl);
                Console.WriteLine($"[NativeAOT] RenderDevice: swapchain recreated {width}x{height}, gl={_swapChainTextureGl}, handle={_swapChainTextureHandle}");
            }

            return _swapChainTextureHandle;
        }
    }

    /// <summary>
    /// Helper managé pour récupérer la texture de swapchain (Handle TextureSystem).
    /// </summary>
    internal static IntPtr GetSwapChainTextureHandle()
    {
        return EnsureSwapChainTexture();
    }
}

