using System;
using System.Runtime.InteropServices;

namespace Sandbox.Engine.Emulation;

public static unsafe class EngineGlue
{
    public static void StoreImports(void** managedFunctions)
    {
        Imports.StoreImport_Sandbox_AsyncGPUReadback_DispatchManagedReadTextureCallback(managedFunctions[0]);
        Imports.StoreImport_Sandbox_AsyncGPUReadback_DispatchManagedReadBufferCallback(managedFunctions[1]);
        Imports.StoreImport_SandboxAudio_MixingThread_MixOneBuffer(managedFunctions[2]);
        Imports.StoreImport_Sandbox_ConVarSystem_RegisterNativeVar(managedFunctions[3]);
        Imports.StoreImport_Sandbox_ConVarSystem_RegisterNativeCommand(managedFunctions[4]);
        Imports.StoreImport_Sandbox_ConVarSystem_OnConVarChanged(managedFunctions[5]);
        Imports.StoreImport_SndbxDgnstcs_Logging_RegisterEngineLogger(managedFunctions[6]);
        Imports.StoreImport_SandboxEngine_Bootstrap_EnvironmentExit(managedFunctions[7]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnMouseMotion(managedFunctions[8]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnMousePositionChange(managedFunctions[9]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnMouseButton(managedFunctions[10]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnKey(managedFunctions[11]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnText(managedFunctions[12]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnWindowActive(managedFunctions[13]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnMouseWheel(managedFunctions[14]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnImeStart(managedFunctions[15]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnImeComposition(managedFunctions[16]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnImeEnd(managedFunctions[17]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnGameControllerButton(managedFunctions[18]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnGameControllerAxis(managedFunctions[19]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnGameControllerConnected(managedFunctions[20]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnGameControllerDisconnected(managedFunctions[21]);
        Imports.StoreImport_SandboxEngine_InputRouter_OnConsoleCommand(managedFunctions[22]);
        Imports.StoreImport_SandboxEngine_InputRouter_CloseApplication(managedFunctions[23]);
        Imports.StoreImport_SandboxEngine_SystemInfo_Set(managedFunctions[24]);
        Imports.StoreImport_SandboxEngine_SystemInfo_SetGpu(managedFunctions[25]);
        Imports.StoreImport_Sandbox_EngineLoop_Print(managedFunctions[26]);
        Imports.StoreImport_Sandbox_EngineLoop_DispatchConsoleCommand(managedFunctions[27]);
        Imports.StoreImport_Sandbox_EngineLoop_OnClientOutput(managedFunctions[28]);
        Imports.StoreImport_Sandbox_EngineLoop_OnSceneViewSubmitted(managedFunctions[29]);
        Imports.StoreImport_Sandbox_Graphics_OnLayer(managedFunctions[30]);
        Imports.StoreImport_Sandbox_HandleIndex_RegisterHandle(managedFunctions[31]);
        Imports.StoreImport_Sandbox_HandleIndex_FreeHandle(managedFunctions[32]);
        Imports.StoreImport_Sandbox_LobbyManager_OnLobbyInvite(managedFunctions[33]);
        Imports.StoreImport_Sandbox_LobbyManager_OnMemberEntered(managedFunctions[34]);
        Imports.StoreImport_Sandbox_LobbyManager_OnMemberLeave(managedFunctions[35]);
        Imports.StoreImport_Sandbox_LobbyManager_OnEntered(managedFunctions[36]);
        Imports.StoreImport_Sandbox_LobbyManager_OnCreated(managedFunctions[37]);
        Imports.StoreImport_Sandbox_LobbyManager_OnDataUpdate(managedFunctions[38]);
        Imports.StoreImport_Sandbox_LobbyManager_OnChatMessage(managedFunctions[39]);
        Imports.StoreImport_SandboxNetwork_SteamNetwork_OnSocketConnection(managedFunctions[40]);
        Imports.StoreImport_SandboxNetwork_SteamNetwork_OnSocketDisconnection(managedFunctions[41]);
        Imports.StoreImport_SandboxNetwork_SteamNetwork_ShouldAcceptConnection(managedFunctions[42]);
        Imports.StoreImport_SandboxNetwork_SteamNetwork_OnDisconnection(managedFunctions[43]);
        Imports.StoreImport_SandboxNetwork_SteamNetwork_OnSessionEstablished(managedFunctions[44]);
        Imports.StoreImport_SandboxNetwork_SteamNetwork_OnSessionFailed(managedFunctions[45]);
        Imports.StoreImport_SandboxPhysics_PhysicsEngine_OnPhysicsJointBreak(managedFunctions[46]);
        Imports.StoreImport_SandboxPhysics_PhysicsEngine_OnActive(managedFunctions[47]);
        Imports.StoreImport_Sandbox_RealTime_Update(managedFunctions[48]);
        Imports.StoreImport_SndbxRndrng_RenderPipeline_InternalAddLayersToView(managedFunctions[49]);
        Imports.StoreImport_SndbxRndrng_RenderPipeline_InternalPipelineEnd(managedFunctions[50]);
        Imports.StoreImport_Sandbox_RenderTarget_Flush(managedFunctions[51]);
        Imports.StoreImport_Sandbox_Resource_OnResourceReloaded(managedFunctions[52]);
        Imports.StoreImport_Sandbox_ScnCstmbjctRndr_RenderObject(managedFunctions[53]);
        Imports.StoreImport_Sandbox_SceneSystem_OnBeforeRender(managedFunctions[54]);
        Imports.StoreImport_Sandbox_SceneSystem_OnAfterRender(managedFunctions[55]);
        Imports.StoreImport_SndbxSrvcs_Inventory_OnDefinitionUpdate(managedFunctions[56]);
        Imports.StoreImport_SndbxSrvcs_Inventory_OnPricesUpdate(managedFunctions[57]);
        Imports.StoreImport_SndbxSrvcs_Inventory_OnPurchaseResult(managedFunctions[58]);
        Imports.StoreImport_SndbxSrvcs_ServerList_OnStarted(managedFunctions[59]);
        Imports.StoreImport_SndbxSrvcs_ServerList_OnServerResponded(managedFunctions[60]);
        Imports.StoreImport_SndbxSrvcs_ServerList_OnFinished(managedFunctions[61]);
        Imports.StoreImport_Sandbox_VideoPlayer_OnTextureCreatedInternal(managedFunctions[62]);
        Imports.StoreImport_Sandbox_VideoPlayer_OnInitAudioInternal(managedFunctions[63]);
        Imports.StoreImport_Sandbox_VideoPlayer_OnFreeAudioInternal(managedFunctions[64]);
        Imports.StoreImport_Sandbox_VideoPlayer_OnTextureDataInternal(managedFunctions[65]);
        Imports.StoreImport_Sandbox_VideoPlayer_OnLoadedInternal(managedFunctions[66]);
        Imports.StoreImport_Sandbox_VideoPlayer_WantsTextureData(managedFunctions[67]);
        Imports.StoreImport_Sandbox_VideoPlayer_OnFinishedInternal(managedFunctions[68]);
        Imports.StoreImport_Sandbox_VideoPlayer_OnRepeatedInternal(managedFunctions[69]);
        Imports.StoreImport_SandboxVR_VRSystem_InternalIsActive(managedFunctions[70]);
        Imports.StoreImport_SandboxVR_VRSystem_InternalWantsInit(managedFunctions[71]);
        Imports.StoreImport_SandboxVR_VRSystem_BeginFrame(managedFunctions[72]);
        Imports.StoreImport_SandboxVR_VRSystem_EndFrame(managedFunctions[73]);
        Imports.StoreImport_SandboxVR_VRSystem_Submit(managedFunctions[74]);
        Imports.StoreImport_SandboxVR_VRSystem_GetVulkanInstanceExtensionsRequired(managedFunctions[75]);
        Imports.StoreImport_SandboxVR_VRSystem_GetVulkanDeviceExtensionsRequired(managedFunctions[76]);
        Imports.StoreImport_SandboxVR_VRSystem_RenderOverlays(managedFunctions[77]);
        Imports.StoreImport_Steamworks_Dispatch_OnClientCallback(managedFunctions[78]);
    }


    public static unsafe partial class Imports
    {
        public static delegate* unmanaged<void*, void*, long, void*, void*, void*, void*, void*> _ptr_Sandbox_AsyncGPUReadback_DispatchManagedReadTextureCallback;
        public static delegate* unmanaged<void*, void*, void*, void*> _ptr_Sandbox_AsyncGPUReadback_DispatchManagedReadBufferCallback;
        public static delegate* unmanaged<void*> _ptr_SandboxAudio_MixingThread_MixOneBuffer;
        public static delegate* unmanaged<void*, void*> _ptr_Sandbox_ConVarSystem_RegisterNativeVar;
        public static delegate* unmanaged<void*, void*> _ptr_Sandbox_ConVarSystem_RegisterNativeCommand;
        public static delegate* unmanaged<void*, void*, void*, void*> _ptr_Sandbox_ConVarSystem_OnConVarChanged;
        public static delegate* unmanaged<void*, void*, void*> _ptr_SndbxDgnstcs_Logging_RegisterEngineLogger;
        public static delegate* unmanaged<void*, void*> _ptr_SandboxEngine_Bootstrap_EnvironmentExit;
        public static delegate* unmanaged<float, float, void*> _ptr_SandboxEngine_InputRouter_OnMouseMotion;
        public static delegate* unmanaged<float, float, float, float, void*> _ptr_SandboxEngine_InputRouter_OnMousePositionChange;
        public static delegate* unmanaged<long, void*, void*, void*> _ptr_SandboxEngine_InputRouter_OnMouseButton;
        public static delegate* unmanaged<long, void*, void*, void*, void*, void*> _ptr_SandboxEngine_InputRouter_OnKey;
        public static delegate* unmanaged<void*, void*> _ptr_SandboxEngine_InputRouter_OnText;
        public static delegate* unmanaged<void*, void*> _ptr_SandboxEngine_InputRouter_OnWindowActive;
        public static delegate* unmanaged<void*, void*, void*, void*> _ptr_SandboxEngine_InputRouter_OnMouseWheel;
        public static delegate* unmanaged<void*> _ptr_SandboxEngine_InputRouter_OnImeStart;
        public static delegate* unmanaged<void*, void*, void*> _ptr_SandboxEngine_InputRouter_OnImeComposition;
        public static delegate* unmanaged<void*> _ptr_SandboxEngine_InputRouter_OnImeEnd;
        public static delegate* unmanaged<void*, long, void*, void*> _ptr_SandboxEngine_InputRouter_OnGameControllerButton;
        public static delegate* unmanaged<void*, long, void*, void*> _ptr_SandboxEngine_InputRouter_OnGameControllerAxis;
        public static delegate* unmanaged<void*, void*, void*> _ptr_SandboxEngine_InputRouter_OnGameControllerConnected;
        public static delegate* unmanaged<void*, void*> _ptr_SandboxEngine_InputRouter_OnGameControllerDisconnected;
        public static delegate* unmanaged<void*, void*> _ptr_SandboxEngine_InputRouter_OnConsoleCommand;
        public static delegate* unmanaged<void*> _ptr_SandboxEngine_InputRouter_CloseApplication;
        public static delegate* unmanaged<void*, void*, ulong, ulong, void*> _ptr_SandboxEngine_SystemInfo_Set;
        public static delegate* unmanaged<void*, void*, ulong, void*> _ptr_SandboxEngine_SystemInfo_SetGpu;
        public static delegate* unmanaged<void*, void*, void*, void*> _ptr_Sandbox_EngineLoop_Print;
        public static delegate* unmanaged<void*, void*, long, void*> _ptr_Sandbox_EngineLoop_DispatchConsoleCommand;
        public static delegate* unmanaged<void*> _ptr_Sandbox_EngineLoop_OnClientOutput;
        public static delegate* unmanaged<void*, void*> _ptr_Sandbox_EngineLoop_OnSceneViewSubmitted;
        public static delegate* unmanaged<void*, void*, void*> _ptr_Sandbox_Graphics_OnLayer;
        public static delegate* unmanaged<void*, void*, void*> _ptr_Sandbox_HandleIndex_RegisterHandle;
        public static delegate* unmanaged<void*, void*> _ptr_Sandbox_HandleIndex_FreeHandle;
        public static delegate* unmanaged<ulong, ulong, void*> _ptr_Sandbox_LobbyManager_OnLobbyInvite;
        public static delegate* unmanaged<ulong, ulong, void*> _ptr_Sandbox_LobbyManager_OnMemberEntered;
        public static delegate* unmanaged<ulong, ulong, void*> _ptr_Sandbox_LobbyManager_OnMemberLeave;
        public static delegate* unmanaged<ulong, void*> _ptr_Sandbox_LobbyManager_OnEntered;
        public static delegate* unmanaged<ulong, void*> _ptr_Sandbox_LobbyManager_OnCreated;
        public static delegate* unmanaged<ulong, ulong, void*> _ptr_Sandbox_LobbyManager_OnDataUpdate;
        public static delegate* unmanaged<ulong, ulong, void*, void*, void*> _ptr_Sandbox_LobbyManager_OnChatMessage;
        public static delegate* unmanaged<void*, void*, void*> _ptr_SandboxNetwork_SteamNetwork_OnSocketConnection;
        public static delegate* unmanaged<void*, void*, void*> _ptr_SandboxNetwork_SteamNetwork_OnSocketDisconnection;
        public static delegate* unmanaged<void*, void*, void*> _ptr_SandboxNetwork_SteamNetwork_ShouldAcceptConnection;
        public static delegate* unmanaged<void*, void*, void*, void*> _ptr_SandboxNetwork_SteamNetwork_OnDisconnection;
        public static delegate* unmanaged<ulong, void*> _ptr_SandboxNetwork_SteamNetwork_OnSessionEstablished;
        public static delegate* unmanaged<void*, ulong, void*> _ptr_SandboxNetwork_SteamNetwork_OnSessionFailed;
        public static delegate* unmanaged<void*, void*> _ptr_SandboxPhysics_PhysicsEngine_OnPhysicsJointBreak;
        public static delegate* unmanaged<void*, void*, void*, void*, void*> _ptr_SandboxPhysics_PhysicsEngine_OnActive;
        public static delegate* unmanaged<float, void*> _ptr_Sandbox_RealTime_Update;
        public static delegate* unmanaged<void*, void*, void*, void*, long, void*, void*, void*> _ptr_SndbxRndrng_RenderPipeline_InternalAddLayersToView;
        public static delegate* unmanaged<void*, void*, void*, void*, long, void*, void*, void*> _ptr_SndbxRndrng_RenderPipeline_InternalPipelineEnd;
        public static delegate* unmanaged<void*> _ptr_Sandbox_RenderTarget_Flush;
        public static delegate* unmanaged<void*, void*, void*> _ptr_Sandbox_Resource_OnResourceReloaded;
        public static delegate* unmanaged<void*, void*, void*> _ptr_Sandbox_ScnCstmbjctRndr_RenderObject;
        public static delegate* unmanaged<void*, void*, void*> _ptr_Sandbox_SceneSystem_OnBeforeRender;
        public static delegate* unmanaged<void*, void*, void*> _ptr_Sandbox_SceneSystem_OnAfterRender;
        public static delegate* unmanaged<void*> _ptr_SndbxSrvcs_Inventory_OnDefinitionUpdate;
        public static delegate* unmanaged<void*, void*, void*> _ptr_SndbxSrvcs_Inventory_OnPricesUpdate;
        public static delegate* unmanaged<void*, ulong, ulong, void*> _ptr_SndbxSrvcs_Inventory_OnPurchaseResult;
        public static delegate* unmanaged<void*, void*> _ptr_SndbxSrvcs_ServerList_OnStarted;
        public static delegate* unmanaged<void*, void*, ulong, void*> _ptr_SndbxSrvcs_ServerList_OnServerResponded;
        public static delegate* unmanaged<void*, void*> _ptr_SndbxSrvcs_ServerList_OnFinished;
        public static delegate* unmanaged<void*, void*> _ptr_Sandbox_VideoPlayer_OnTextureCreatedInternal;
        public static delegate* unmanaged<void*, void*, void*, void*> _ptr_Sandbox_VideoPlayer_OnInitAudioInternal;
        public static delegate* unmanaged<void*, void*> _ptr_Sandbox_VideoPlayer_OnFreeAudioInternal;
        public static delegate* unmanaged<void*, void*, void*, void*, void*> _ptr_Sandbox_VideoPlayer_OnTextureDataInternal;
        public static delegate* unmanaged<void*, void*> _ptr_Sandbox_VideoPlayer_OnLoadedInternal;
        public static delegate* unmanaged<void*, void*> _ptr_Sandbox_VideoPlayer_WantsTextureData;
        public static delegate* unmanaged<void*, void*> _ptr_Sandbox_VideoPlayer_OnFinishedInternal;
        public static delegate* unmanaged<void*, void*> _ptr_Sandbox_VideoPlayer_OnRepeatedInternal;
        public static delegate* unmanaged<void*> _ptr_SandboxVR_VRSystem_InternalIsActive;
        public static delegate* unmanaged<void*> _ptr_SandboxVR_VRSystem_InternalWantsInit;
        public static delegate* unmanaged<void*> _ptr_SandboxVR_VRSystem_BeginFrame;
        public static delegate* unmanaged<void*> _ptr_SandboxVR_VRSystem_EndFrame;
        public static delegate* unmanaged<void*, void*, void*> _ptr_SandboxVR_VRSystem_Submit;
        public static delegate* unmanaged<void*> _ptr_SandboxVR_VRSystem_GetVulkanInstanceExtensionsRequired;
        public static delegate* unmanaged<void*> _ptr_SandboxVR_VRSystem_GetVulkanDeviceExtensionsRequired;
        public static delegate* unmanaged<void*> _ptr_SandboxVR_VRSystem_RenderOverlays;
        public static delegate* unmanaged<void*, void*, void*, void*, void*> _ptr_Steamworks_Dispatch_OnClientCallback;
        public static void StoreImport_Sandbox_AsyncGPUReadback_DispatchManagedReadTextureCallback(void* ptr)
        {
            _ptr_Sandbox_AsyncGPUReadback_DispatchManagedReadTextureCallback = (delegate* unmanaged<void*, void*, long, void*, void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_AsyncGPUReadback_DispatchManagedReadBufferCallback(void* ptr)
        {
            _ptr_Sandbox_AsyncGPUReadback_DispatchManagedReadBufferCallback = (delegate* unmanaged<void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxAudio_MixingThread_MixOneBuffer(void* ptr)
        {
            _ptr_SandboxAudio_MixingThread_MixOneBuffer = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_Sandbox_ConVarSystem_RegisterNativeVar(void* ptr)
        {
            _ptr_Sandbox_ConVarSystem_RegisterNativeVar = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_ConVarSystem_RegisterNativeCommand(void* ptr)
        {
            _ptr_Sandbox_ConVarSystem_RegisterNativeCommand = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_ConVarSystem_OnConVarChanged(void* ptr)
        {
            _ptr_Sandbox_ConVarSystem_OnConVarChanged = (delegate* unmanaged<void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_SndbxDgnstcs_Logging_RegisterEngineLogger(void* ptr)
        {
            _ptr_SndbxDgnstcs_Logging_RegisterEngineLogger = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_Bootstrap_EnvironmentExit(void* ptr)
        {
            _ptr_SandboxEngine_Bootstrap_EnvironmentExit = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnMouseMotion(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnMouseMotion = (delegate* unmanaged<float, float, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnMousePositionChange(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnMousePositionChange = (delegate* unmanaged<float, float, float, float, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnMouseButton(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnMouseButton = (delegate* unmanaged<long, void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnKey(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnKey = (delegate* unmanaged<long, void*, void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnText(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnText = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnWindowActive(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnWindowActive = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnMouseWheel(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnMouseWheel = (delegate* unmanaged<void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnImeStart(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnImeStart = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnImeComposition(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnImeComposition = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnImeEnd(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnImeEnd = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnGameControllerButton(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnGameControllerButton = (delegate* unmanaged<void*, long, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnGameControllerAxis(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnGameControllerAxis = (delegate* unmanaged<void*, long, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnGameControllerConnected(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnGameControllerConnected = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnGameControllerDisconnected(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnGameControllerDisconnected = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_OnConsoleCommand(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_OnConsoleCommand = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_InputRouter_CloseApplication(void* ptr)
        {
            _ptr_SandboxEngine_InputRouter_CloseApplication = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_SystemInfo_Set(void* ptr)
        {
            _ptr_SandboxEngine_SystemInfo_Set = (delegate* unmanaged<void*, void*, ulong, ulong, void*>)ptr;
        }
        public static void StoreImport_SandboxEngine_SystemInfo_SetGpu(void* ptr)
        {
            _ptr_SandboxEngine_SystemInfo_SetGpu = (delegate* unmanaged<void*, void*, ulong, void*>)ptr;
        }
        public static void StoreImport_Sandbox_EngineLoop_Print(void* ptr)
        {
            _ptr_Sandbox_EngineLoop_Print = (delegate* unmanaged<void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_EngineLoop_DispatchConsoleCommand(void* ptr)
        {
            _ptr_Sandbox_EngineLoop_DispatchConsoleCommand = (delegate* unmanaged<void*, void*, long, void*>)ptr;
        }
        public static void StoreImport_Sandbox_EngineLoop_OnClientOutput(void* ptr)
        {
            _ptr_Sandbox_EngineLoop_OnClientOutput = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_Sandbox_EngineLoop_OnSceneViewSubmitted(void* ptr)
        {
            _ptr_Sandbox_EngineLoop_OnSceneViewSubmitted = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_Graphics_OnLayer(void* ptr)
        {
            _ptr_Sandbox_Graphics_OnLayer = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_HandleIndex_RegisterHandle(void* ptr)
        {
            _ptr_Sandbox_HandleIndex_RegisterHandle = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_HandleIndex_FreeHandle(void* ptr)
        {
            _ptr_Sandbox_HandleIndex_FreeHandle = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_LobbyManager_OnLobbyInvite(void* ptr)
        {
            _ptr_Sandbox_LobbyManager_OnLobbyInvite = (delegate* unmanaged<ulong, ulong, void*>)ptr;
        }
        public static void StoreImport_Sandbox_LobbyManager_OnMemberEntered(void* ptr)
        {
            _ptr_Sandbox_LobbyManager_OnMemberEntered = (delegate* unmanaged<ulong, ulong, void*>)ptr;
        }
        public static void StoreImport_Sandbox_LobbyManager_OnMemberLeave(void* ptr)
        {
            _ptr_Sandbox_LobbyManager_OnMemberLeave = (delegate* unmanaged<ulong, ulong, void*>)ptr;
        }
        public static void StoreImport_Sandbox_LobbyManager_OnEntered(void* ptr)
        {
            _ptr_Sandbox_LobbyManager_OnEntered = (delegate* unmanaged<ulong, void*>)ptr;
        }
        public static void StoreImport_Sandbox_LobbyManager_OnCreated(void* ptr)
        {
            _ptr_Sandbox_LobbyManager_OnCreated = (delegate* unmanaged<ulong, void*>)ptr;
        }
        public static void StoreImport_Sandbox_LobbyManager_OnDataUpdate(void* ptr)
        {
            _ptr_Sandbox_LobbyManager_OnDataUpdate = (delegate* unmanaged<ulong, ulong, void*>)ptr;
        }
        public static void StoreImport_Sandbox_LobbyManager_OnChatMessage(void* ptr)
        {
            _ptr_Sandbox_LobbyManager_OnChatMessage = (delegate* unmanaged<ulong, ulong, void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxNetwork_SteamNetwork_OnSocketConnection(void* ptr)
        {
            _ptr_SandboxNetwork_SteamNetwork_OnSocketConnection = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxNetwork_SteamNetwork_OnSocketDisconnection(void* ptr)
        {
            _ptr_SandboxNetwork_SteamNetwork_OnSocketDisconnection = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxNetwork_SteamNetwork_ShouldAcceptConnection(void* ptr)
        {
            _ptr_SandboxNetwork_SteamNetwork_ShouldAcceptConnection = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxNetwork_SteamNetwork_OnDisconnection(void* ptr)
        {
            _ptr_SandboxNetwork_SteamNetwork_OnDisconnection = (delegate* unmanaged<void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxNetwork_SteamNetwork_OnSessionEstablished(void* ptr)
        {
            _ptr_SandboxNetwork_SteamNetwork_OnSessionEstablished = (delegate* unmanaged<ulong, void*>)ptr;
        }
        public static void StoreImport_SandboxNetwork_SteamNetwork_OnSessionFailed(void* ptr)
        {
            _ptr_SandboxNetwork_SteamNetwork_OnSessionFailed = (delegate* unmanaged<void*, ulong, void*>)ptr;
        }
        public static void StoreImport_SandboxPhysics_PhysicsEngine_OnPhysicsJointBreak(void* ptr)
        {
            _ptr_SandboxPhysics_PhysicsEngine_OnPhysicsJointBreak = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_SandboxPhysics_PhysicsEngine_OnActive(void* ptr)
        {
            _ptr_SandboxPhysics_PhysicsEngine_OnActive = (delegate* unmanaged<void*, void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_RealTime_Update(void* ptr)
        {
            _ptr_Sandbox_RealTime_Update = (delegate* unmanaged<float, void*>)ptr;
        }
        public static void StoreImport_SndbxRndrng_RenderPipeline_InternalAddLayersToView(void* ptr)
        {
            _ptr_SndbxRndrng_RenderPipeline_InternalAddLayersToView = (delegate* unmanaged<void*, void*, void*, void*, long, void*, void*, void*>)ptr;
        }
        public static void StoreImport_SndbxRndrng_RenderPipeline_InternalPipelineEnd(void* ptr)
        {
            _ptr_SndbxRndrng_RenderPipeline_InternalPipelineEnd = (delegate* unmanaged<void*, void*, void*, void*, long, void*, void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_RenderTarget_Flush(void* ptr)
        {
            _ptr_Sandbox_RenderTarget_Flush = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_Sandbox_Resource_OnResourceReloaded(void* ptr)
        {
            _ptr_Sandbox_Resource_OnResourceReloaded = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_ScnCstmbjctRndr_RenderObject(void* ptr)
        {
            _ptr_Sandbox_ScnCstmbjctRndr_RenderObject = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_SceneSystem_OnBeforeRender(void* ptr)
        {
            _ptr_Sandbox_SceneSystem_OnBeforeRender = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_SceneSystem_OnAfterRender(void* ptr)
        {
            _ptr_Sandbox_SceneSystem_OnAfterRender = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_SndbxSrvcs_Inventory_OnDefinitionUpdate(void* ptr)
        {
            _ptr_SndbxSrvcs_Inventory_OnDefinitionUpdate = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_SndbxSrvcs_Inventory_OnPricesUpdate(void* ptr)
        {
            _ptr_SndbxSrvcs_Inventory_OnPricesUpdate = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_SndbxSrvcs_Inventory_OnPurchaseResult(void* ptr)
        {
            _ptr_SndbxSrvcs_Inventory_OnPurchaseResult = (delegate* unmanaged<void*, ulong, ulong, void*>)ptr;
        }
        public static void StoreImport_SndbxSrvcs_ServerList_OnStarted(void* ptr)
        {
            _ptr_SndbxSrvcs_ServerList_OnStarted = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_SndbxSrvcs_ServerList_OnServerResponded(void* ptr)
        {
            _ptr_SndbxSrvcs_ServerList_OnServerResponded = (delegate* unmanaged<void*, void*, ulong, void*>)ptr;
        }
        public static void StoreImport_SndbxSrvcs_ServerList_OnFinished(void* ptr)
        {
            _ptr_SndbxSrvcs_ServerList_OnFinished = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_VideoPlayer_OnTextureCreatedInternal(void* ptr)
        {
            _ptr_Sandbox_VideoPlayer_OnTextureCreatedInternal = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_VideoPlayer_OnInitAudioInternal(void* ptr)
        {
            _ptr_Sandbox_VideoPlayer_OnInitAudioInternal = (delegate* unmanaged<void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_VideoPlayer_OnFreeAudioInternal(void* ptr)
        {
            _ptr_Sandbox_VideoPlayer_OnFreeAudioInternal = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_VideoPlayer_OnTextureDataInternal(void* ptr)
        {
            _ptr_Sandbox_VideoPlayer_OnTextureDataInternal = (delegate* unmanaged<void*, void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_VideoPlayer_OnLoadedInternal(void* ptr)
        {
            _ptr_Sandbox_VideoPlayer_OnLoadedInternal = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_VideoPlayer_WantsTextureData(void* ptr)
        {
            _ptr_Sandbox_VideoPlayer_WantsTextureData = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_VideoPlayer_OnFinishedInternal(void* ptr)
        {
            _ptr_Sandbox_VideoPlayer_OnFinishedInternal = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Sandbox_VideoPlayer_OnRepeatedInternal(void* ptr)
        {
            _ptr_Sandbox_VideoPlayer_OnRepeatedInternal = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_SandboxVR_VRSystem_InternalIsActive(void* ptr)
        {
            _ptr_SandboxVR_VRSystem_InternalIsActive = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_SandboxVR_VRSystem_InternalWantsInit(void* ptr)
        {
            _ptr_SandboxVR_VRSystem_InternalWantsInit = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_SandboxVR_VRSystem_BeginFrame(void* ptr)
        {
            _ptr_SandboxVR_VRSystem_BeginFrame = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_SandboxVR_VRSystem_EndFrame(void* ptr)
        {
            _ptr_SandboxVR_VRSystem_EndFrame = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_SandboxVR_VRSystem_Submit(void* ptr)
        {
            _ptr_SandboxVR_VRSystem_Submit = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_SandboxVR_VRSystem_GetVulkanInstanceExtensionsRequired(void* ptr)
        {
            _ptr_SandboxVR_VRSystem_GetVulkanInstanceExtensionsRequired = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_SandboxVR_VRSystem_GetVulkanDeviceExtensionsRequired(void* ptr)
        {
            _ptr_SandboxVR_VRSystem_GetVulkanDeviceExtensionsRequired = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_SandboxVR_VRSystem_RenderOverlays(void* ptr)
        {
            _ptr_SandboxVR_VRSystem_RenderOverlays = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_Steamworks_Dispatch_OnClientCallback(void* ptr)
        {
            _ptr_Steamworks_Dispatch_OnClientCallback = (delegate* unmanaged<void*, void*, void*, void*, void*>)ptr;
        }

        // Sandbox.AsyncGPUReadback
        public static void* Sandbox_AsyncGPUReadback_DispatchManagedReadTextureCallback( void* caller, void* pData, long format, void* nMipLevel, void* nWidth, void* nHeight, void* nPitchInBytes )
        {
            return (void*)Imports._ptr_Sandbox_AsyncGPUReadback_DispatchManagedReadTextureCallback( caller, pData, format, nMipLevel, nWidth, nHeight, nPitchInBytes );
        }

        public static void* Sandbox_AsyncGPUReadback_DispatchManagedReadBufferCallback( void* caller, void* pData, void* nBytes )
        {
            return (void*)Imports._ptr_Sandbox_AsyncGPUReadback_DispatchManagedReadBufferCallback( caller, pData, nBytes );
        }

        // Sandbox.Audio.MixingThread
        public static void* SandboxAudio_MixingThread_MixOneBuffer()
        {
            return (void*)Imports._ptr_SandboxAudio_MixingThread_MixOneBuffer();
        }

        // Sandbox.ConVarSystem
        public static void* Sandbox_ConVarSystem_RegisterNativeVar( void* convar )
        {
            return (void*)Imports._ptr_Sandbox_ConVarSystem_RegisterNativeVar( convar );
        }

        public static void* Sandbox_ConVarSystem_RegisterNativeCommand( void* concommand )
        {
            return (void*)Imports._ptr_Sandbox_ConVarSystem_RegisterNativeCommand( concommand );
        }

        public static void* Sandbox_ConVarSystem_OnConVarChanged( void* convarname, void* value, void* previous )
        {
            return (void*)Imports._ptr_Sandbox_ConVarSystem_OnConVarChanged( convarname, value, previous );
        }

        // Sandbox.Diagnostics.Logging
        public static void* SndbxDgnstcs_Logging_RegisterEngineLogger( void* id, void* name )
        {
            return (void*)Imports._ptr_SndbxDgnstcs_Logging_RegisterEngineLogger( id, name );
        }

        // Sandbox.Engine.Bootstrap
        public static void* SandboxEngine_Bootstrap_EnvironmentExit( void* nCode )
        {
            return (void*)Imports._ptr_SandboxEngine_Bootstrap_EnvironmentExit( nCode );
        }

        // Sandbox.DecalSceneObject
        // Sandbox.Engine.InputRouter
        public static void* SandboxEngine_InputRouter_OnMouseMotion( float dx, float dy )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnMouseMotion( dx, dy );
        }

        public static void* SandboxEngine_InputRouter_OnMousePositionChange( float x, float y, float dx, float dy )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnMousePositionChange( x, y, dx, dy );
        }

        public static void* SandboxEngine_InputRouter_OnMouseButton( long button, void* state, void* ikeymods )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnMouseButton( button, state, ikeymods );
        }

        public static void* SandboxEngine_InputRouter_OnKey( long button, void* state, void* repeating, void* ikeymods, void* vkcode )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnKey( button, state, repeating, ikeymods, vkcode );
        }

        public static void* SandboxEngine_InputRouter_OnText( void* key )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnText( key );
        }

        public static void* SandboxEngine_InputRouter_OnWindowActive( void* active )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnWindowActive( active );
        }

        public static void* SandboxEngine_InputRouter_OnMouseWheel( void* x, void* y, void* ikeymods )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnMouseWheel( x, y, ikeymods );
        }

        public static void* SandboxEngine_InputRouter_OnImeStart()
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnImeStart();
        }

        public static void* SandboxEngine_InputRouter_OnImeComposition( void* text, void* final )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnImeComposition( text, final );
        }

        public static void* SandboxEngine_InputRouter_OnImeEnd()
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnImeEnd();
        }

        public static void* SandboxEngine_InputRouter_OnGameControllerButton( void* deviceId, long button, void* state )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnGameControllerButton( deviceId, button, state );
        }

        public static void* SandboxEngine_InputRouter_OnGameControllerAxis( void* deviceId, long axis, void* value )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnGameControllerAxis( deviceId, axis, value );
        }

        public static void* SandboxEngine_InputRouter_OnGameControllerConnected( void* joystickId, void* deviceId )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnGameControllerConnected( joystickId, deviceId );
        }

        public static void* SandboxEngine_InputRouter_OnGameControllerDisconnected( void* deviceId )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnGameControllerDisconnected( deviceId );
        }

        public static void* SandboxEngine_InputRouter_OnConsoleCommand( void* command )
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_OnConsoleCommand( command );
        }

        public static void* SandboxEngine_InputRouter_CloseApplication()
        {
            return (void*)Imports._ptr_SandboxEngine_InputRouter_CloseApplication();
        }

        // Sandbox.PhysicsBody
        // Sandbox.PhysicsGroup
        // Sandbox.PhysicsShape
        // Sandbox.PhysicsWorld
        // Sandbox.SceneCubemap
        // Sandbox.SceneCustomObject
        // Sandbox.SceneLight
        // Sandbox.SceneLightProbe
        // Sandbox.SceneModel
        // Sandbox.SceneObject
        // Sandbox.SceneSkyBox
        // Sandbox.SceneWorld
        // Sandbox.SoundStream
        // Sandbox.Engine.SystemInfo
        public static void* SandboxEngine_SystemInfo_Set( void* cpu, void* processorCount, ulong frequency, ulong totalMemory )
        {
            return (void*)Imports._ptr_SandboxEngine_SystemInfo_Set( cpu, processorCount, frequency, totalMemory );
        }

        public static void* SandboxEngine_SystemInfo_SetGpu( void* driver, void* version, ulong memory )
        {
            return (void*)Imports._ptr_SandboxEngine_SystemInfo_SetGpu( driver, version, memory );
        }

        // Sandbox.EngineLoop
        public static void* Sandbox_EngineLoop_Print( void* severitty, void* logger, void* message )
        {
            return (void*)Imports._ptr_Sandbox_EngineLoop_Print( severitty, logger, message );
        }

        public static void* Sandbox_EngineLoop_DispatchConsoleCommand( void* name, void* args, long flags )
        {
            return (void*)Imports._ptr_Sandbox_EngineLoop_DispatchConsoleCommand( name, args, flags );
        }

        public static void* Sandbox_EngineLoop_OnClientOutput()
        {
            return (void*)Imports._ptr_Sandbox_EngineLoop_OnClientOutput();
        }

        public static void* Sandbox_EngineLoop_OnSceneViewSubmitted( void* view )
        {
            return (void*)Imports._ptr_Sandbox_EngineLoop_OnSceneViewSubmitted( view );
        }

        // Sandbox.Graphics
        public static void* Sandbox_Graphics_OnLayer( void* renderHookStage, void* setup )
        {
            return (void*)Imports._ptr_Sandbox_Graphics_OnLayer( renderHookStage, setup );
        }

        // Sandbox.HandleIndex
        public static int Sandbox_HandleIndex_RegisterHandle( void* ptr, void* type )
        {
            return (int)Imports._ptr_Sandbox_HandleIndex_RegisterHandle( ptr, type );
        }

        public static void* Sandbox_HandleIndex_FreeHandle( void* handle )
        {
            return (void*)Imports._ptr_Sandbox_HandleIndex_FreeHandle( handle );
        }

        // Sandbox.LobbyManager
        public static void* Sandbox_LobbyManager_OnLobbyInvite( ulong lobbyid, ulong memberid )
        {
            return (void*)Imports._ptr_Sandbox_LobbyManager_OnLobbyInvite( lobbyid, memberid );
        }

        public static void* Sandbox_LobbyManager_OnMemberEntered( ulong lobbyid, ulong memberid )
        {
            return (void*)Imports._ptr_Sandbox_LobbyManager_OnMemberEntered( lobbyid, memberid );
        }

        public static void* Sandbox_LobbyManager_OnMemberLeave( ulong lobbyid, ulong memberid )
        {
            return (void*)Imports._ptr_Sandbox_LobbyManager_OnMemberLeave( lobbyid, memberid );
        }

        public static void* Sandbox_LobbyManager_OnEntered( ulong lobbyid )
        {
            return (void*)Imports._ptr_Sandbox_LobbyManager_OnEntered( lobbyid );
        }

        public static void* Sandbox_LobbyManager_OnCreated( ulong lobbyid )
        {
            return (void*)Imports._ptr_Sandbox_LobbyManager_OnCreated( lobbyid );
        }

        public static void* Sandbox_LobbyManager_OnDataUpdate( ulong lobbyid, ulong targetid )
        {
            return (void*)Imports._ptr_Sandbox_LobbyManager_OnDataUpdate( lobbyid, targetid );
        }

        public static void* Sandbox_LobbyManager_OnChatMessage( ulong lobbyid, ulong memberid, void* message, void* length )
        {
            return (void*)Imports._ptr_Sandbox_LobbyManager_OnChatMessage( lobbyid, memberid, message, length );
        }

        // Sandbox.Network.SteamNetwork
        public static void* SandboxNetwork_SteamNetwork_OnSocketConnection( void* socket, void* connection )
        {
            return (void*)Imports._ptr_SandboxNetwork_SteamNetwork_OnSocketConnection( socket, connection );
        }

        public static void* SandboxNetwork_SteamNetwork_OnSocketDisconnection( void* socket, void* connection )
        {
            return (void*)Imports._ptr_SandboxNetwork_SteamNetwork_OnSocketDisconnection( socket, connection );
        }

        public static int SandboxNetwork_SteamNetwork_ShouldAcceptConnection( void* socket, void* connection )
        {
            return (int)Imports._ptr_SandboxNetwork_SteamNetwork_ShouldAcceptConnection( socket, connection );
        }

        public static void* SandboxNetwork_SteamNetwork_OnDisconnection( void* connection, void* reasonCode, void* reasonString )
        {
            return (void*)Imports._ptr_SandboxNetwork_SteamNetwork_OnDisconnection( connection, reasonCode, reasonString );
        }

        public static void* SandboxNetwork_SteamNetwork_OnSessionEstablished( ulong steamId )
        {
            return (void*)Imports._ptr_SandboxNetwork_SteamNetwork_OnSessionEstablished( steamId );
        }

        public static void* SandboxNetwork_SteamNetwork_OnSessionFailed( void* socket, ulong steamId )
        {
            return (void*)Imports._ptr_SandboxNetwork_SteamNetwork_OnSessionFailed( socket, steamId );
        }

        // Sandbox.Physics.PhysicsJoint
        // Sandbox.Physics.PhysicsEngine
        public static void* SandboxPhysics_PhysicsEngine_OnPhysicsJointBreak( void* joint )
        {
            return (void*)Imports._ptr_SandboxPhysics_PhysicsEngine_OnPhysicsJointBreak( joint );
        }

        public static void* SandboxPhysics_PhysicsEngine_OnActive( void* body, void* transform, void* velocity, void* linearVelocity )
        {
            return (void*)Imports._ptr_SandboxPhysics_PhysicsEngine_OnActive( body, transform, velocity, linearVelocity );
        }

        // Sandbox.RealTime
        public static void* Sandbox_RealTime_Update( float time )
        {
            return (void*)Imports._ptr_Sandbox_RealTime_Update( time );
        }

        // Sandbox.Rendering.RenderPipeline
        public static void* SndbxRndrng_RenderPipeline_InternalAddLayersToView( void* view, void* viewport, void* hColor, void* hDepth, long nMSAA, void* pipelineAttributes, void* screenDimensions )
        {
            return (void*)Imports._ptr_SndbxRndrng_RenderPipeline_InternalAddLayersToView( view, viewport, hColor, hDepth, nMSAA, pipelineAttributes, screenDimensions );
        }

        public static void* SndbxRndrng_RenderPipeline_InternalPipelineEnd( void* view, void* viewport, void* hColor, void* hDepth, long nMSAA, void* pipelineAttributes, void* screenDimensions )
        {
            return (void*)Imports._ptr_SndbxRndrng_RenderPipeline_InternalPipelineEnd( view, viewport, hColor, hDepth, nMSAA, pipelineAttributes, screenDimensions );
        }

        // Sandbox.RenderTarget
        public static void* Sandbox_RenderTarget_Flush()
        {
            return (void*)Imports._ptr_Sandbox_RenderTarget_Flush();
        }

        // Sandbox.Resource
        public static void* Sandbox_Resource_OnResourceReloaded( void* name, void* resourceBinding )
        {
            return (void*)Imports._ptr_Sandbox_Resource_OnResourceReloaded( name, resourceBinding );
        }

        // Sandbox.SceneCustomObjectRender
        public static void* Sandbox_ScnCstmbjctRndr_RenderObject( void* setup, void* obj )
        {
            return (void*)Imports._ptr_Sandbox_ScnCstmbjctRndr_RenderObject( setup, obj );
        }

        // Sandbox.SceneSystem
        public static void* Sandbox_SceneSystem_OnBeforeRender( void* obj, void* setup )
        {
            return (void*)Imports._ptr_Sandbox_SceneSystem_OnBeforeRender( obj, setup );
        }

        public static void* Sandbox_SceneSystem_OnAfterRender( void* obj, void* setup )
        {
            return (void*)Imports._ptr_Sandbox_SceneSystem_OnAfterRender( obj, setup );
        }

        // Sandbox.Services.Inventory
        public static void* SndbxSrvcs_Inventory_OnDefinitionUpdate()
        {
            return (void*)Imports._ptr_SndbxSrvcs_Inventory_OnDefinitionUpdate();
        }

        public static void* SndbxSrvcs_Inventory_OnPricesUpdate( void* success, void* currency )
        {
            return (void*)Imports._ptr_SndbxSrvcs_Inventory_OnPricesUpdate( success, currency );
        }

        public static void* SndbxSrvcs_Inventory_OnPurchaseResult( void* success, ulong orderid, ulong transid )
        {
            return (void*)Imports._ptr_SndbxSrvcs_Inventory_OnPurchaseResult( success, orderid, transid );
        }

        // Sandbox.Services.ServerList
        public static void* SndbxSrvcs_ServerList_OnStarted( void* self )
        {
            return (void*)Imports._ptr_SndbxSrvcs_ServerList_OnStarted( self );
        }

        public static void* SndbxSrvcs_ServerList_OnServerResponded( void* self, void* ptr, ulong steamid )
        {
            return (void*)Imports._ptr_SndbxSrvcs_ServerList_OnServerResponded( self, ptr, steamid );
        }

        public static void* SndbxSrvcs_ServerList_OnFinished( void* self )
        {
            return (void*)Imports._ptr_SndbxSrvcs_ServerList_OnFinished( self );
        }

        // Sandbox.VideoPlayer
        public static void* Sandbox_VideoPlayer_OnTextureCreatedInternal( void* self )
        {
            return (void*)Imports._ptr_Sandbox_VideoPlayer_OnTextureCreatedInternal( self );
        }

        public static void* Sandbox_VideoPlayer_OnInitAudioInternal( void* self, void* sampleRate, void* channels )
        {
            return (void*)Imports._ptr_Sandbox_VideoPlayer_OnInitAudioInternal( self, sampleRate, channels );
        }

        public static void* Sandbox_VideoPlayer_OnFreeAudioInternal( void* self )
        {
            return (void*)Imports._ptr_Sandbox_VideoPlayer_OnFreeAudioInternal( self );
        }

        public static void* Sandbox_VideoPlayer_OnTextureDataInternal( void* self, void* data, void* width, void* height )
        {
            return (void*)Imports._ptr_Sandbox_VideoPlayer_OnTextureDataInternal( self, data, width, height );
        }

        public static void* Sandbox_VideoPlayer_OnLoadedInternal( void* self )
        {
            return (void*)Imports._ptr_Sandbox_VideoPlayer_OnLoadedInternal( self );
        }

        public static int Sandbox_VideoPlayer_WantsTextureData( void* self )
        {
            return (int)Imports._ptr_Sandbox_VideoPlayer_WantsTextureData( self );
        }

        public static void* Sandbox_VideoPlayer_OnFinishedInternal( void* self )
        {
            return (void*)Imports._ptr_Sandbox_VideoPlayer_OnFinishedInternal( self );
        }

        public static void* Sandbox_VideoPlayer_OnRepeatedInternal( void* self )
        {
            return (void*)Imports._ptr_Sandbox_VideoPlayer_OnRepeatedInternal( self );
        }

        // Sandbox.VR.VRSystem
        public static int SandboxVR_VRSystem_InternalIsActive()
        {
            return (int)Imports._ptr_SandboxVR_VRSystem_InternalIsActive();
        }

        public static int SandboxVR_VRSystem_InternalWantsInit()
        {
            return (int)Imports._ptr_SandboxVR_VRSystem_InternalWantsInit();
        }

        public static void* SandboxVR_VRSystem_BeginFrame()
        {
            return (void*)Imports._ptr_SandboxVR_VRSystem_BeginFrame();
        }

        public static void* SandboxVR_VRSystem_EndFrame()
        {
            return (void*)Imports._ptr_SandboxVR_VRSystem_EndFrame();
        }

        public static int SandboxVR_VRSystem_Submit( void* pTexture, void* pDepthTexture )
        {
            return (int)Imports._ptr_SandboxVR_VRSystem_Submit( pTexture, pDepthTexture );
        }

        public static void* SandboxVR_VRSystem_GetVulkanInstanceExtensionsRequired()
        {
            return (void*)Imports._ptr_SandboxVR_VRSystem_GetVulkanInstanceExtensionsRequired();
        }

        public static void* SandboxVR_VRSystem_GetVulkanDeviceExtensionsRequired()
        {
            return (void*)Imports._ptr_SandboxVR_VRSystem_GetVulkanDeviceExtensionsRequired();
        }

        public static void* SandboxVR_VRSystem_RenderOverlays()
        {
            return (void*)Imports._ptr_SandboxVR_VRSystem_RenderOverlays();
        }

        // Steamworks.Dispatch
        public static void* Steamworks_Dispatch_OnClientCallback( void* type, void* data, void* datasize, void* isServer )
        {
            return (void*)Imports._ptr_Steamworks_Dispatch_OnClientCallback( type, data, datasize, isServer );
        }
    }
}
