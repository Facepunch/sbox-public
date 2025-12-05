using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Generated;
using Sandbox.Engine.Emulation.Physics;
using Sandbox.Engine;
using NativeEngine;
using Silk.NET.Maths;
using System.IO;
using Silk.NET.GLFW;
using Sandbox;
using Silk.NET.OpenGL;

namespace Sandbox.Engine.Emulation;

public static unsafe class EngineExports_test
{
    private static bool _isReady = false;
    private static Glfw? _glfw;
    private static WindowHandle* _windowHandle;
    private static Silk.NET.OpenGL.GL? _gl;

    /// <summary>
    /// Main engine initialization function called by the managed runtime.
    /// This is the entry point that receives function pointers from both sides.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "igen_engine")]
    public static void IGenEngine(int hash, void* managedFunctions, void* nativeFunctions, int* structSizes)
    {
        Console.WriteLine($"[NativeAOT Engine] igen_engine called with hash: {hash}");

        void** managed = (void**)managedFunctions;
        void** native  = (void**)nativeFunctions;

        // 1. Store managed function pointers (Imports)
        EngineGlue.StoreImports(managed);

        // 2. Fill native function pointers (Exports)
        Exports.FillNativeFunctionsEngine(managed, native, structSizes);
        
        // 3. Patch native function pointers with our implementations
        // Indices calculated from engine.Generated.cs (LineNumber - 13)
        
        // CMtrlSystm2ppSys_Create: Line 511 -> Index 498
        native[498] = (void*)(delegate* unmanaged<void*, void*>)&CMtrlSystm2ppSys_Create;

        // CMtrlSystm2ppSys_Init: Line 513 -> Index 500
        native[500] = (void*)(delegate* unmanaged<void*, int>)&CMtrlSystm2ppSys_Init;

        // CMtrlSystm2ppSys_GetAppWindow: Line 516 -> Index 503
        native[503] = (void*)(delegate* unmanaged<void*, void*>)&CMtrlSystm2ppSys_GetAppWindow;

        // CMtrlSystm2ppSys_CreateAppWindow: Line 544 -> Index 531
        native[531] = (void*)(delegate* unmanaged<void*, void*, int, int, int, int, int, int, void*>)&CreateAppWindow;

        native[1421] = (void*)(delegate* unmanaged< void*, void*, void* >)&GetEngineSwapChainSize;

        native[1474] = (void*)(delegate* unmanaged< void*, int, void*, void*, int, void* >)&FindOrCreateTexture2;
        
        // global_Plat_MessageBox: Line 1584 -> Index 1571
        native[1571] = (void*)(delegate* unmanaged<void*, void*, void*>)&Plat_MessageBox;

        // global_Plat_SetCurrentFrame: Line 1591 -> Index 1578
        native[1578] = (void*)(delegate* unmanaged<ulong, void*>)&Plat_SetCurrentFrame;
        
        // global_GetGameRootFolder: Line 1602 -> Index 1589
        native[1589] = (void*)(delegate* unmanaged<void*>)&GetGameRootFolder;

        // NOTE: SourceEnginePreInit, SourceEngineInit, et SourceEngineFrame sont maintenant gérés par PlatformFunctions.cs
        // Ces patches ont été déplacés vers PlatformFunctions.Init() pour éviter les conflits
        // Les indices 1592, 1593, 1594 sont patchés dans PlatformFunctions.cs
        
        // global_SourceEnginePreInit: Line 1605 -> Index 1592
        // native[1592] = (void*)(delegate* unmanaged<void*, void*, int>)&SourceEnginePreInit; // DÉPLACÉ vers PlatformFunctions
        
        // global_SourceEngineInit: Line 1606 -> Index 1593
        // native[1593] = (void*)(delegate* unmanaged<void*, int>)&SourceEngineInit; // DÉPLACÉ vers PlatformFunctions
        
        // global_SourceEngineFrame: Line 1607 -> Index 1594
        // native[1594] = (void*)(delegate* unmanaged<void*, double, double, int>)&SourceEngineFrame; // DÉPLACÉ vers EngineExports.cs (logique de rendu complexe)
        
        // global_UpdateWindowSize: Line 1609 -> Index 1596
        native[1596] = (void*)(delegate* unmanaged<void*>)&UpdateWindowSize;

        // Physics System Exports
        // g_pPhysicsSystem_CreateWorld: Index 1464
        native[1464] = (void*)(delegate* unmanaged<int>)&Physics_CreateWorld;
        // g_pPhysicsSystem_DestroyWorld: Index 1465
        native[1465] = (void*)(delegate* unmanaged<void*, void>)&Physics_DestroyWorld;
        // IPhysicsWorld_SetWorldReferenceBody: Index 2019
        native[2019] = (void*)(delegate* unmanaged<void*, void*, void>)&Physics_SetWorldReferenceBody;
        // IPhysicsWorld_GetGravity: Index 2022
        native[2022] = (void*)(delegate* unmanaged<void*, Vector3>)&Physics_GetGravity;
        // IPhysicsWorld_SetDebugScene: Index 2041
        native[2041] = (void*)(delegate* unmanaged<void*, void*, void>)&Physics_SetDebugScene;
        // IPhysicsWorld_GetDebugScene: Index 2042
        native[2042] = (void*)(delegate* unmanaged<void*, int>)&Physics_GetDebugScene;
        // IPhysicsWorld_AddBody: Index 2043
        native[2043] = (void*)(delegate* unmanaged<void*, int>)&Physics_AddBody;
        // IPhysicsWorld_Step: Index 2044
        native[2044] = (void*)(delegate* unmanaged<void*, float, void>)&Physics_Step;

        // Audio System Exports
        // CAudioMixBuffer
        native[19] = (void*)(delegate* unmanaged<IntPtr>)&CAudioMixBuffer_Create;
        native[20] = (void*)(delegate* unmanaged<IntPtr, void*>)&CAudioMixBuffer_Dispose;
        native[21] = (void*)(delegate* unmanaged<IntPtr, void*>)&CAudioMixBuffer_GetDataPointer;
        native[22] = (void*)(delegate* unmanaged<IntPtr, void*>)&CAudioMixBuffer_Silence;
        native[23] = (void*)(delegate* unmanaged<IntPtr, float>)&CAudioMixBuffer_AbsLevel;
        native[24] = (void*)(delegate* unmanaged<IntPtr, float>)&CAudioMixBuffer_AverageLevel;
        native[25] = (void*)(delegate* unmanaged<IntPtr, float, float, void*>)&CAudioMixBuffer_Ramp;
        native[26] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void*>)&CAudioMixBuffer_CopyFrom;
        native[27] = (void*)(delegate* unmanaged<IntPtr, IntPtr, float, void*>)&CAudioMixBuffer_Mix;
        native[28] = (void*)(delegate* unmanaged<IntPtr, IntPtr, float, float, void*>)&CAudioMixBuffer_MixRamp;
        // CAudioMixDeviceBuffers
        native[29] = (void*)(delegate* unmanaged<int, IntPtr>)&CdMxDvcBffrs_Create;
        native[30] = (void*)(delegate* unmanaged<IntPtr, void*>)&CdMxDvcBffrs_Destroy;
        native[31] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr>)&CdMxDvcBffrs_GetBuffer;
        // CAudioMixer
        native[32] = (void*)(delegate* unmanaged<IntPtr, void*>)&CAudioMixer_Dispose;
        native[33] = (void*)(delegate* unmanaged<IntPtr, int>)&CAudioMixer_GetSamplePosition;
        native[34] = (void*)(delegate* unmanaged<IntPtr, int>)&CAudioMixer_ShouldContinueMixing;
        native[35] = (void*)(delegate* unmanaged<IntPtr, int, void*>)&CAudioMixer_SetSamplePosition;
        native[36] = (void*)(delegate* unmanaged<IntPtr, uint, void*>)&CAudioMixer_SetSampleEnd;
        native[37] = (void*)(delegate* unmanaged<IntPtr, int, void*>)&CAudioMixer_DelayOrSkipSamples;
        native[38] = (void*)(delegate* unmanaged<IntPtr, int>)&CAudioMixer_IsReadyToMix;
        native[39] = (void*)(delegate* unmanaged<IntPtr, int>)&CAudioMixer_GetPositionForSave;
        native[40] = (void*)(delegate* unmanaged<IntPtr, int, void*>)&CAudioMixer_SetPositionFromSaved;
        native[41] = (void*)(delegate* unmanaged<IntPtr, audio_source_indexstate_t*, void*>)&CAudioMixer_UpdateMixerState;
        native[42] = (void*)(delegate* unmanaged<IntPtr, audio_source_indexstate_t>)&CAudioMixer_GetIndexState;
        native[43] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CAudioMixer_GetSfxTable;
        native[44] = (void*)(delegate* unmanaged<IntPtr, int>)&CAudioMixer_GetSampleCount;
        native[45] = (void*)(delegate* unmanaged<IntPtr, int>)&CAudioMixer_GetChannelCount;
        native[46] = (void*)(delegate* unmanaged<IntPtr, float, int>)&CAudioMixer_SetTimeScale;
        native[47] = (void*)(delegate* unmanaged<IntPtr, int, void*>)&CAudioMixer_EnableLooping;
        native[48] = (void*)(delegate* unmanaged<IntPtr, float, IntPtr, void*>)&CAudioMixer_ReadToBuffer;
        // CAudioProcessor
        native[49] = (void*)(delegate* unmanaged<IntPtr, void*>)&CAudioProcessor_Dispose;
        native[50] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int, void*>)&CAudioProcessor_Process;
        native[51] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, float, int>)&CAudioProcessor_SetControlParameter;
        native[52] = (void*)(delegate* unmanaged<IntPtr, Sandbox.StringToken, Sandbox.StringToken, int>)&CAudioProcessor_SetNameParameter;
        native[53] = (void*)(delegate* unmanaged<int, IntPtr>)&CAudioProcessor_CreateDelay;
        native[54] = (void*)(delegate* unmanaged<int, IntPtr>)&CAudioProcessor_CreatePitchShift;
        // CAudioStreamManaged
        native[55] = (void*)(delegate* unmanaged<int, uint, int>)&CdStrmMngd_Create;
        native[56] = (void*)(delegate* unmanaged<IntPtr, void*>)&CdStrmMngd_Destroy;
        native[57] = (void*)(delegate* unmanaged<IntPtr, IntPtr, uint, uint, void*>)&CdStrmMngd_WriteAudioData;
        native[58] = (void*)(delegate* unmanaged<IntPtr, uint>)&CdStrmMngd_QueuedSampleCount;
        native[59] = (void*)(delegate* unmanaged<IntPtr, uint>)&CdStrmMngd_MaxWriteSampleCount;
        native[60] = (void*)(delegate* unmanaged<IntPtr, uint>)&CdStrmMngd_LatencySamplesCount;
        native[61] = (void*)(delegate* unmanaged<IntPtr, void*>)&CdStrmMngd_Pause;
        native[62] = (void*)(delegate* unmanaged<IntPtr, void*>)&CdStrmMngd_Resume;
        native[63] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CdStrmMngd_GetName;
        native[64] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CdStrmMngd_GetSfxTable;
        // CBinauralEffect
        native[65] = (void*)(delegate* unmanaged<IntPtr>)&CBinauralEffect_Create;
        native[66] = (void*)(delegate* unmanaged<IntPtr, void*>)&CBinauralEffect_Dispose;
        native[67] = (void*)(delegate* unmanaged<IntPtr, Vector3*, float, IntPtr, IntPtr, void*>)&CBinauralEffect_Apply;
    

        _isReady = true;
    }

    [UnmanagedCallersOnly(EntryPoint = "Debug_Error")]
    public static void DebugError(IntPtr message)
    {
        string? msg = Marshal.PtrToStringUTF8(message);
        Console.WriteLine($"[NativeAOT ERROR] {msg}");
    }

    [UnmanagedCallersOnly]
    public static void* CMtrlSystm2ppSys_Create(void* createInfo)
    {
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_Create");
        return (void*)1; // Return a dummy non-null pointer
    }

    [UnmanagedCallersOnly]
    public static int CMtrlSystm2ppSys_Init(void* self)
    {
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_Init");
        return 1; // Success
    }

    [UnmanagedCallersOnly]
    public static void* CMtrlSystm2ppSys_GetAppWindow(void* self)
    {
        Console.WriteLine("[NativeAOT] CMtrlSystm2ppSys_GetAppWindow");
        return (void*)_windowHandle;
    }

    [UnmanagedCallersOnly]
    public static void* FindOrCreateTexture2(void* pResourceName, int bIsAnonymous, void* pDescriptor, void* data, int dataSize)
    {
        if (_gl == null) return null;
        var gl = _gl;

        var desc = (TextureCreationConfig_t*)pDescriptor;

        if (desc->m_nWidth <= 0)
            desc->m_nWidth = 1;

        if (desc->m_nHeight <= 0)
            desc->m_nHeight = 1;

        if (desc->m_nDepth <= 0)
            desc->m_nDepth = 1;

        if (desc->m_nNumMipLevels <= 0)
            desc->m_nNumMipLevels = 1;

        if (desc->m_nImageFormat == ImageFormat.None)
            desc->m_nImageFormat = ImageFormat.RGBA8888;

        desc->m_nDisplayRectWidth = desc->m_nWidth;
        desc->m_nDisplayRectHeight = desc->m_nHeight;

        uint tex;
        gl.GenTextures(1, out tex);
        gl.BindTexture(GLEnum.Texture2D, tex);

        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);

        gl.TexImage2D(
            GLEnum.Texture2D,
            0,
            (int)GLEnum.Rgba8,
            (uint)desc->m_nWidth,
            (uint)desc->m_nHeight,
            0,
            GLEnum.Rgba,
            GLEnum.UnsignedByte,
            data
        );
        return (void*)tex;
    }

    [UnmanagedCallersOnly]
    public static void* Plat_MessageBox(void* title, void* message)
    {
        string? t = Marshal.PtrToStringUTF8((IntPtr)title);
        string? m = Marshal.PtrToStringUTF8((IntPtr)message);
        Console.WriteLine($"[NativeAOT MESSAGEBOX] {t}: {m}");
        return null;
    }

    [UnmanagedCallersOnly]
    public static void* Plat_SetCurrentFrame(ulong frame)
    {
        // Console.WriteLine($"[NativeAOT] Plat_SetCurrentFrame({frame})");
        return null;
    }

    [UnmanagedCallersOnly]
    public static void* GetGameRootFolder()
    {
        string cwd = Directory.GetCurrentDirectory();
        Console.WriteLine($"[NativeAOT] GetGameRootFolder: {cwd}");
        return (void*)Marshal.StringToHGlobalAnsi(cwd); // Leak memory for now
    }

    // NOTE: SourceEnginePreInit a été déplacé vers PlatformFunctions.cs
    // Cette fonction est commentée pour éviter les conflits
    // L'implémentation active se trouve dans PlatformFunctions.SourceEnginePreInit
    /*
    [UnmanagedCallersOnly]
    public static int SourceEnginePreInit(void* lpCmdLine, void* appDict)
    {
        Console.WriteLine("[NativeAOT] SourceEnginePreInit");
        return 1; // Success
    }
    */

    // Internal helper that can be called directly
    private static void* CreateAppWindowInternal(string title, int w, int h)
    {
        Console.WriteLine($"[NativeAOT] Creating Window (GLFW): {title} ({w}x{h})");

        try
        {
            _glfw = Glfw.GetApi();
            if (!_glfw.Init())
            {
                Console.WriteLine("[NativeAOT] GLFW Init failed!");
                return null;
            }

            _glfw.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGL);
            _glfw.WindowHint(WindowHintInt.ContextVersionMajor, 3);
            _glfw.WindowHint(WindowHintInt.ContextVersionMinor, 3);
            _glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);

            _windowHandle = _glfw.CreateWindow(w > 0 ? w : 1280, h > 0 ? h : 720, title, null, null);
            
            if (_windowHandle == null)
            {
                Console.WriteLine("[NativeAOT] GLFW CreateWindow failed!");
                // Try to get error
                byte* errorDesc;
                var errorCode = _glfw.GetError(out errorDesc);
                string? errorStr = Marshal.PtrToStringUTF8((IntPtr)errorDesc);
                Console.WriteLine($"[NativeAOT] GLFW Error: {errorCode} - {errorStr}");
                return null;
            }

            _glfw.MakeContextCurrent(_windowHandle);
            
            // Initialize GL
            _gl = GL.GetApi(new GlfwContext(_glfw, _windowHandle));

            Console.WriteLine($"[NativeAOT] Window Initialized (Handle: {(long)_windowHandle:X})");
            return (void*)_windowHandle;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] Exception in CreateAppWindowInternal: {ex}");
            return null;
        }
    }

    [UnmanagedCallersOnly]
    public static void* CreateAppWindow(void* self, void* pTitle, int nPlatWindowFlags, int x, int y, int w, int h, int nRefreshRateHz)
    {
        string title = Marshal.PtrToStringUTF8((IntPtr)pTitle) ?? "S&box NativeAOT";
        return CreateAppWindowInternal(title, w, h);
    }

    [UnmanagedCallersOnly]
    public static void* GetEngineSwapChainSize( void* w, void* h )
    {
        if (_windowHandle == null || _glfw == null)
            return null;

        int width, height;
        _glfw.GetFramebufferSize(_windowHandle, out width, out height);

        *(int*)w = width;
        *(int*)h = height;

        return null;
    }

    // NOTE: SourceEngineInit a été déplacé vers PlatformFunctions.cs
    // Cette fonction est commentée pour éviter les conflits
    // La création de fenêtre est maintenant gérée par MaterialSystem.CMtrlSystm2ppSys_CreateAppWindow
    /*
    [UnmanagedCallersOnly]
    public static int SourceEngineInit(void* appDict)
    {
        Console.WriteLine("[NativeAOT] SourceEngineInit - Forcing Window Creation");
        
        // Force create window
        CreateAppWindowInternal("S&box NativeAOT", 1280, 720);
        
        return 1; // Success
    }
    */

    [UnmanagedCallersOnly]
    public static int SourceEngineFrame(void* appDict, double currentTime, double previousTime)
    {
        if (_windowHandle == null || _glfw == null) return 0;

        _glfw.PollEvents();
        
        if (_glfw.WindowShouldClose(_windowHandle)) return 0;

        // Render loop would go here
        // _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        
        _glfw.SwapBuffers(_windowHandle);
        
        return 1;
    }

    [UnmanagedCallersOnly]
    public static void* UpdateWindowSize()
    {
        Console.WriteLine("[NativeAOT] UpdateWindowSize");
        return null;
    }

    // Physics Stubs
    [UnmanagedCallersOnly]
    public static int Physics_CreateWorld()
    {
        Console.WriteLine("[NativeAOT] Physics_CreateWorld (Bepu)");
        var world = new BepuPhysicsWorld();
        int handle = HandleManager.Register(world);
        return handle;
    }

    [UnmanagedCallersOnly]
    public static void Physics_DestroyWorld(void* worldPtr)
    {
        int handle = (int)(long)worldPtr;
        var world = HandleManager.Get<BepuPhysicsWorld>(handle);
        world?.Dispose();
        HandleManager.Unregister(handle);
        Console.WriteLine("[NativeAOT] Physics_DestroyWorld (Bepu) handle=" + handle);
    }

    [UnmanagedCallersOnly]
    public static void Physics_SetWorldReferenceBody(void* world, void* body)
    {
        // Console.WriteLine("[NativeAOT] Physics_SetWorldReferenceBody");
    }

    [UnmanagedCallersOnly]
    public static Vector3 Physics_GetGravity(void* worldPtr)
    {
        int handle = (int)(long)worldPtr;
        var world = HandleManager.Get<BepuPhysicsWorld>(handle);
        return world?.Gravity ?? new Vector3(0, 0, -800);
    }

    private static readonly Dictionary<int, IntPtr> _debugScenes = new();

    [UnmanagedCallersOnly]
    public static void Physics_SetDebugScene(void* worldPtr, void* scenePtr)
    {
        int handle = (int)(long)worldPtr;
        IntPtr scene = (IntPtr)scenePtr;
        _debugScenes[handle] = scene;
        Console.WriteLine($"[NativeAOT] Physics_SetDebugScene: world={handle} scene=0x{scene.ToInt64():X}");
    }

    [UnmanagedCallersOnly]
    public static int Physics_GetDebugScene(void* worldPtr)
    {
        int handle = (int)(long)worldPtr;
        if (_debugScenes.TryGetValue(handle, out var scene))
            return (int)scene;
        return 0;
    }

    // IPhysicsBody Stubs
    [UnmanagedCallersOnly]
    public static Vector3 IPhysicsBody_GetPosition(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        return body?.GetPosition() ?? Vector3.Zero;
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_SetPosition(void* bodyPtr, Vector3 pos)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        body?.SetPosition(pos);
    }

    [UnmanagedCallersOnly]
    public static System.Numerics.Quaternion IPhysicsBody_GetOrientation(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        return body != null ? body.GetOrientation() : new System.Numerics.Quaternion(0,0,0,1);
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_SetOrientation(void* bodyPtr, System.Numerics.Quaternion orientation)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        body?.SetOrientation(orientation);
    }

    [UnmanagedCallersOnly]
    public static Vector3 IPhysicsBody_GetLinearVelocity(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        return body?.GetLinearVelocity() ?? Vector3.Zero;
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_SetLinearVelocity(void* bodyPtr, Vector3 velocity)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        body?.SetLinearVelocity(velocity);
    }

    [UnmanagedCallersOnly]
    public static Vector3 IPhysicsBody_GetAngularVelocity(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        return body?.GetAngularVelocity() ?? Vector3.Zero;
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_SetAngularVelocity(void* bodyPtr, Vector3 velocity)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        body?.SetAngularVelocity(velocity);
    }

    [UnmanagedCallersOnly]
    public static float IPhysicsBody_GetMass(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        return body?.GetMass() ?? 0f;
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_SetMass(void* bodyPtr, float mass)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        body?.SetMass(mass);
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_Enable(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        body?.Enable();
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_Disable(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = HandleManager.Get<BepuPhysicsBody>(handle);
        body?.Disable();
    }

    [UnmanagedCallersOnly]
    public static int Physics_AddBody(void* worldPtr)
    {
        int handle = (int)(long)worldPtr;
        var world = HandleManager.Get<BepuPhysicsWorld>(handle);
        if (world == null) return 0;
        var bodyHandle = world.AddBody();
        var body = new BepuPhysicsBody(world, bodyHandle);
        int bodyId = HandleManager.Register(body);
        return bodyId;
    }

    [UnmanagedCallersOnly]
    public static void Physics_Step(void* worldPtr, float dt)
    {
        int handle = (int)(long)worldPtr;
        var world = HandleManager.Get<BepuPhysicsWorld>(handle);
        world?.Step(dt);
    }

    // Audio mix buffer API implementations
    [UnmanagedCallersOnly]
    public static IntPtr CAudioMixBuffer_Create()
    {
        var buf = new AudioMixBuffer();
        int handle = HandleManager.Register(buf);
        return (IntPtr)handle;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixBuffer_Dispose(IntPtr bufferPtr)
    {
        int handle = (int)bufferPtr;
        HandleManager.Unregister(handle);
        return null;
    }
    [UnmanagedCallersOnly]
    public static IntPtr CAudioMixBuffer_GetDataPointer(IntPtr bufferPtr)
    {
        int handle = (int)bufferPtr;
        var buffer = HandleManager.Get<AudioMixBuffer>(handle);
        if (buffer == null) return IntPtr.Zero;
        fixed (float* ptr = buffer.Data)
        {
            return (IntPtr)ptr;
        }
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixBuffer_Silence(IntPtr bufferPtr)
    {
        int handle = (int)bufferPtr;
        var buffer = HandleManager.Get<AudioMixBuffer>(handle);
        buffer?.Silence();
        return null;
    }
    [UnmanagedCallersOnly]
    public static float CAudioMixBuffer_AbsLevel(IntPtr bufferPtr)
    {
        int handle = (int)bufferPtr;
        var buffer = HandleManager.Get<AudioMixBuffer>(handle);
        return buffer?.AbsLevel() ?? 0f;
    }
    [UnmanagedCallersOnly]
    public static float CAudioMixBuffer_AverageLevel(IntPtr bufferPtr)
    {
        int handle = (int)bufferPtr;
        var buffer = HandleManager.Get<AudioMixBuffer>(handle);
        return buffer?.AverageLevel() ?? 0f;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixBuffer_Ramp(IntPtr bufferPtr, float a, float b)
    {
        int handle = (int)bufferPtr;
        var buffer = HandleManager.Get<AudioMixBuffer>(handle);
        buffer?.Ramp(a, b);
        return null;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixBuffer_CopyFrom(IntPtr bufferPtr, IntPtr otherPtr)
    {
        int h = (int)bufferPtr;
        int o = (int)otherPtr;
        var buffer = HandleManager.Get<AudioMixBuffer>(h);
        var other = HandleManager.Get<AudioMixBuffer>(o);
        if (buffer != null && other != null) buffer.CopyFrom(other);
        return null;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixBuffer_Mix(IntPtr bufferPtr, IntPtr otherPtr, float factor)
    {
        int h = (int)bufferPtr;
        int o = (int)otherPtr;
        var buffer = HandleManager.Get<AudioMixBuffer>(h);
        var other = HandleManager.Get<AudioMixBuffer>(o);
        if (buffer != null && other != null) buffer.Mix(other, factor);
        return null;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixBuffer_MixRamp(IntPtr bufferPtr, IntPtr otherPtr, float a, float b)
    {
        int h = (int)bufferPtr;
        int o = (int)otherPtr;
        var buffer = HandleManager.Get<AudioMixBuffer>(h);
        var other = HandleManager.Get<AudioMixBuffer>(o);
        if (buffer != null && other != null) buffer.MixRamp(other, a, b);
        return null;
    }

    // Audio mix device buffer API
    [UnmanagedCallersOnly]
    public static IntPtr CdMxDvcBffrs_Create(int count)
    {
        var dev = new AudioMixDeviceBuffers(count);
        int handle = HandleManager.Register(dev);
        return (IntPtr)handle;
    }
    [UnmanagedCallersOnly]
    public static void* CdMxDvcBffrs_Destroy(IntPtr devicePtr)
    {
        int handle = (int)devicePtr;
        HandleManager.Unregister(handle);
        return null;
    }
    [UnmanagedCallersOnly]
    public static IntPtr CdMxDvcBffrs_GetBuffer(IntPtr devicePtr, int index)
    {
        int handle = (int)devicePtr;
        var dev = HandleManager.Get<AudioMixDeviceBuffers>(handle);
        if (dev == null) return IntPtr.Zero;
        int bufHandle = dev.GetBuffer(index);
        return (IntPtr)bufHandle;
    }

    // Audio mixer API
    [UnmanagedCallersOnly]
    public static void* CAudioMixer_Dispose(IntPtr mixerPtr)
    {
        int handle = (int)mixerPtr;
        HandleManager.Unregister(handle);
        return null;
    }
    [UnmanagedCallersOnly]
    public static int CAudioMixer_GetSamplePosition(IntPtr mixerPtr)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        return mixer?.GetSamplePosition() ?? 0;
    }
    [UnmanagedCallersOnly]
    public static int CAudioMixer_ShouldContinueMixing(IntPtr mixerPtr)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        return mixer?.ShouldContinueMixing() ?? 1;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixer_SetSamplePosition(IntPtr mixerPtr, int pos)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        mixer?.SetSamplePosition(pos);
        return null;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixer_SetSampleEnd(IntPtr mixerPtr, uint end)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        mixer?.SetSampleEnd(end);
        return null;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixer_DelayOrSkipSamples(IntPtr mixerPtr, int delta)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        mixer?.DelayOrSkipSamples(delta);
        return null;
    }
    [UnmanagedCallersOnly]
    public static int CAudioMixer_IsReadyToMix(IntPtr mixerPtr)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        return mixer?.IsReadyToMix() ?? 1;
    }
    [UnmanagedCallersOnly]
    public static int CAudioMixer_GetPositionForSave(IntPtr mixerPtr)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        return mixer?.GetPositionForSave() ?? 0;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixer_SetPositionFromSaved(IntPtr mixerPtr, int pos)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        mixer?.SetPositionFromSaved(pos);
        return null;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixer_UpdateMixerState(IntPtr mixerPtr, audio_source_indexstate_t* state)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        mixer?.UpdateMixerState(state);
        return null;
    }
    [UnmanagedCallersOnly]
    public static audio_source_indexstate_t CAudioMixer_GetIndexState(IntPtr mixerPtr)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        return mixer != null ? mixer.GetIndexState() : new audio_source_indexstate_t();
    }
    [UnmanagedCallersOnly]
    public static IntPtr CAudioMixer_GetSfxTable(IntPtr mixerPtr)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        return mixer?.GetSfxTable() ?? IntPtr.Zero;
    }
    [UnmanagedCallersOnly]
    public static int CAudioMixer_GetSampleCount(IntPtr mixerPtr)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        return mixer?.GetSampleCount() ?? 0;
    }
    [UnmanagedCallersOnly]
    public static int CAudioMixer_GetChannelCount(IntPtr mixerPtr)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        return mixer?.GetChannelCount() ?? 2;
    }
    [UnmanagedCallersOnly]
    public static int CAudioMixer_SetTimeScale(IntPtr mixerPtr, float scale)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        return mixer?.SetTimeScale(scale) ?? 1;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixer_EnableLooping(IntPtr mixerPtr, int enable)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        mixer?.EnableLooping(enable);
        return null;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioMixer_ReadToBuffer(IntPtr mixerPtr, float dt, IntPtr bufferPtr)
    {
        int handle = (int)mixerPtr;
        var mixer = HandleManager.Get<AudioMixer>(handle);
        mixer?.ReadToBuffer(dt, (int)bufferPtr);
        return null;
    }

    // Audio processor API
    [UnmanagedCallersOnly]
    public static void* CAudioProcessor_Dispose(IntPtr procPtr)
    {
        int handle = (int)procPtr;
        HandleManager.Unregister(handle);
        return null;
    }
    [UnmanagedCallersOnly]
    public static void* CAudioProcessor_Process(IntPtr procPtr, IntPtr inputBuffer, IntPtr outputBuffer, int samples)
    {
        int handle = (int)procPtr;
        var proc = HandleManager.Get<AudioProcessor>(handle);
        proc?.Process((int)inputBuffer, (int)outputBuffer, samples);
        return null;
    }
    [UnmanagedCallersOnly]
    public static int CAudioProcessor_SetControlParameter(IntPtr procPtr, Sandbox.StringToken param, float value)
    {
        int handle = (int)procPtr;
        var proc = HandleManager.Get<AudioProcessor>(handle);
        return proc?.SetControlParameter(param, value) ?? 0;
    }
    [UnmanagedCallersOnly]
    public static int CAudioProcessor_SetNameParameter(IntPtr procPtr, Sandbox.StringToken param1, Sandbox.StringToken param2)
    {
        int handle = (int)procPtr;
        var proc = HandleManager.Get<AudioProcessor>(handle);
        return proc?.SetNameParameter(param1, param2) ?? 0;
    }
    [UnmanagedCallersOnly]
    public static IntPtr CAudioProcessor_CreateDelay(int dummy)
    {
        var proc = new AudioProcessor();
        int handle = HandleManager.Register(proc);
        return (IntPtr)handle;
    }
    [UnmanagedCallersOnly]
    public static IntPtr CAudioProcessor_CreatePitchShift(int dummy)
    {
        var proc = new AudioProcessor();
        int handle = HandleManager.Register(proc);
        return (IntPtr)handle;
    }

    // Audio stream managed API
    [UnmanagedCallersOnly]
    public static int CdStrmMngd_Create(int sampleRate, uint channels)
    {
        var stream = new AudioStreamManaged(sampleRate, channels);
        int handle = HandleManager.Register(stream);
        return handle;
    }
    [UnmanagedCallersOnly]
    public static void* CdStrmMngd_Destroy(IntPtr streamPtr)
    {
        int handle = (int)streamPtr;
        HandleManager.Unregister(handle);
        return null;
    }
    [UnmanagedCallersOnly]
    public static void* CdStrmMngd_WriteAudioData(IntPtr streamPtr, IntPtr dataPtr, uint sampleCount, uint channels)
    {
        int handle = (int)streamPtr;
        var stream = HandleManager.Get<AudioStreamManaged>(handle);
        if (stream != null && dataPtr != IntPtr.Zero)
        {
            int total = (int)sampleCount;
            float[] data = new float[total];
            unsafe
            {
                float* src = (float*)dataPtr;
                for (int i = 0; i < total; i++) data[i] = src[i];
            }
            stream.WriteAudioData(data);
        }
        return null;
    }
    [UnmanagedCallersOnly]
    public static uint CdStrmMngd_QueuedSampleCount(IntPtr streamPtr)
    {
        int handle = (int)streamPtr;
        var stream = HandleManager.Get<AudioStreamManaged>(handle);
        return stream?.QueuedSampleCount() ?? 0;
    }
    [UnmanagedCallersOnly]
    public static uint CdStrmMngd_MaxWriteSampleCount(IntPtr streamPtr)
    {
        int handle = (int)streamPtr;
        var stream = HandleManager.Get<AudioStreamManaged>(handle);
        return stream?.MaxWriteSampleCount() ?? 0;
    }
    [UnmanagedCallersOnly]
    public static uint CdStrmMngd_LatencySamplesCount(IntPtr streamPtr)
    {
        int handle = (int)streamPtr;
        var stream = HandleManager.Get<AudioStreamManaged>(handle);
        return stream?.LatencySamplesCount() ?? 0;
    }
    [UnmanagedCallersOnly]
    public static void* CdStrmMngd_Pause(IntPtr streamPtr)
    {
        int handle = (int)streamPtr;
        var stream = HandleManager.Get<AudioStreamManaged>(handle);
        stream?.Pause();
        return null;
    }
    [UnmanagedCallersOnly]
    public static void* CdStrmMngd_Resume(IntPtr streamPtr)
    {
        int handle = (int)streamPtr;
        var stream = HandleManager.Get<AudioStreamManaged>(handle);
        stream?.Resume();
        return null;
    }
    [UnmanagedCallersOnly]
    public static IntPtr CdStrmMngd_GetName(IntPtr streamPtr)
    {
        int handle = (int)streamPtr;
        var stream = HandleManager.Get<AudioStreamManaged>(handle);
        return stream?.GetName() ?? IntPtr.Zero;
    }
    [UnmanagedCallersOnly]
    public static IntPtr CdStrmMngd_GetSfxTable(IntPtr streamPtr)
    {
        return IntPtr.Zero;
    }

    // Binaural effect API
    [UnmanagedCallersOnly]
    public static IntPtr CBinauralEffect_Create()
    {
        var eff = new BinauralEffect();
        int handle = HandleManager.Register(eff);
        return (IntPtr)handle;
    }
    [UnmanagedCallersOnly]
    public static void* CBinauralEffect_Dispose(IntPtr effPtr)
    {
        int handle = (int)effPtr;
        HandleManager.Unregister(handle);
        return null;
    }
    [UnmanagedCallersOnly]
    public static void* CBinauralEffect_Apply(IntPtr effPtr, Vector3* direction, float intensity, IntPtr inputPtr, IntPtr outputPtr)
    {
        int handle = (int)effPtr;
        var eff = HandleManager.Get<BinauralEffect>(handle);
        eff?.Apply(direction, intensity, (int)inputPtr, (int)outputPtr);
        return null;
    }
}

// Definitions for audio source state structure used by audio mixer APIs.
public struct audio_source_indexstate_t
{
    public uint dummy;
}

// Simple audio mix buffer holding sample data.
internal class AudioMixBuffer
{
    public float[] Data;
    public AudioMixBuffer(int length = 1024)
    {
        Data = new float[length];
    }
    public void Silence()
    {
        for (int i = 0; i < Data.Length; i++) Data[i] = 0f;
    }
    public float AbsLevel()
    {
        float sum = 0f;
        for (int i = 0; i < Data.Length; i++) sum += System.MathF.Abs(Data[i]);
        return sum;
    }
    public float AverageLevel()
    {
        if (Data.Length == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < Data.Length; i++) sum += System.MathF.Abs(Data[i]);
        return sum / Data.Length;
    }
    public void Ramp(float a, float b)
    {
        int n = Data.Length;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);
            float factor = a + (b - a) * t;
            Data[i] *= factor;
        }
    }
    public void CopyFrom(AudioMixBuffer other)
    {
        int len = Math.Min(Data.Length, other.Data.Length);
        Array.Copy(other.Data, Data, len);
    }
    public void Mix(AudioMixBuffer other, float factor)
    {
        int len = Math.Min(Data.Length, other.Data.Length);
        for (int i = 0; i < len; i++) Data[i] += other.Data[i] * factor;
    }
    public void MixRamp(AudioMixBuffer other, float a, float b)
    {
        int len = Math.Min(Data.Length, other.Data.Length);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / (len - 1);
            float factor = a + (b - a) * t;
            Data[i] += other.Data[i] * factor;
        }
    }
}

// Device buffers containing multiple mix buffers.
internal class AudioMixDeviceBuffers
{
    public List<int> BufferHandles = new List<int>();
    public AudioMixDeviceBuffers(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var buf = new AudioMixBuffer();
            int h = HandleManager.Register(buf);
            BufferHandles.Add(h);
        }
    }
    public int GetBuffer(int index) => index >= 0 && index < BufferHandles.Count ? BufferHandles[index] : 0;
}

// Simple audio mixer that mixes buffers and tracks sample position.
internal class AudioMixer
{
    public int SamplePosition;
    public int SampleEnd;
    public int ChannelCount = 2;
    public int SampleCount = 0;
    public float TimeScale = 1f;
    public bool Looping;
    public int GetSamplePosition() => SamplePosition;
    public int ShouldContinueMixing() => 1;
    public void SetSamplePosition(int pos) { SamplePosition = pos; }
    public void SetSampleEnd(uint end) { SampleEnd = (int)end; }
    public void DelayOrSkipSamples(int delta) { SamplePosition += delta; }
    public int IsReadyToMix() => 1;
    public int GetPositionForSave() => SamplePosition;
    public void SetPositionFromSaved(int pos) { SamplePosition = pos; }
    public void UpdateMixerState(audio_source_indexstate_t* state) { }
    public audio_source_indexstate_t GetIndexState() => new audio_source_indexstate_t();
    public IntPtr GetSfxTable() => IntPtr.Zero;
    public int GetSampleCount() => SampleCount;
    public int GetChannelCount() => ChannelCount;
    public int SetTimeScale(float scale) { TimeScale = scale; return 1; }
    public void EnableLooping(int enable) { Looping = enable != 0; }
    public void ReadToBuffer(float dt, int bufferHandle)
    {
        var buffer = HandleManager.Get<AudioMixBuffer>(bufferHandle);
        buffer?.Silence();
    }
}

// Simple audio processor for effects.
internal class AudioProcessor
{
    public void Dispose() { }
    public void Process(int inputHandle, int outputHandle, int samples)
    {
        var input = HandleManager.Get<AudioMixBuffer>(inputHandle);
        var output = HandleManager.Get<AudioMixBuffer>(outputHandle);
        if (input == null || output == null) return;
        int len = Math.Min(input.Data.Length, output.Data.Length);
        for (int i = 0; i < len; i++) output.Data[i] = input.Data[i];
    }
    public int SetControlParameter(Sandbox.StringToken token, float value) { return 0; }
    public int SetNameParameter(Sandbox.StringToken token1, Sandbox.StringToken token2) { return 0; }
}

// Simple audio stream manager that queues audio data.
internal class AudioStreamManaged
{
    private readonly List<float> _queue = new List<float>();
    public bool Paused;
    public int SampleRate;
    public uint Channels;
    public AudioStreamManaged(int sampleRate, uint channels)
    {
        SampleRate = sampleRate;
        Channels = channels;
    }
    public void WriteAudioData(float[] data)
    {
        _queue.AddRange(data);
    }
    public uint QueuedSampleCount() => (uint)_queue.Count;
    public uint MaxWriteSampleCount() => 8192;
    public uint LatencySamplesCount() => 0;
    public void Pause() { Paused = true; }
    public void Resume() { Paused = false; }
    public IntPtr GetName()
    {
        var str = "AudioStream";
        return Marshal.StringToHGlobalAnsi(str);
    }
    public IntPtr GetSfxTable() => IntPtr.Zero;
}

// Binaural effect placeholder.
internal class BinauralEffect
{
    public void Dispose() { }
    public void Apply(Vector3* direction, float intensity, int inputHandle, int outputHandle)
    {
        var input = HandleManager.Get<AudioMixBuffer>(inputHandle);
        var output = HandleManager.Get<AudioMixBuffer>(outputHandle);
        if (input == null || output == null) return;
        int len = Math.Min(input.Data.Length, output.Data.Length);
        for (int i = 0; i < len; i++) output.Data[i] = input.Data[i];
    }
}

// Handle manager for registering and retrieving objects.
internal static class HandleManager
{
    private static readonly Dictionary<int, object> _objects = new Dictionary<int, object>();
    private static int _next = 1;
    public static int Register(object obj)
    {
        int handle = _next++;
        _objects[handle] = obj;
        return handle;
    }
    public static void Unregister(int handle)
    {
        _objects.Remove(handle);
    }
    public static T? Get<T>(int handle) where T : class
    {
        if (_objects.TryGetValue(handle, out var obj)) return obj as T;
        return null;
    }
}

// Minimal physics world representation.
internal class BepuPhysicsWorld
{
    public Vector3 Gravity = new Vector3(0, 0, -800);
    private readonly List<BepuPhysicsBody> _bodies = new List<BepuPhysicsBody>();
    public int AddBody()
    {
        var body = new BepuPhysicsBody(this, 0);
        _bodies.Add(body);
        return _bodies.Count - 1;
    }
    public void Step(float dt)
    {
        foreach (var body in _bodies)
        {
            if (!body.Enabled) continue;
            body.LinearVelocity += Gravity * dt;
            body.Position += body.LinearVelocity * dt;
        }
    }
    public void Dispose() { _bodies.Clear(); }
}

// Minimal physics body representation.
internal class BepuPhysicsBody
{
    public BepuPhysicsWorld World;
    public Vector3 Position;
    public System.Numerics.Quaternion Orientation = new System.Numerics.Quaternion(0, 0, 0, 1);
    public Vector3 LinearVelocity;
    public Vector3 AngularVelocity;
    public float Mass = 1f;
    public bool Enabled = true;
    public BepuPhysicsBody(BepuPhysicsWorld world, int index)
    {
        World = world;
    }
    public Vector3 GetPosition() => Position;
    public void SetPosition(Vector3 pos) { Position = pos; }
    public System.Numerics.Quaternion GetOrientation() => Orientation;
    public void SetOrientation(System.Numerics.Quaternion rot) { Orientation = rot; }
    public Vector3 GetLinearVelocity() => LinearVelocity;
    public void SetLinearVelocity(Vector3 vel) { LinearVelocity = vel; }
    public Vector3 GetAngularVelocity() => AngularVelocity;
    public void SetAngularVelocity(Vector3 vel) { AngularVelocity = vel; }
    public float GetMass() => Mass;
    public void SetMass(float m) { Mass = m; }
    public void Enable() { Enabled = true; }
    public void Disable() { Enabled = false; }
}
