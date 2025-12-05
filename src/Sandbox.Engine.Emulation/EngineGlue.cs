using System;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Generated;

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
}
