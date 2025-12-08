using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;

namespace Sandbox.Engine.Emulation.Generated
{
    public static unsafe partial class Exports
    {
	public static unsafe void FillNativeFunctionsHammer(void** managedFunctions, void** nativeFunctions, int* structSizes)
 	{
 		var i = 0;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged<IntPtr, void>)&Sandbox.Engine.Emulation.EngineExports.DebugError;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CHammerApp_RefreshEntitiesGameData;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CHammerApp_GetCurrentMaterial;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CHammerApp_SetCurrentTexture;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CHammerApp_GetActiveMapAsset;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CHammerApp_GetActiveMapDoc;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CHammerApp_MarkAllViewHudsDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CHammerApp_SelectObjectsUsingAsset;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CHammerApp_SelectFacesUsingMaterial;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CHammerApp_AssignAssetToSelection;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CHammerApp_ShowEntityReportForAsset;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CHammerApp_OnFileReload;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CHmmrdtrSssn_ShowLoadingProgressBar;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CHmmrdtrSssn_HideLoadingProgressBar;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CHmmrdtrSssn_GetMapAsset;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CHmmrdtrSssn_GetMapDoc;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void* >)&CHistory_MarkUndoPosition;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CHistory_Keep;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CHistory_KeepNew;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void* >)&CHistory_GetHistory;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapDoc_GetMapWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapDoc_GetPathName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapDoc_GetSelection;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void* >)&CMapDoc_AddObjectToDocument;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapDoc_DeleteObject;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapDoc_CreateEmptyMesh;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapDoc_CreateEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapDoc_CreateGameObject;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapDoc_CreateMapGroup;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapDoc_CreateMapInstance;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapDoc_CreateMapPath;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapDoc_CreateStaticOverlay;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapNode_To_CMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapNode_From_CMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_GetClassName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapEntity_SetClass;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void* >)&CMapEntity_SetKeyValue;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapEntity_GetKeyValue;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapEntity_TargetNameMatches;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void* >)&CMapEntity_SetDefaultBounds;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapEntity_GetNodeID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_GetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapEntity_SetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapEntity_GetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapEntity_SetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapEntity_GetParentWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int >)&CMapEntity_GetRootDocument;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_GetDescription;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_GetTypeString;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapEntity_Copy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapEntity_IsVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CMapEntity_SetVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapEntity_IsSelected;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapEntity_GetChildCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapEntity_GetChild;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapEntity_GetFirstDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapEntity_GetNextDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapEntity_AsMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_GetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_GetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_GetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapEntity_SetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapEntity_SetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapEntity_SetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CMapEntity_BeginTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, long, void* >)&CMapEntity_Transform;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_EndTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_MarkBoundsDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_FullBoundsUpdate;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_SetModifiedFlag;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapEntity_GeneratesEntityModelGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_DescriptionChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapEntity_CoreAttributeChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapNode_To_CMapGameObject;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapNode_From_CMapGameObject;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapGameObject_SetGUID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_GetGUID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGameObject_GetNodeID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_GetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapGameObject_SetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGameObject_GetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapGameObject_SetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGameObject_GetParentWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int >)&CMapGameObject_GetRootDocument;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_GetDescription;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_GetTypeString;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGameObject_Copy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGameObject_IsVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CMapGameObject_SetVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGameObject_IsSelected;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGameObject_GetChildCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapGameObject_GetChild;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapGameObject_GetFirstDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapGameObject_GetNextDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGameObject_AsMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_GetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_GetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_GetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapGameObject_SetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapGameObject_SetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapGameObject_SetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CMapGameObject_BeginTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, long, void* >)&CMapGameObject_Transform;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_EndTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_MarkBoundsDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_FullBoundsUpdate;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_SetModifiedFlag;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGameObject_GeneratesEntityModelGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_DescriptionChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGameObject_CoreAttributeChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapNode_To_CMapGroup;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapNode_From_CMapGroup;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGroup_GetNodeID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_GetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapGroup_SetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGroup_GetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapGroup_SetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGroup_GetParentWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int >)&CMapGroup_GetRootDocument;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_GetDescription;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_GetTypeString;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGroup_Copy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGroup_IsVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CMapGroup_SetVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGroup_IsSelected;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGroup_GetChildCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapGroup_GetChild;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapGroup_GetFirstDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapGroup_GetNextDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGroup_AsMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_GetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_GetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_GetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapGroup_SetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapGroup_SetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapGroup_SetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CMapGroup_BeginTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, long, void* >)&CMapGroup_Transform;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_EndTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_MarkBoundsDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_FullBoundsUpdate;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_SetModifiedFlag;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapGroup_GeneratesEntityModelGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_DescriptionChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapGroup_CoreAttributeChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapNode_To_CMapInstance;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapNode_From_CMapInstance;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapInstance_SetTarget;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapInstance_GetTarget;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapInstance_GetNodeID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_GetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapInstance_SetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapInstance_GetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapInstance_SetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapInstance_GetParentWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int >)&CMapInstance_GetRootDocument;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_GetDescription;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_GetTypeString;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapInstance_Copy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapInstance_IsVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CMapInstance_SetVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapInstance_IsSelected;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapInstance_GetChildCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapInstance_GetChild;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapInstance_GetFirstDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapInstance_GetNextDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapInstance_AsMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_GetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_GetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_GetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapInstance_SetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapInstance_SetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapInstance_SetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CMapInstance_BeginTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, long, void* >)&CMapInstance_Transform;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_EndTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_MarkBoundsDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_FullBoundsUpdate;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_SetModifiedFlag;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapInstance_GeneratesEntityModelGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_DescriptionChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapInstance_CoreAttributeChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapNode_To_CMapMesh;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapNode_From_CMapMesh;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapMesh_AssignMaterialToMesh;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void*, void*, int, void*, int, void*, void*, int, float, void* >)&CMapMesh_ConstructFromData;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapMesh_GetFaceMaterials;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapMesh_GetNodeID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_GetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapMesh_SetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapMesh_GetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapMesh_SetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapMesh_GetParentWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int >)&CMapMesh_GetRootDocument;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_GetDescription;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_GetTypeString;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapMesh_Copy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapMesh_IsVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CMapMesh_SetVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapMesh_IsSelected;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapMesh_GetChildCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapMesh_GetChild;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapMesh_GetFirstDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapMesh_GetNextDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapMesh_AsMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_GetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_GetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_GetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapMesh_SetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapMesh_SetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapMesh_SetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CMapMesh_BeginTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, long, void* >)&CMapMesh_Transform;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_EndTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_MarkBoundsDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_FullBoundsUpdate;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_SetModifiedFlag;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapMesh_GeneratesEntityModelGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_DescriptionChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapMesh_CoreAttributeChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapNode_GetNodeID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_GetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapNode_SetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapNode_GetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapNode_SetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapNode_GetParentWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int >)&CMapNode_GetRootDocument;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_GetDescription;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_GetTypeString;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapNode_Copy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapNode_IsVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CMapNode_SetVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapNode_IsSelected;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapNode_GetChildCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapNode_GetChild;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapNode_GetFirstDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapNode_GetNextDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapNode_AsMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_GetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_GetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_GetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapNode_SetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapNode_SetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapNode_SetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CMapNode_BeginTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, long, void* >)&CMapNode_Transform;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_EndTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_MarkBoundsDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_FullBoundsUpdate;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_SetModifiedFlag;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapNode_GeneratesEntityModelGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_DescriptionChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapNode_CoreAttributeChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapEntity_To_CMapPath;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapEntity_From_CMapPath;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapNode_To_CMapPath;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapNode_From_CMapPath;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPath_AddNewNodeToPath;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_GetClassName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPath_SetClass;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void* >)&CMapPath_SetKeyValue;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPath_GetKeyValue;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapPath_TargetNameMatches;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void* >)&CMapPath_SetDefaultBounds;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPath_GetNodeID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_GetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPath_SetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPath_GetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPath_SetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPath_GetParentWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int >)&CMapPath_GetRootDocument;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_GetDescription;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_GetTypeString;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPath_Copy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPath_IsVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CMapPath_SetVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPath_IsSelected;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPath_GetChildCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapPath_GetChild;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapPath_GetFirstDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapPath_GetNextDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPath_AsMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_GetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_GetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_GetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPath_SetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPath_SetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPath_SetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CMapPath_BeginTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, long, void* >)&CMapPath_Transform;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_EndTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_MarkBoundsDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_FullBoundsUpdate;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_SetModifiedFlag;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPath_GeneratesEntityModelGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_DescriptionChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPath_CoreAttributeChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapEntity_To_CMapPathNode;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapEntity_From_CMapPathNode;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapNode_To_CMapPathNode;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapNode_From_CMapPathNode;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_GetClassName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPathNode_SetClass;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void* >)&CMapPathNode_SetKeyValue;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPathNode_GetKeyValue;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapPathNode_TargetNameMatches;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void* >)&CMapPathNode_SetDefaultBounds;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPathNode_GetNodeID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_GetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPathNode_SetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPathNode_GetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPathNode_SetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPathNode_GetParentWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int >)&CMapPathNode_GetRootDocument;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_GetDescription;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_GetTypeString;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPathNode_Copy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPathNode_IsVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CMapPathNode_SetVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPathNode_IsSelected;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPathNode_GetChildCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapPathNode_GetChild;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapPathNode_GetFirstDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapPathNode_GetNextDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPathNode_AsMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_GetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_GetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_GetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPathNode_SetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPathNode_SetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapPathNode_SetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CMapPathNode_BeginTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, long, void* >)&CMapPathNode_Transform;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_EndTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_MarkBoundsDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_FullBoundsUpdate;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_SetModifiedFlag;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapPathNode_GeneratesEntityModelGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_DescriptionChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapPathNode_CoreAttributeChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapMesh_To_CMapStaticOverlay;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapMesh_From_CMapStaticOverlay;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapNode_To_CMapStaticOverlay;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapNode_From_CMapStaticOverlay;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, float, float, void*, void* >)&CMpSttcvrly_CreateCenteredQuad;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMpSttcvrly_AssignMaterialToMesh;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void*, void*, int, void*, int, void*, void*, int, float, void* >)&CMpSttcvrly_ConstructFromData;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMpSttcvrly_GetFaceMaterials;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMpSttcvrly_GetNodeID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_GetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMpSttcvrly_SetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMpSttcvrly_GetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMpSttcvrly_SetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMpSttcvrly_GetParentWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int >)&CMpSttcvrly_GetRootDocument;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_GetDescription;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_GetTypeString;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMpSttcvrly_Copy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMpSttcvrly_IsVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CMpSttcvrly_SetVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMpSttcvrly_IsSelected;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMpSttcvrly_GetChildCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMpSttcvrly_GetChild;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMpSttcvrly_GetFirstDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMpSttcvrly_GetNextDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMpSttcvrly_AsMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_GetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_GetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_GetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMpSttcvrly_SetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMpSttcvrly_SetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMpSttcvrly_SetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CMpSttcvrly_BeginTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, long, void* >)&CMpSttcvrly_Transform;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_EndTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_MarkBoundsDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_FullBoundsUpdate;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_SetModifiedFlag;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMpSttcvrly_GeneratesEntityModelGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_DescriptionChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMpSttcvrly_CoreAttributeChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapView_GetMapDoc;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapView_MarkHudDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapView_GetCamera;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapView_IsActive;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapView_GetMousePosition;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void*, int, void* >)&CMapView_EnterFreeDragMode;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&CMapView_UpdateFreeDragMode;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CMapView_ExitFreeDragMode;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void*, void* >)&CMapView_GetDropTarget;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, float, void* >)&CMapView_UpdateManagedGizmoState;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long >)&CMapView_GetManipulationMode;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, float >)&CMapView_HitDistanceAtMouse;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CMapNode_To_CMapWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CMapNode_From_CMapWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapWorld_SetSerializedScene;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_GetSerializedScene;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapWorld_FindNodeByID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapWorld_FindEntityByNodeId;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapWorld_Trace;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapWorld_GetNodeID;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_GetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapWorld_SetName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapWorld_GetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapWorld_SetParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapWorld_GetParentWorld;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int >)&CMapWorld_GetRootDocument;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_GetDescription;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_GetTypeString;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapWorld_Copy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapWorld_IsVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CMapWorld_SetVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapWorld_IsSelected;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapWorld_GetChildCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&CMapWorld_GetChild;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapWorld_GetFirstDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CMapWorld_GetNextDescendent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapWorld_AsMapEntity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_GetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_GetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_GetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapWorld_SetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapWorld_SetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CMapWorld_SetScales;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CMapWorld_BeginTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, long, void* >)&CMapWorld_Transform;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_EndTransformOperation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_MarkBoundsDirty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_FullBoundsUpdate;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_SetModifiedFlag;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CMapWorld_GeneratesEntityModelGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_DescriptionChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMapWorld_CoreAttributeChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_CFramelessMainWindow_To_CQHammerMainWnd;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_CFramelessMainWindow_From_CQHammerMainWnd;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_QMainWindow_To_CQHammerMainWnd;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_QMainWindow_From_CQHammerMainWnd;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_QWidget_To_CQHammerMainWnd;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_QWidget_From_CQHammerMainWnd;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&From_QObject_To_CQHammerMainWnd;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&To_QObject_From_CQHammerMainWnd;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_CreateEverything;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_CreateMenus;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_SetupDefaultLayout;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void*, void*, void*, void*, void*, void* >)&CQHammerMainWnd_SetTitleBarWidgets;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_iconSize;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setIconSize;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_menuBar;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setMenuBar;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setMenuWidget;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_statusBar;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setStatusBar;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_centralWidget;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setCentralWidget;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_isAnimated;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_setAnimated;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void* >)&CQHammerMainWnd_addToolBarBreak;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_insertToolBarBreak;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void*, void* >)&CQHammerMainWnd_addToolBar;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_addToolBar_1;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void* >)&CQHammerMainWnd_insertToolBar;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_removeToolBar;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_removeToolBarBreak;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_saveState;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CQHammerMainWnd_restoreState;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_isTopLevel;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_isWindow;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_isModal;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setStyleSheet;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_windowTitle;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setWindowTitle;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void* >)&CQHammerMainWnd_setWindowFlags;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long >)&CQHammerMainWnd_windowFlags;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_size;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_resize;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_minimumSize;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setMinimumSize;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_maximumSize;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setMaximumSize;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_sizeHint;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_pos;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_move;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_isEnabled;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_setEnabled;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_setVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_show;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_hide;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_showMinimized;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_showMaximized;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_showFullScreen;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_showNormal;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_close;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_raise;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_lower;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_isVisible;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_isHidden;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_setHidden;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int, void* >)&CQHammerMainWnd_setAttribute;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, int >)&CQHammerMainWnd_testAttribute;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_acceptDrops;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_setAcceptDrops;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_updateGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_update;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_repaint;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void* >)&CQHammerMainWnd_setCursor;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setCursor_1;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_unsetCursor;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setWindowIcon;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setWindowIconFromPixmap;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setWindowIconText;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, float, void* >)&CQHammerMainWnd_setWindowOpacity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, float >)&CQHammerMainWnd_windowOpacity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_isMinimized;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_isMaximized;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_isFullScreen;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_setMouseTracking;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_hasMouseTracking;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_underMouse;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_mapToGlobal;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_mapFromGlobal;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_hasFocus;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long >)&CQHammerMainWnd_focusPolicy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void* >)&CQHammerMainWnd_setFocusPolicy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setFocusProxy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_isActiveWindow;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_updatesEnabled;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_setUpdatesEnabled;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_setFocus;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_activateWindow;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_clearFocus;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, float >)&CQHammerMainWnd_devicePixelRatioF;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_saveGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CQHammerMainWnd_restoreGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_addAction;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_removeAction;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_parentWidget;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_window;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_AddClassName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_Polish;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_toolTip;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setToolTip;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_statusTip;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setStatusTip;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_toolTipDuration;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_setToolTipDuration;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CQHammerMainWnd_autoFillBackground;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_setAutoFillBackground;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_adjustSize;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long >)&CQHammerMainWnd_windowModality;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void* >)&CQHammerMainWnd_setWindowModality;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_ScreenGeometry;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int, int, int, void* >)&CQHammerMainWnd_setContentsMargins;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_contentsMargins;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_layout;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setLayout;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_contentsRect;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, float, void* >)&CQHammerMainWnd_SetEffectOpacity;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CQHammerMainWnd_setSizePolicy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long >)&CQHammerMainWnd_GetHorizontalSizePolicy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long >)&CQHammerMainWnd_GetVerticalSizePolicy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void* >)&CQHammerMainWnd_SetHorizontalSizePolicy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void* >)&CQHammerMainWnd_SetVerticalSizePolicy;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int, void* >)&CQHammerMainWnd_setFixedSize;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_setFixedWidth;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CQHammerMainWnd_setFixedHeight;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_winId;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_deleteLater;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQHammerMainWnd_objectName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setObjectName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQHammerMainWnd_setParent_1;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&CQHammerMainWnd_setProperty;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, float, void* >)&CQHammerMainWnd_setProperty_1;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void* >)&CQHammerMainWnd_setProperty_2;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CSavedObjects_DeleteThis;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void* >)&CSavedObjects_Create;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CSavedObjects_RemoveAll;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CSavedObjects_SaveObject;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CSavedObjects_RestoreObjects;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CSavedObjects_Count;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long >)&CSelection_GetMode;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, long, void* >)&CSelection_SetMode;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CSelection_ActiveSelectionSet;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void* >)&CSelection_GetSelectionSetForMode;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CSelection_GetNumSelectionSets;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CSelection_GetSelectionSet;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CToolBlock_OnObjectTypeChanged;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CToolBlock_SetPrimitiveType2D;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CToolBlock_GetOrientPrimitives;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CToolBlock_SetOrientPrimitives;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*>)&_Get__CToolBlock_m_OverrideMaterial;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged[SuppressGCTransition]<void*, void*, void*>)&_Set__CToolBlock_m_OverrideMaterial;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CToolBlockState_GetAABBBounds;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CToolBlockState_GetDragWorkPlane;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CToolCamera_GetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CToolCamera_GetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CToolCamera_SetOrigin;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CToolCamera_SetAngles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, float >)&CToolCamera_GetWidth;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, float >)&CToolCamera_GetHeight;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, float >)&CToolCamera_GetCameraFOV;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, void*, void* >)&CToolCamera_BuildRay;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CToolEntity_StartBlockEntityCreation;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CWorkPlane_GetWorkPlaneToWorldTransform;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&global_MaterialGetMappingWidth;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&global_MaterialGetMappingHeight;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, long, int >)&ISelectionSet_SelectObject;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&ISelectionSet_GetSelectedObject;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&ISelectionSet_Count;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&ISelectionSet_RemoveAll;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&ISelectionSet_SelectAll;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&ISelectionSet_InvertSelection;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&ISelectionSet_GetPivotPosition;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&ISelectionSet_SetPivot;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< int >)&NativeHammer_Options_GetShowHelpers;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< int >)&NativeHammer_Options_GetShowGameObjectsOnly;
 	}

        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_RefreshEntitiesGameData")]
        public static void* CHammerApp_RefreshEntitiesGameData( void* self )
        {
            throw new NotImplementedException("CHammerApp_RefreshEntitiesGameData is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_GetCurrentMaterial")]
        public static void* CHammerApp_GetCurrentMaterial( void* self )
        {
            throw new NotImplementedException("CHammerApp_GetCurrentMaterial is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_SetCurrentTexture")]
        public static void* CHammerApp_SetCurrentTexture( void* self, void* assetFilename )
        {
            throw new NotImplementedException("CHammerApp_SetCurrentTexture is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_GetActiveMapAsset")]
        public static int CHammerApp_GetActiveMapAsset( void* self )
        {
            throw new NotImplementedException("CHammerApp_GetActiveMapAsset is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_GetActiveMapDoc")]
        public static int CHammerApp_GetActiveMapDoc( void* self )
        {
            throw new NotImplementedException("CHammerApp_GetActiveMapDoc is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_MarkAllViewHudsDirty")]
        public static void* CHammerApp_MarkAllViewHudsDirty( void* self )
        {
            throw new NotImplementedException("CHammerApp_MarkAllViewHudsDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_SelectObjectsUsingAsset")]
        public static void* CHammerApp_SelectObjectsUsingAsset( void* self, void* assetFilename )
        {
            throw new NotImplementedException("CHammerApp_SelectObjectsUsingAsset is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_SelectFacesUsingMaterial")]
        public static void* CHammerApp_SelectFacesUsingMaterial( void* self, void* assetFilename )
        {
            throw new NotImplementedException("CHammerApp_SelectFacesUsingMaterial is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_AssignAssetToSelection")]
        public static void* CHammerApp_AssignAssetToSelection( void* self, void* assetFilename )
        {
            throw new NotImplementedException("CHammerApp_AssignAssetToSelection is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_ShowEntityReportForAsset")]
        public static void* CHammerApp_ShowEntityReportForAsset( void* self, void* assetFilename )
        {
            throw new NotImplementedException("CHammerApp_ShowEntityReportForAsset is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_OnFileReload")]
        public static void* CHammerApp_OnFileReload( void* self )
        {
            throw new NotImplementedException("CHammerApp_OnFileReload is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHmmrdtrSssn_ShowLoadingProgressBar")]
        public static void* CHmmrdtrSssn_ShowLoadingProgressBar( void* self )
        {
            throw new NotImplementedException("CHmmrdtrSssn_ShowLoadingProgressBar is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHmmrdtrSssn_HideLoadingProgressBar")]
        public static void* CHmmrdtrSssn_HideLoadingProgressBar( void* self )
        {
            throw new NotImplementedException("CHmmrdtrSssn_HideLoadingProgressBar is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHmmrdtrSssn_GetMapAsset")]
        public static void* CHmmrdtrSssn_GetMapAsset( void* self )
        {
            throw new NotImplementedException("CHmmrdtrSssn_GetMapAsset is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHmmrdtrSssn_GetMapDoc")]
        public static int CHmmrdtrSssn_GetMapDoc( void* self )
        {
            throw new NotImplementedException("CHmmrdtrSssn_GetMapDoc is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHistory_MarkUndoPosition")]
        public static void* CHistory_MarkUndoPosition( void* self, void* pSelection, void* pszName )
        {
            throw new NotImplementedException("CHistory_MarkUndoPosition is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHistory_Keep")]
        public static void* CHistory_Keep( void* self, void* pUndoable )
        {
            throw new NotImplementedException("CHistory_Keep is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHistory_KeepNew")]
        public static void* CHistory_KeepNew( void* self, void* pUndoable )
        {
            throw new NotImplementedException("CHistory_KeepNew is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CHistory_GetHistory")]
        public static void* CHistory_GetHistory()
        {
            throw new NotImplementedException("CHistory_GetHistory is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_GetMapWorld")]
        public static int CMapDoc_GetMapWorld( void* self )
        {
            throw new NotImplementedException("CMapDoc_GetMapWorld is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_GetPathName")]
        public static void* CMapDoc_GetPathName( void* self )
        {
            throw new NotImplementedException("CMapDoc_GetPathName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_GetSelection")]
        public static void* CMapDoc_GetSelection( void* self )
        {
            throw new NotImplementedException("CMapDoc_GetSelection is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_AddObjectToDocument")]
        public static void* CMapDoc_AddObjectToDocument( void* self, void* pObject, void* pParent )
        {
            throw new NotImplementedException("CMapDoc_AddObjectToDocument is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_DeleteObject")]
        public static void* CMapDoc_DeleteObject( void* self, void* node )
        {
            throw new NotImplementedException("CMapDoc_DeleteObject is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateEmptyMesh")]
        public static int CMapDoc_CreateEmptyMesh( void* self, int addToDocument )
        {
            throw new NotImplementedException("CMapDoc_CreateEmptyMesh is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateEntity")]
        public static int CMapDoc_CreateEntity( void* self, int addToDocument )
        {
            throw new NotImplementedException("CMapDoc_CreateEntity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateGameObject")]
        public static int CMapDoc_CreateGameObject( void* self, int addToDocument )
        {
            throw new NotImplementedException("CMapDoc_CreateGameObject is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateMapGroup")]
        public static void* CMapDoc_CreateMapGroup( void* self )
        {
            throw new NotImplementedException("CMapDoc_CreateMapGroup is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateMapInstance")]
        public static void* CMapDoc_CreateMapInstance( void* self )
        {
            throw new NotImplementedException("CMapDoc_CreateMapInstance is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateMapPath")]
        public static void* CMapDoc_CreateMapPath( void* self )
        {
            throw new NotImplementedException("CMapDoc_CreateMapPath is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateStaticOverlay")]
        public static void* CMapDoc_CreateStaticOverlay( void* self )
        {
            throw new NotImplementedException("CMapDoc_CreateStaticOverlay is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapEntity", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapNode_To_CMapEntity( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapEntity", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapNode_From_CMapEntity( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetClassName")]
        public static void* CMapEntity_GetClassName( void* self )
        {
            throw new NotImplementedException("CMapEntity_GetClassName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetClass")]
        public static void* CMapEntity_SetClass( void* self, void* classname )
        {
            throw new NotImplementedException("CMapEntity_SetClass is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetKeyValue")]
        public static void* CMapEntity_SetKeyValue( void* self, void* key, void* value )
        {
            throw new NotImplementedException("CMapEntity_SetKeyValue is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetKeyValue")]
        public static void* CMapEntity_GetKeyValue( void* self, void* key )
        {
            throw new NotImplementedException("CMapEntity_GetKeyValue is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_TargetNameMatches")]
        public static int CMapEntity_TargetNameMatches( void* self, void* name )
        {
            throw new NotImplementedException("CMapEntity_TargetNameMatches is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetDefaultBounds")]
        public static void* CMapEntity_SetDefaultBounds( void* self, void* minBounds, void* maxBounds )
        {
            throw new NotImplementedException("CMapEntity_SetDefaultBounds is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetNodeID")]
        public static int CMapEntity_GetNodeID( void* self )
        {
            throw new NotImplementedException("CMapEntity_GetNodeID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetName")]
        public static void* CMapEntity_GetName( void* self )
        {
            throw new NotImplementedException("CMapEntity_GetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetName")]
        public static void* CMapEntity_SetName( void* self, void* name )
        {
            throw new NotImplementedException("CMapEntity_SetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetParent")]
        public static int CMapEntity_GetParent( void* self )
        {
            throw new NotImplementedException("CMapEntity_GetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetParent")]
        public static void* CMapEntity_SetParent( void* self, void* parent )
        {
            throw new NotImplementedException("CMapEntity_SetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetParentWorld")]
        public static int CMapEntity_GetParentWorld( void* self )
        {
            throw new NotImplementedException("CMapEntity_GetParentWorld is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetRootDocument")]
        public static int CMapEntity_GetRootDocument( void* self, long nDocumentLoaded )
        {
            throw new NotImplementedException("CMapEntity_GetRootDocument is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetDescription")]
        public static void* CMapEntity_GetDescription( void* self )
        {
            throw new NotImplementedException("CMapEntity_GetDescription is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetTypeString")]
        public static void* CMapEntity_GetTypeString( void* self )
        {
            throw new NotImplementedException("CMapEntity_GetTypeString is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_Copy")]
        public static int CMapEntity_Copy( void* self )
        {
            throw new NotImplementedException("CMapEntity_Copy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_IsVisible")]
        public static int CMapEntity_IsVisible( void* self )
        {
            throw new NotImplementedException("CMapEntity_IsVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetVisible")]
        public static void* CMapEntity_SetVisible( void* self, int bVisible )
        {
            throw new NotImplementedException("CMapEntity_SetVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_IsSelected")]
        public static int CMapEntity_IsSelected( void* self )
        {
            throw new NotImplementedException("CMapEntity_IsSelected is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetChildCount")]
        public static int CMapEntity_GetChildCount( void* self )
        {
            throw new NotImplementedException("CMapEntity_GetChildCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetChild")]
        public static int CMapEntity_GetChild( void* self, int i )
        {
            throw new NotImplementedException("CMapEntity_GetChild is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetFirstDescendent")]
        public static int CMapEntity_GetFirstDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapEntity_GetFirstDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetNextDescendent")]
        public static int CMapEntity_GetNextDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapEntity_GetNextDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_AsMapEntity")]
        public static int CMapEntity_AsMapEntity( void* self )
        {
            throw new NotImplementedException("CMapEntity_AsMapEntity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetOrigin")]
        public static void* CMapEntity_GetOrigin( void* self )
        {
            throw new NotImplementedException("CMapEntity_GetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetAngles")]
        public static void* CMapEntity_GetAngles( void* self )
        {
            throw new NotImplementedException("CMapEntity_GetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetScales")]
        public static void* CMapEntity_GetScales( void* self )
        {
            throw new NotImplementedException("CMapEntity_GetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetOrigin")]
        public static void* CMapEntity_SetOrigin( void* self, void* origin )
        {
            throw new NotImplementedException("CMapEntity_SetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetAngles")]
        public static void* CMapEntity_SetAngles( void* self, void* angles )
        {
            throw new NotImplementedException("CMapEntity_SetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetScales")]
        public static void* CMapEntity_SetScales( void* self, void* scales )
        {
            throw new NotImplementedException("CMapEntity_SetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_BeginTransformOperation")]
        public static void* CMapEntity_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            throw new NotImplementedException("CMapEntity_BeginTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_Transform")]
        public static void* CMapEntity_Transform( void* self, void* matrix, long nFlags )
        {
            throw new NotImplementedException("CMapEntity_Transform is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_EndTransformOperation")]
        public static void* CMapEntity_EndTransformOperation( void* self )
        {
            throw new NotImplementedException("CMapEntity_EndTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_MarkBoundsDirty")]
        public static void* CMapEntity_MarkBoundsDirty( void* self )
        {
            throw new NotImplementedException("CMapEntity_MarkBoundsDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_FullBoundsUpdate")]
        public static void* CMapEntity_FullBoundsUpdate( void* self )
        {
            throw new NotImplementedException("CMapEntity_FullBoundsUpdate is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetModifiedFlag")]
        public static void* CMapEntity_SetModifiedFlag( void* self )
        {
            throw new NotImplementedException("CMapEntity_SetModifiedFlag is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GeneratesEntityModelGeometry")]
        public static int CMapEntity_GeneratesEntityModelGeometry( void* self )
        {
            throw new NotImplementedException("CMapEntity_GeneratesEntityModelGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_DescriptionChanged")]
        public static void* CMapEntity_DescriptionChanged( void* self )
        {
            throw new NotImplementedException("CMapEntity_DescriptionChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_CoreAttributeChanged")]
        public static void* CMapEntity_CoreAttributeChanged( void* self )
        {
            throw new NotImplementedException("CMapEntity_CoreAttributeChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapGameObject", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapNode_To_CMapGameObject( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapGameObject", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapNode_From_CMapGameObject( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetGUID")]
        public static void* CMapGameObject_SetGUID( void* self, void* json )
        {
            throw new NotImplementedException("CMapGameObject_SetGUID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetGUID")]
        public static void* CMapGameObject_GetGUID( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GetGUID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetNodeID")]
        public static int CMapGameObject_GetNodeID( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GetNodeID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetName")]
        public static void* CMapGameObject_GetName( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetName")]
        public static void* CMapGameObject_SetName( void* self, void* name )
        {
            throw new NotImplementedException("CMapGameObject_SetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetParent")]
        public static int CMapGameObject_GetParent( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetParent")]
        public static void* CMapGameObject_SetParent( void* self, void* parent )
        {
            throw new NotImplementedException("CMapGameObject_SetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetParentWorld")]
        public static int CMapGameObject_GetParentWorld( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GetParentWorld is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetRootDocument")]
        public static int CMapGameObject_GetRootDocument( void* self, long nDocumentLoaded )
        {
            throw new NotImplementedException("CMapGameObject_GetRootDocument is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetDescription")]
        public static void* CMapGameObject_GetDescription( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GetDescription is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetTypeString")]
        public static void* CMapGameObject_GetTypeString( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GetTypeString is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_Copy")]
        public static int CMapGameObject_Copy( void* self )
        {
            throw new NotImplementedException("CMapGameObject_Copy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_IsVisible")]
        public static int CMapGameObject_IsVisible( void* self )
        {
            throw new NotImplementedException("CMapGameObject_IsVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetVisible")]
        public static void* CMapGameObject_SetVisible( void* self, int bVisible )
        {
            throw new NotImplementedException("CMapGameObject_SetVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_IsSelected")]
        public static int CMapGameObject_IsSelected( void* self )
        {
            throw new NotImplementedException("CMapGameObject_IsSelected is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetChildCount")]
        public static int CMapGameObject_GetChildCount( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GetChildCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetChild")]
        public static int CMapGameObject_GetChild( void* self, int i )
        {
            throw new NotImplementedException("CMapGameObject_GetChild is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetFirstDescendent")]
        public static int CMapGameObject_GetFirstDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapGameObject_GetFirstDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetNextDescendent")]
        public static int CMapGameObject_GetNextDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapGameObject_GetNextDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_AsMapEntity")]
        public static int CMapGameObject_AsMapEntity( void* self )
        {
            throw new NotImplementedException("CMapGameObject_AsMapEntity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetOrigin")]
        public static void* CMapGameObject_GetOrigin( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetAngles")]
        public static void* CMapGameObject_GetAngles( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetScales")]
        public static void* CMapGameObject_GetScales( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetOrigin")]
        public static void* CMapGameObject_SetOrigin( void* self, void* origin )
        {
            throw new NotImplementedException("CMapGameObject_SetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetAngles")]
        public static void* CMapGameObject_SetAngles( void* self, void* angles )
        {
            throw new NotImplementedException("CMapGameObject_SetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetScales")]
        public static void* CMapGameObject_SetScales( void* self, void* scales )
        {
            throw new NotImplementedException("CMapGameObject_SetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_BeginTransformOperation")]
        public static void* CMapGameObject_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            throw new NotImplementedException("CMapGameObject_BeginTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_Transform")]
        public static void* CMapGameObject_Transform( void* self, void* matrix, long nFlags )
        {
            throw new NotImplementedException("CMapGameObject_Transform is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_EndTransformOperation")]
        public static void* CMapGameObject_EndTransformOperation( void* self )
        {
            throw new NotImplementedException("CMapGameObject_EndTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_MarkBoundsDirty")]
        public static void* CMapGameObject_MarkBoundsDirty( void* self )
        {
            throw new NotImplementedException("CMapGameObject_MarkBoundsDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_FullBoundsUpdate")]
        public static void* CMapGameObject_FullBoundsUpdate( void* self )
        {
            throw new NotImplementedException("CMapGameObject_FullBoundsUpdate is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetModifiedFlag")]
        public static void* CMapGameObject_SetModifiedFlag( void* self )
        {
            throw new NotImplementedException("CMapGameObject_SetModifiedFlag is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GeneratesEntityModelGeometry")]
        public static int CMapGameObject_GeneratesEntityModelGeometry( void* self )
        {
            throw new NotImplementedException("CMapGameObject_GeneratesEntityModelGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_DescriptionChanged")]
        public static void* CMapGameObject_DescriptionChanged( void* self )
        {
            throw new NotImplementedException("CMapGameObject_DescriptionChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_CoreAttributeChanged")]
        public static void* CMapGameObject_CoreAttributeChanged( void* self )
        {
            throw new NotImplementedException("CMapGameObject_CoreAttributeChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapGroup", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapNode_To_CMapGroup( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapGroup", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapNode_From_CMapGroup( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetNodeID")]
        public static int CMapGroup_GetNodeID( void* self )
        {
            throw new NotImplementedException("CMapGroup_GetNodeID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetName")]
        public static void* CMapGroup_GetName( void* self )
        {
            throw new NotImplementedException("CMapGroup_GetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetName")]
        public static void* CMapGroup_SetName( void* self, void* name )
        {
            throw new NotImplementedException("CMapGroup_SetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetParent")]
        public static int CMapGroup_GetParent( void* self )
        {
            throw new NotImplementedException("CMapGroup_GetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetParent")]
        public static void* CMapGroup_SetParent( void* self, void* parent )
        {
            throw new NotImplementedException("CMapGroup_SetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetParentWorld")]
        public static int CMapGroup_GetParentWorld( void* self )
        {
            throw new NotImplementedException("CMapGroup_GetParentWorld is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetRootDocument")]
        public static int CMapGroup_GetRootDocument( void* self, long nDocumentLoaded )
        {
            throw new NotImplementedException("CMapGroup_GetRootDocument is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetDescription")]
        public static void* CMapGroup_GetDescription( void* self )
        {
            throw new NotImplementedException("CMapGroup_GetDescription is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetTypeString")]
        public static void* CMapGroup_GetTypeString( void* self )
        {
            throw new NotImplementedException("CMapGroup_GetTypeString is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_Copy")]
        public static int CMapGroup_Copy( void* self )
        {
            throw new NotImplementedException("CMapGroup_Copy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_IsVisible")]
        public static int CMapGroup_IsVisible( void* self )
        {
            throw new NotImplementedException("CMapGroup_IsVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetVisible")]
        public static void* CMapGroup_SetVisible( void* self, int bVisible )
        {
            throw new NotImplementedException("CMapGroup_SetVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_IsSelected")]
        public static int CMapGroup_IsSelected( void* self )
        {
            throw new NotImplementedException("CMapGroup_IsSelected is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetChildCount")]
        public static int CMapGroup_GetChildCount( void* self )
        {
            throw new NotImplementedException("CMapGroup_GetChildCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetChild")]
        public static int CMapGroup_GetChild( void* self, int i )
        {
            throw new NotImplementedException("CMapGroup_GetChild is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetFirstDescendent")]
        public static int CMapGroup_GetFirstDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapGroup_GetFirstDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetNextDescendent")]
        public static int CMapGroup_GetNextDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapGroup_GetNextDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_AsMapEntity")]
        public static int CMapGroup_AsMapEntity( void* self )
        {
            throw new NotImplementedException("CMapGroup_AsMapEntity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetOrigin")]
        public static void* CMapGroup_GetOrigin( void* self )
        {
            throw new NotImplementedException("CMapGroup_GetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetAngles")]
        public static void* CMapGroup_GetAngles( void* self )
        {
            throw new NotImplementedException("CMapGroup_GetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetScales")]
        public static void* CMapGroup_GetScales( void* self )
        {
            throw new NotImplementedException("CMapGroup_GetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetOrigin")]
        public static void* CMapGroup_SetOrigin( void* self, void* origin )
        {
            throw new NotImplementedException("CMapGroup_SetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetAngles")]
        public static void* CMapGroup_SetAngles( void* self, void* angles )
        {
            throw new NotImplementedException("CMapGroup_SetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetScales")]
        public static void* CMapGroup_SetScales( void* self, void* scales )
        {
            throw new NotImplementedException("CMapGroup_SetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_BeginTransformOperation")]
        public static void* CMapGroup_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            throw new NotImplementedException("CMapGroup_BeginTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_Transform")]
        public static void* CMapGroup_Transform( void* self, void* matrix, long nFlags )
        {
            throw new NotImplementedException("CMapGroup_Transform is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_EndTransformOperation")]
        public static void* CMapGroup_EndTransformOperation( void* self )
        {
            throw new NotImplementedException("CMapGroup_EndTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_MarkBoundsDirty")]
        public static void* CMapGroup_MarkBoundsDirty( void* self )
        {
            throw new NotImplementedException("CMapGroup_MarkBoundsDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_FullBoundsUpdate")]
        public static void* CMapGroup_FullBoundsUpdate( void* self )
        {
            throw new NotImplementedException("CMapGroup_FullBoundsUpdate is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetModifiedFlag")]
        public static void* CMapGroup_SetModifiedFlag( void* self )
        {
            throw new NotImplementedException("CMapGroup_SetModifiedFlag is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GeneratesEntityModelGeometry")]
        public static int CMapGroup_GeneratesEntityModelGeometry( void* self )
        {
            throw new NotImplementedException("CMapGroup_GeneratesEntityModelGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_DescriptionChanged")]
        public static void* CMapGroup_DescriptionChanged( void* self )
        {
            throw new NotImplementedException("CMapGroup_DescriptionChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_CoreAttributeChanged")]
        public static void* CMapGroup_CoreAttributeChanged( void* self )
        {
            throw new NotImplementedException("CMapGroup_CoreAttributeChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapInstance", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapNode_To_CMapInstance( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapInstance", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapNode_From_CMapInstance( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetTarget")]
        public static void* CMapInstance_SetTarget( void* self, void* node )
        {
            throw new NotImplementedException("CMapInstance_SetTarget is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetTarget")]
        public static int CMapInstance_GetTarget( void* self )
        {
            throw new NotImplementedException("CMapInstance_GetTarget is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetNodeID")]
        public static int CMapInstance_GetNodeID( void* self )
        {
            throw new NotImplementedException("CMapInstance_GetNodeID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetName")]
        public static void* CMapInstance_GetName( void* self )
        {
            throw new NotImplementedException("CMapInstance_GetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetName")]
        public static void* CMapInstance_SetName( void* self, void* name )
        {
            throw new NotImplementedException("CMapInstance_SetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetParent")]
        public static int CMapInstance_GetParent( void* self )
        {
            throw new NotImplementedException("CMapInstance_GetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetParent")]
        public static void* CMapInstance_SetParent( void* self, void* parent )
        {
            throw new NotImplementedException("CMapInstance_SetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetParentWorld")]
        public static int CMapInstance_GetParentWorld( void* self )
        {
            throw new NotImplementedException("CMapInstance_GetParentWorld is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetRootDocument")]
        public static int CMapInstance_GetRootDocument( void* self, long nDocumentLoaded )
        {
            throw new NotImplementedException("CMapInstance_GetRootDocument is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetDescription")]
        public static void* CMapInstance_GetDescription( void* self )
        {
            throw new NotImplementedException("CMapInstance_GetDescription is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetTypeString")]
        public static void* CMapInstance_GetTypeString( void* self )
        {
            throw new NotImplementedException("CMapInstance_GetTypeString is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_Copy")]
        public static int CMapInstance_Copy( void* self )
        {
            throw new NotImplementedException("CMapInstance_Copy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_IsVisible")]
        public static int CMapInstance_IsVisible( void* self )
        {
            throw new NotImplementedException("CMapInstance_IsVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetVisible")]
        public static void* CMapInstance_SetVisible( void* self, int bVisible )
        {
            throw new NotImplementedException("CMapInstance_SetVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_IsSelected")]
        public static int CMapInstance_IsSelected( void* self )
        {
            throw new NotImplementedException("CMapInstance_IsSelected is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetChildCount")]
        public static int CMapInstance_GetChildCount( void* self )
        {
            throw new NotImplementedException("CMapInstance_GetChildCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetChild")]
        public static int CMapInstance_GetChild( void* self, int i )
        {
            throw new NotImplementedException("CMapInstance_GetChild is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetFirstDescendent")]
        public static int CMapInstance_GetFirstDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapInstance_GetFirstDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetNextDescendent")]
        public static int CMapInstance_GetNextDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapInstance_GetNextDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_AsMapEntity")]
        public static int CMapInstance_AsMapEntity( void* self )
        {
            throw new NotImplementedException("CMapInstance_AsMapEntity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetOrigin")]
        public static void* CMapInstance_GetOrigin( void* self )
        {
            throw new NotImplementedException("CMapInstance_GetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetAngles")]
        public static void* CMapInstance_GetAngles( void* self )
        {
            throw new NotImplementedException("CMapInstance_GetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetScales")]
        public static void* CMapInstance_GetScales( void* self )
        {
            throw new NotImplementedException("CMapInstance_GetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetOrigin")]
        public static void* CMapInstance_SetOrigin( void* self, void* origin )
        {
            throw new NotImplementedException("CMapInstance_SetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetAngles")]
        public static void* CMapInstance_SetAngles( void* self, void* angles )
        {
            throw new NotImplementedException("CMapInstance_SetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetScales")]
        public static void* CMapInstance_SetScales( void* self, void* scales )
        {
            throw new NotImplementedException("CMapInstance_SetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_BeginTransformOperation")]
        public static void* CMapInstance_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            throw new NotImplementedException("CMapInstance_BeginTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_Transform")]
        public static void* CMapInstance_Transform( void* self, void* matrix, long nFlags )
        {
            throw new NotImplementedException("CMapInstance_Transform is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_EndTransformOperation")]
        public static void* CMapInstance_EndTransformOperation( void* self )
        {
            throw new NotImplementedException("CMapInstance_EndTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_MarkBoundsDirty")]
        public static void* CMapInstance_MarkBoundsDirty( void* self )
        {
            throw new NotImplementedException("CMapInstance_MarkBoundsDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_FullBoundsUpdate")]
        public static void* CMapInstance_FullBoundsUpdate( void* self )
        {
            throw new NotImplementedException("CMapInstance_FullBoundsUpdate is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetModifiedFlag")]
        public static void* CMapInstance_SetModifiedFlag( void* self )
        {
            throw new NotImplementedException("CMapInstance_SetModifiedFlag is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GeneratesEntityModelGeometry")]
        public static int CMapInstance_GeneratesEntityModelGeometry( void* self )
        {
            throw new NotImplementedException("CMapInstance_GeneratesEntityModelGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_DescriptionChanged")]
        public static void* CMapInstance_DescriptionChanged( void* self )
        {
            throw new NotImplementedException("CMapInstance_DescriptionChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_CoreAttributeChanged")]
        public static void* CMapInstance_CoreAttributeChanged( void* self )
        {
            throw new NotImplementedException("CMapInstance_CoreAttributeChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapMesh", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapNode_To_CMapMesh( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapMesh", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapNode_From_CMapMesh( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_AssignMaterialToMesh")]
        public static void* CMapMesh_AssignMaterialToMesh( void* self, void* materialName )
        {
            throw new NotImplementedException("CMapMesh_AssignMaterialToMesh is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_ConstructFromData")]
        public static void* CMapMesh_ConstructFromData( void* self, int numVerticies, void* vertexPositions, void* vertexTexCoords, int numIndices, void* vertexIndices, int numFaces, void* faceVertexCounts, void* faceMaterialsPtr, int mergeVertices, float vertexMergeTolerance )
        {
            throw new NotImplementedException("CMapMesh_ConstructFromData is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetFaceMaterials")]
        public static void* CMapMesh_GetFaceMaterials( void* self, void* materials )
        {
            throw new NotImplementedException("CMapMesh_GetFaceMaterials is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetNodeID")]
        public static int CMapMesh_GetNodeID( void* self )
        {
            throw new NotImplementedException("CMapMesh_GetNodeID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetName")]
        public static void* CMapMesh_GetName( void* self )
        {
            throw new NotImplementedException("CMapMesh_GetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetName")]
        public static void* CMapMesh_SetName( void* self, void* name )
        {
            throw new NotImplementedException("CMapMesh_SetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetParent")]
        public static int CMapMesh_GetParent( void* self )
        {
            throw new NotImplementedException("CMapMesh_GetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetParent")]
        public static void* CMapMesh_SetParent( void* self, void* parent )
        {
            throw new NotImplementedException("CMapMesh_SetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetParentWorld")]
        public static int CMapMesh_GetParentWorld( void* self )
        {
            throw new NotImplementedException("CMapMesh_GetParentWorld is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetRootDocument")]
        public static int CMapMesh_GetRootDocument( void* self, long nDocumentLoaded )
        {
            throw new NotImplementedException("CMapMesh_GetRootDocument is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetDescription")]
        public static void* CMapMesh_GetDescription( void* self )
        {
            throw new NotImplementedException("CMapMesh_GetDescription is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetTypeString")]
        public static void* CMapMesh_GetTypeString( void* self )
        {
            throw new NotImplementedException("CMapMesh_GetTypeString is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_Copy")]
        public static int CMapMesh_Copy( void* self )
        {
            throw new NotImplementedException("CMapMesh_Copy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_IsVisible")]
        public static int CMapMesh_IsVisible( void* self )
        {
            throw new NotImplementedException("CMapMesh_IsVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetVisible")]
        public static void* CMapMesh_SetVisible( void* self, int bVisible )
        {
            throw new NotImplementedException("CMapMesh_SetVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_IsSelected")]
        public static int CMapMesh_IsSelected( void* self )
        {
            throw new NotImplementedException("CMapMesh_IsSelected is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetChildCount")]
        public static int CMapMesh_GetChildCount( void* self )
        {
            throw new NotImplementedException("CMapMesh_GetChildCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetChild")]
        public static int CMapMesh_GetChild( void* self, int i )
        {
            throw new NotImplementedException("CMapMesh_GetChild is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetFirstDescendent")]
        public static int CMapMesh_GetFirstDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapMesh_GetFirstDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetNextDescendent")]
        public static int CMapMesh_GetNextDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapMesh_GetNextDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_AsMapEntity")]
        public static int CMapMesh_AsMapEntity( void* self )
        {
            throw new NotImplementedException("CMapMesh_AsMapEntity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetOrigin")]
        public static void* CMapMesh_GetOrigin( void* self )
        {
            throw new NotImplementedException("CMapMesh_GetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetAngles")]
        public static void* CMapMesh_GetAngles( void* self )
        {
            throw new NotImplementedException("CMapMesh_GetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetScales")]
        public static void* CMapMesh_GetScales( void* self )
        {
            throw new NotImplementedException("CMapMesh_GetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetOrigin")]
        public static void* CMapMesh_SetOrigin( void* self, void* origin )
        {
            throw new NotImplementedException("CMapMesh_SetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetAngles")]
        public static void* CMapMesh_SetAngles( void* self, void* angles )
        {
            throw new NotImplementedException("CMapMesh_SetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetScales")]
        public static void* CMapMesh_SetScales( void* self, void* scales )
        {
            throw new NotImplementedException("CMapMesh_SetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_BeginTransformOperation")]
        public static void* CMapMesh_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            throw new NotImplementedException("CMapMesh_BeginTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_Transform")]
        public static void* CMapMesh_Transform( void* self, void* matrix, long nFlags )
        {
            throw new NotImplementedException("CMapMesh_Transform is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_EndTransformOperation")]
        public static void* CMapMesh_EndTransformOperation( void* self )
        {
            throw new NotImplementedException("CMapMesh_EndTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_MarkBoundsDirty")]
        public static void* CMapMesh_MarkBoundsDirty( void* self )
        {
            throw new NotImplementedException("CMapMesh_MarkBoundsDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_FullBoundsUpdate")]
        public static void* CMapMesh_FullBoundsUpdate( void* self )
        {
            throw new NotImplementedException("CMapMesh_FullBoundsUpdate is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetModifiedFlag")]
        public static void* CMapMesh_SetModifiedFlag( void* self )
        {
            throw new NotImplementedException("CMapMesh_SetModifiedFlag is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GeneratesEntityModelGeometry")]
        public static int CMapMesh_GeneratesEntityModelGeometry( void* self )
        {
            throw new NotImplementedException("CMapMesh_GeneratesEntityModelGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_DescriptionChanged")]
        public static void* CMapMesh_DescriptionChanged( void* self )
        {
            throw new NotImplementedException("CMapMesh_DescriptionChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_CoreAttributeChanged")]
        public static void* CMapMesh_CoreAttributeChanged( void* self )
        {
            throw new NotImplementedException("CMapMesh_CoreAttributeChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetNodeID")]
        public static int CMapNode_GetNodeID( void* self )
        {
            throw new NotImplementedException("CMapNode_GetNodeID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetName")]
        public static void* CMapNode_GetName( void* self )
        {
            throw new NotImplementedException("CMapNode_GetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetName")]
        public static void* CMapNode_SetName( void* self, void* name )
        {
            throw new NotImplementedException("CMapNode_SetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetParent")]
        public static int CMapNode_GetParent( void* self )
        {
            throw new NotImplementedException("CMapNode_GetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetParent")]
        public static void* CMapNode_SetParent( void* self, void* parent )
        {
            throw new NotImplementedException("CMapNode_SetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetParentWorld")]
        public static int CMapNode_GetParentWorld( void* self )
        {
            throw new NotImplementedException("CMapNode_GetParentWorld is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetRootDocument")]
        public static int CMapNode_GetRootDocument( void* self, long nDocumentLoaded )
        {
            throw new NotImplementedException("CMapNode_GetRootDocument is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetDescription")]
        public static void* CMapNode_GetDescription( void* self )
        {
            throw new NotImplementedException("CMapNode_GetDescription is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetTypeString")]
        public static void* CMapNode_GetTypeString( void* self )
        {
            throw new NotImplementedException("CMapNode_GetTypeString is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_Copy")]
        public static int CMapNode_Copy( void* self )
        {
            throw new NotImplementedException("CMapNode_Copy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_IsVisible")]
        public static int CMapNode_IsVisible( void* self )
        {
            throw new NotImplementedException("CMapNode_IsVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetVisible")]
        public static void* CMapNode_SetVisible( void* self, int bVisible )
        {
            throw new NotImplementedException("CMapNode_SetVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_IsSelected")]
        public static int CMapNode_IsSelected( void* self )
        {
            throw new NotImplementedException("CMapNode_IsSelected is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetChildCount")]
        public static int CMapNode_GetChildCount( void* self )
        {
            throw new NotImplementedException("CMapNode_GetChildCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetChild")]
        public static int CMapNode_GetChild( void* self, int i )
        {
            throw new NotImplementedException("CMapNode_GetChild is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetFirstDescendent")]
        public static int CMapNode_GetFirstDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapNode_GetFirstDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetNextDescendent")]
        public static int CMapNode_GetNextDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapNode_GetNextDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_AsMapEntity")]
        public static int CMapNode_AsMapEntity( void* self )
        {
            throw new NotImplementedException("CMapNode_AsMapEntity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetOrigin")]
        public static void* CMapNode_GetOrigin( void* self )
        {
            throw new NotImplementedException("CMapNode_GetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetAngles")]
        public static void* CMapNode_GetAngles( void* self )
        {
            throw new NotImplementedException("CMapNode_GetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetScales")]
        public static void* CMapNode_GetScales( void* self )
        {
            throw new NotImplementedException("CMapNode_GetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetOrigin")]
        public static void* CMapNode_SetOrigin( void* self, void* origin )
        {
            throw new NotImplementedException("CMapNode_SetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetAngles")]
        public static void* CMapNode_SetAngles( void* self, void* angles )
        {
            throw new NotImplementedException("CMapNode_SetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetScales")]
        public static void* CMapNode_SetScales( void* self, void* scales )
        {
            throw new NotImplementedException("CMapNode_SetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_BeginTransformOperation")]
        public static void* CMapNode_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            throw new NotImplementedException("CMapNode_BeginTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_Transform")]
        public static void* CMapNode_Transform( void* self, void* matrix, long nFlags )
        {
            throw new NotImplementedException("CMapNode_Transform is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_EndTransformOperation")]
        public static void* CMapNode_EndTransformOperation( void* self )
        {
            throw new NotImplementedException("CMapNode_EndTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_MarkBoundsDirty")]
        public static void* CMapNode_MarkBoundsDirty( void* self )
        {
            throw new NotImplementedException("CMapNode_MarkBoundsDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_FullBoundsUpdate")]
        public static void* CMapNode_FullBoundsUpdate( void* self )
        {
            throw new NotImplementedException("CMapNode_FullBoundsUpdate is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetModifiedFlag")]
        public static void* CMapNode_SetModifiedFlag( void* self )
        {
            throw new NotImplementedException("CMapNode_SetModifiedFlag is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GeneratesEntityModelGeometry")]
        public static int CMapNode_GeneratesEntityModelGeometry( void* self )
        {
            throw new NotImplementedException("CMapNode_GeneratesEntityModelGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_DescriptionChanged")]
        public static void* CMapNode_DescriptionChanged( void* self )
        {
            throw new NotImplementedException("CMapNode_DescriptionChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_CoreAttributeChanged")]
        public static void* CMapNode_CoreAttributeChanged( void* self )
        {
            throw new NotImplementedException("CMapNode_CoreAttributeChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapEntity_To_CMapPath", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapEntity_To_CMapPath( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapEntity_From_CMapPath", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapEntity_From_CMapPath( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapPath", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapNode_To_CMapPath( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapPath", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapNode_From_CMapPath( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_AddNewNodeToPath")]
        public static int CMapPath_AddNewNodeToPath( void* self )
        {
            throw new NotImplementedException("CMapPath_AddNewNodeToPath is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetClassName")]
        public static void* CMapPath_GetClassName( void* self )
        {
            throw new NotImplementedException("CMapPath_GetClassName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetClass")]
        public static void* CMapPath_SetClass( void* self, void* classname )
        {
            throw new NotImplementedException("CMapPath_SetClass is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetKeyValue")]
        public static void* CMapPath_SetKeyValue( void* self, void* key, void* value )
        {
            throw new NotImplementedException("CMapPath_SetKeyValue is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetKeyValue")]
        public static void* CMapPath_GetKeyValue( void* self, void* key )
        {
            throw new NotImplementedException("CMapPath_GetKeyValue is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_TargetNameMatches")]
        public static int CMapPath_TargetNameMatches( void* self, void* name )
        {
            throw new NotImplementedException("CMapPath_TargetNameMatches is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetDefaultBounds")]
        public static void* CMapPath_SetDefaultBounds( void* self, void* minBounds, void* maxBounds )
        {
            throw new NotImplementedException("CMapPath_SetDefaultBounds is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetNodeID")]
        public static int CMapPath_GetNodeID( void* self )
        {
            throw new NotImplementedException("CMapPath_GetNodeID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetName")]
        public static void* CMapPath_GetName( void* self )
        {
            throw new NotImplementedException("CMapPath_GetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetName")]
        public static void* CMapPath_SetName( void* self, void* name )
        {
            throw new NotImplementedException("CMapPath_SetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetParent")]
        public static int CMapPath_GetParent( void* self )
        {
            throw new NotImplementedException("CMapPath_GetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetParent")]
        public static void* CMapPath_SetParent( void* self, void* parent )
        {
            throw new NotImplementedException("CMapPath_SetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetParentWorld")]
        public static int CMapPath_GetParentWorld( void* self )
        {
            throw new NotImplementedException("CMapPath_GetParentWorld is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetRootDocument")]
        public static int CMapPath_GetRootDocument( void* self, long nDocumentLoaded )
        {
            throw new NotImplementedException("CMapPath_GetRootDocument is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetDescription")]
        public static void* CMapPath_GetDescription( void* self )
        {
            throw new NotImplementedException("CMapPath_GetDescription is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetTypeString")]
        public static void* CMapPath_GetTypeString( void* self )
        {
            throw new NotImplementedException("CMapPath_GetTypeString is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_Copy")]
        public static int CMapPath_Copy( void* self )
        {
            throw new NotImplementedException("CMapPath_Copy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_IsVisible")]
        public static int CMapPath_IsVisible( void* self )
        {
            throw new NotImplementedException("CMapPath_IsVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetVisible")]
        public static void* CMapPath_SetVisible( void* self, int bVisible )
        {
            throw new NotImplementedException("CMapPath_SetVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_IsSelected")]
        public static int CMapPath_IsSelected( void* self )
        {
            throw new NotImplementedException("CMapPath_IsSelected is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetChildCount")]
        public static int CMapPath_GetChildCount( void* self )
        {
            throw new NotImplementedException("CMapPath_GetChildCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetChild")]
        public static int CMapPath_GetChild( void* self, int i )
        {
            throw new NotImplementedException("CMapPath_GetChild is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetFirstDescendent")]
        public static int CMapPath_GetFirstDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapPath_GetFirstDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetNextDescendent")]
        public static int CMapPath_GetNextDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapPath_GetNextDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_AsMapEntity")]
        public static int CMapPath_AsMapEntity( void* self )
        {
            throw new NotImplementedException("CMapPath_AsMapEntity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetOrigin")]
        public static void* CMapPath_GetOrigin( void* self )
        {
            throw new NotImplementedException("CMapPath_GetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetAngles")]
        public static void* CMapPath_GetAngles( void* self )
        {
            throw new NotImplementedException("CMapPath_GetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetScales")]
        public static void* CMapPath_GetScales( void* self )
        {
            throw new NotImplementedException("CMapPath_GetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetOrigin")]
        public static void* CMapPath_SetOrigin( void* self, void* origin )
        {
            throw new NotImplementedException("CMapPath_SetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetAngles")]
        public static void* CMapPath_SetAngles( void* self, void* angles )
        {
            throw new NotImplementedException("CMapPath_SetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetScales")]
        public static void* CMapPath_SetScales( void* self, void* scales )
        {
            throw new NotImplementedException("CMapPath_SetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_BeginTransformOperation")]
        public static void* CMapPath_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            throw new NotImplementedException("CMapPath_BeginTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_Transform")]
        public static void* CMapPath_Transform( void* self, void* matrix, long nFlags )
        {
            throw new NotImplementedException("CMapPath_Transform is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_EndTransformOperation")]
        public static void* CMapPath_EndTransformOperation( void* self )
        {
            throw new NotImplementedException("CMapPath_EndTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_MarkBoundsDirty")]
        public static void* CMapPath_MarkBoundsDirty( void* self )
        {
            throw new NotImplementedException("CMapPath_MarkBoundsDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_FullBoundsUpdate")]
        public static void* CMapPath_FullBoundsUpdate( void* self )
        {
            throw new NotImplementedException("CMapPath_FullBoundsUpdate is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetModifiedFlag")]
        public static void* CMapPath_SetModifiedFlag( void* self )
        {
            throw new NotImplementedException("CMapPath_SetModifiedFlag is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GeneratesEntityModelGeometry")]
        public static int CMapPath_GeneratesEntityModelGeometry( void* self )
        {
            throw new NotImplementedException("CMapPath_GeneratesEntityModelGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_DescriptionChanged")]
        public static void* CMapPath_DescriptionChanged( void* self )
        {
            throw new NotImplementedException("CMapPath_DescriptionChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_CoreAttributeChanged")]
        public static void* CMapPath_CoreAttributeChanged( void* self )
        {
            throw new NotImplementedException("CMapPath_CoreAttributeChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapEntity_To_CMapPathNode", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapEntity_To_CMapPathNode( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapEntity_From_CMapPathNode", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapEntity_From_CMapPathNode( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapPathNode", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapNode_To_CMapPathNode( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapPathNode", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapNode_From_CMapPathNode( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetClassName")]
        public static void* CMapPathNode_GetClassName( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GetClassName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetClass")]
        public static void* CMapPathNode_SetClass( void* self, void* classname )
        {
            throw new NotImplementedException("CMapPathNode_SetClass is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetKeyValue")]
        public static void* CMapPathNode_SetKeyValue( void* self, void* key, void* value )
        {
            throw new NotImplementedException("CMapPathNode_SetKeyValue is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetKeyValue")]
        public static void* CMapPathNode_GetKeyValue( void* self, void* key )
        {
            throw new NotImplementedException("CMapPathNode_GetKeyValue is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_TargetNameMatches")]
        public static int CMapPathNode_TargetNameMatches( void* self, void* name )
        {
            throw new NotImplementedException("CMapPathNode_TargetNameMatches is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetDefaultBounds")]
        public static void* CMapPathNode_SetDefaultBounds( void* self, void* minBounds, void* maxBounds )
        {
            throw new NotImplementedException("CMapPathNode_SetDefaultBounds is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetNodeID")]
        public static int CMapPathNode_GetNodeID( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GetNodeID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetName")]
        public static void* CMapPathNode_GetName( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetName")]
        public static void* CMapPathNode_SetName( void* self, void* name )
        {
            throw new NotImplementedException("CMapPathNode_SetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetParent")]
        public static int CMapPathNode_GetParent( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetParent")]
        public static void* CMapPathNode_SetParent( void* self, void* parent )
        {
            throw new NotImplementedException("CMapPathNode_SetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetParentWorld")]
        public static int CMapPathNode_GetParentWorld( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GetParentWorld is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetRootDocument")]
        public static int CMapPathNode_GetRootDocument( void* self, long nDocumentLoaded )
        {
            throw new NotImplementedException("CMapPathNode_GetRootDocument is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetDescription")]
        public static void* CMapPathNode_GetDescription( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GetDescription is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetTypeString")]
        public static void* CMapPathNode_GetTypeString( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GetTypeString is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_Copy")]
        public static int CMapPathNode_Copy( void* self )
        {
            throw new NotImplementedException("CMapPathNode_Copy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_IsVisible")]
        public static int CMapPathNode_IsVisible( void* self )
        {
            throw new NotImplementedException("CMapPathNode_IsVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetVisible")]
        public static void* CMapPathNode_SetVisible( void* self, int bVisible )
        {
            throw new NotImplementedException("CMapPathNode_SetVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_IsSelected")]
        public static int CMapPathNode_IsSelected( void* self )
        {
            throw new NotImplementedException("CMapPathNode_IsSelected is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetChildCount")]
        public static int CMapPathNode_GetChildCount( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GetChildCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetChild")]
        public static int CMapPathNode_GetChild( void* self, int i )
        {
            throw new NotImplementedException("CMapPathNode_GetChild is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetFirstDescendent")]
        public static int CMapPathNode_GetFirstDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapPathNode_GetFirstDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetNextDescendent")]
        public static int CMapPathNode_GetNextDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapPathNode_GetNextDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_AsMapEntity")]
        public static int CMapPathNode_AsMapEntity( void* self )
        {
            throw new NotImplementedException("CMapPathNode_AsMapEntity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetOrigin")]
        public static void* CMapPathNode_GetOrigin( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetAngles")]
        public static void* CMapPathNode_GetAngles( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetScales")]
        public static void* CMapPathNode_GetScales( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetOrigin")]
        public static void* CMapPathNode_SetOrigin( void* self, void* origin )
        {
            throw new NotImplementedException("CMapPathNode_SetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetAngles")]
        public static void* CMapPathNode_SetAngles( void* self, void* angles )
        {
            throw new NotImplementedException("CMapPathNode_SetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetScales")]
        public static void* CMapPathNode_SetScales( void* self, void* scales )
        {
            throw new NotImplementedException("CMapPathNode_SetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_BeginTransformOperation")]
        public static void* CMapPathNode_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            throw new NotImplementedException("CMapPathNode_BeginTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_Transform")]
        public static void* CMapPathNode_Transform( void* self, void* matrix, long nFlags )
        {
            throw new NotImplementedException("CMapPathNode_Transform is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_EndTransformOperation")]
        public static void* CMapPathNode_EndTransformOperation( void* self )
        {
            throw new NotImplementedException("CMapPathNode_EndTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_MarkBoundsDirty")]
        public static void* CMapPathNode_MarkBoundsDirty( void* self )
        {
            throw new NotImplementedException("CMapPathNode_MarkBoundsDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_FullBoundsUpdate")]
        public static void* CMapPathNode_FullBoundsUpdate( void* self )
        {
            throw new NotImplementedException("CMapPathNode_FullBoundsUpdate is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetModifiedFlag")]
        public static void* CMapPathNode_SetModifiedFlag( void* self )
        {
            throw new NotImplementedException("CMapPathNode_SetModifiedFlag is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GeneratesEntityModelGeometry")]
        public static int CMapPathNode_GeneratesEntityModelGeometry( void* self )
        {
            throw new NotImplementedException("CMapPathNode_GeneratesEntityModelGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_DescriptionChanged")]
        public static void* CMapPathNode_DescriptionChanged( void* self )
        {
            throw new NotImplementedException("CMapPathNode_DescriptionChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_CoreAttributeChanged")]
        public static void* CMapPathNode_CoreAttributeChanged( void* self )
        {
            throw new NotImplementedException("CMapPathNode_CoreAttributeChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapMesh_To_CMapStaticOverlay", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapMesh_To_CMapStaticOverlay( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapMesh_From_CMapStaticOverlay", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapMesh_From_CMapStaticOverlay( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapStaticOverlay", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapNode_To_CMapStaticOverlay( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapStaticOverlay", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapNode_From_CMapStaticOverlay( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_CreateCenteredQuad")]
        public static void* CMpSttcvrly_CreateCenteredQuad( void* self, float flWidth, float flHeight, void* pMaterialName )
        {
            throw new NotImplementedException("CMpSttcvrly_CreateCenteredQuad is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_AssignMaterialToMesh")]
        public static void* CMpSttcvrly_AssignMaterialToMesh( void* self, void* materialName )
        {
            throw new NotImplementedException("CMpSttcvrly_AssignMaterialToMesh is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_ConstructFromData")]
        public static void* CMpSttcvrly_ConstructFromData( void* self, int numVerticies, void* vertexPositions, void* vertexTexCoords, int numIndices, void* vertexIndices, int numFaces, void* faceVertexCounts, void* faceMaterialsPtr, int mergeVertices, float vertexMergeTolerance )
        {
            throw new NotImplementedException("CMpSttcvrly_ConstructFromData is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetFaceMaterials")]
        public static void* CMpSttcvrly_GetFaceMaterials( void* self, void* materials )
        {
            throw new NotImplementedException("CMpSttcvrly_GetFaceMaterials is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetNodeID")]
        public static int CMpSttcvrly_GetNodeID( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_GetNodeID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetName")]
        public static void* CMpSttcvrly_GetName( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_GetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetName")]
        public static void* CMpSttcvrly_SetName( void* self, void* name )
        {
            throw new NotImplementedException("CMpSttcvrly_SetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetParent")]
        public static int CMpSttcvrly_GetParent( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_GetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetParent")]
        public static void* CMpSttcvrly_SetParent( void* self, void* parent )
        {
            throw new NotImplementedException("CMpSttcvrly_SetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetParentWorld")]
        public static int CMpSttcvrly_GetParentWorld( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_GetParentWorld is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetRootDocument")]
        public static int CMpSttcvrly_GetRootDocument( void* self, long nDocumentLoaded )
        {
            throw new NotImplementedException("CMpSttcvrly_GetRootDocument is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetDescription")]
        public static void* CMpSttcvrly_GetDescription( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_GetDescription is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetTypeString")]
        public static void* CMpSttcvrly_GetTypeString( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_GetTypeString is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_Copy")]
        public static int CMpSttcvrly_Copy( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_Copy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_IsVisible")]
        public static int CMpSttcvrly_IsVisible( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_IsVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetVisible")]
        public static void* CMpSttcvrly_SetVisible( void* self, int bVisible )
        {
            throw new NotImplementedException("CMpSttcvrly_SetVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_IsSelected")]
        public static int CMpSttcvrly_IsSelected( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_IsSelected is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetChildCount")]
        public static int CMpSttcvrly_GetChildCount( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_GetChildCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetChild")]
        public static int CMpSttcvrly_GetChild( void* self, int i )
        {
            throw new NotImplementedException("CMpSttcvrly_GetChild is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetFirstDescendent")]
        public static int CMpSttcvrly_GetFirstDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMpSttcvrly_GetFirstDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetNextDescendent")]
        public static int CMpSttcvrly_GetNextDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMpSttcvrly_GetNextDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_AsMapEntity")]
        public static int CMpSttcvrly_AsMapEntity( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_AsMapEntity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetOrigin")]
        public static void* CMpSttcvrly_GetOrigin( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_GetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetAngles")]
        public static void* CMpSttcvrly_GetAngles( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_GetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetScales")]
        public static void* CMpSttcvrly_GetScales( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_GetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetOrigin")]
        public static void* CMpSttcvrly_SetOrigin( void* self, void* origin )
        {
            throw new NotImplementedException("CMpSttcvrly_SetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetAngles")]
        public static void* CMpSttcvrly_SetAngles( void* self, void* angles )
        {
            throw new NotImplementedException("CMpSttcvrly_SetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetScales")]
        public static void* CMpSttcvrly_SetScales( void* self, void* scales )
        {
            throw new NotImplementedException("CMpSttcvrly_SetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_BeginTransformOperation")]
        public static void* CMpSttcvrly_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            throw new NotImplementedException("CMpSttcvrly_BeginTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_Transform")]
        public static void* CMpSttcvrly_Transform( void* self, void* matrix, long nFlags )
        {
            throw new NotImplementedException("CMpSttcvrly_Transform is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_EndTransformOperation")]
        public static void* CMpSttcvrly_EndTransformOperation( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_EndTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_MarkBoundsDirty")]
        public static void* CMpSttcvrly_MarkBoundsDirty( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_MarkBoundsDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_FullBoundsUpdate")]
        public static void* CMpSttcvrly_FullBoundsUpdate( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_FullBoundsUpdate is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetModifiedFlag")]
        public static void* CMpSttcvrly_SetModifiedFlag( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_SetModifiedFlag is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GeneratesEntityModelGeometry")]
        public static int CMpSttcvrly_GeneratesEntityModelGeometry( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_GeneratesEntityModelGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_DescriptionChanged")]
        public static void* CMpSttcvrly_DescriptionChanged( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_DescriptionChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_CoreAttributeChanged")]
        public static void* CMpSttcvrly_CoreAttributeChanged( void* self )
        {
            throw new NotImplementedException("CMpSttcvrly_CoreAttributeChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_GetMapDoc")]
        public static int CMapView_GetMapDoc( void* self )
        {
            throw new NotImplementedException("CMapView_GetMapDoc is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_MarkHudDirty")]
        public static void* CMapView_MarkHudDirty( void* self )
        {
            throw new NotImplementedException("CMapView_MarkHudDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_GetCamera")]
        public static void* CMapView_GetCamera( void* self )
        {
            throw new NotImplementedException("CMapView_GetCamera is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_IsActive")]
        public static int CMapView_IsActive( void* self )
        {
            throw new NotImplementedException("CMapView_IsActive is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_GetMousePosition")]
        public static void* CMapView_GetMousePosition( void* self, void* pos )
        {
            throw new NotImplementedException("CMapView_GetMousePosition is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_EnterFreeDragMode")]
        public static void* CMapView_EnterFreeDragMode( void* self, void* vClientPos, void* pObjectToSelect, void* vecInitialHitNormal, int bAlignToSurface )
        {
            throw new NotImplementedException("CMapView_EnterFreeDragMode is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_UpdateFreeDragMode")]
        public static void* CMapView_UpdateFreeDragMode( void* self, void* vClientPos, int bModifyAlignToSurface )
        {
            throw new NotImplementedException("CMapView_UpdateFreeDragMode is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_ExitFreeDragMode")]
        public static void* CMapView_ExitFreeDragMode( void* self, int bRestoreToolState )
        {
            throw new NotImplementedException("CMapView_ExitFreeDragMode is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_GetDropTarget")]
        public static void* CMapView_GetDropTarget( void* self, void* pVecNormalOut, void* pVecDropPointOut, void* vecPoint2D )
        {
            throw new NotImplementedException("CMapView_GetDropTarget is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_UpdateManagedGizmoState")]
        public static void* CMapView_UpdateManagedGizmoState( void* self, int isHoveredOverSomething, float distanceFromCamera )
        {
            throw new NotImplementedException("CMapView_UpdateManagedGizmoState is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_GetManipulationMode")]
        public static long CMapView_GetManipulationMode( void* self )
        {
            throw new NotImplementedException("CMapView_GetManipulationMode is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_HitDistanceAtMouse")]
        public static float CMapView_HitDistanceAtMouse( void* self )
        {
            throw new NotImplementedException("CMapView_HitDistanceAtMouse is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapWorld", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CMapNode_To_CMapWorld( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapWorld", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CMapNode_From_CMapWorld( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetSerializedScene")]
        public static void* CMapWorld_SetSerializedScene( void* self, void* json )
        {
            throw new NotImplementedException("CMapWorld_SetSerializedScene is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetSerializedScene")]
        public static void* CMapWorld_GetSerializedScene( void* self )
        {
            throw new NotImplementedException("CMapWorld_GetSerializedScene is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_FindNodeByID")]
        public static int CMapWorld_FindNodeByID( void* self, int nodeId )
        {
            throw new NotImplementedException("CMapWorld_FindNodeByID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_FindEntityByNodeId")]
        public static int CMapWorld_FindEntityByNodeId( void* self, int nodeId )
        {
            throw new NotImplementedException("CMapWorld_FindEntityByNodeId is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_Trace")]
        public static void* CMapWorld_Trace( void* self, void* traceRequest )
        {
            throw new NotImplementedException("CMapWorld_Trace is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetNodeID")]
        public static int CMapWorld_GetNodeID( void* self )
        {
            throw new NotImplementedException("CMapWorld_GetNodeID is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetName")]
        public static void* CMapWorld_GetName( void* self )
        {
            throw new NotImplementedException("CMapWorld_GetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetName")]
        public static void* CMapWorld_SetName( void* self, void* name )
        {
            throw new NotImplementedException("CMapWorld_SetName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetParent")]
        public static int CMapWorld_GetParent( void* self )
        {
            throw new NotImplementedException("CMapWorld_GetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetParent")]
        public static void* CMapWorld_SetParent( void* self, void* parent )
        {
            throw new NotImplementedException("CMapWorld_SetParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetParentWorld")]
        public static int CMapWorld_GetParentWorld( void* self )
        {
            throw new NotImplementedException("CMapWorld_GetParentWorld is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetRootDocument")]
        public static int CMapWorld_GetRootDocument( void* self, long nDocumentLoaded )
        {
            throw new NotImplementedException("CMapWorld_GetRootDocument is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetDescription")]
        public static void* CMapWorld_GetDescription( void* self )
        {
            throw new NotImplementedException("CMapWorld_GetDescription is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetTypeString")]
        public static void* CMapWorld_GetTypeString( void* self )
        {
            throw new NotImplementedException("CMapWorld_GetTypeString is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_Copy")]
        public static int CMapWorld_Copy( void* self )
        {
            throw new NotImplementedException("CMapWorld_Copy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_IsVisible")]
        public static int CMapWorld_IsVisible( void* self )
        {
            throw new NotImplementedException("CMapWorld_IsVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetVisible")]
        public static void* CMapWorld_SetVisible( void* self, int bVisible )
        {
            throw new NotImplementedException("CMapWorld_SetVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_IsSelected")]
        public static int CMapWorld_IsSelected( void* self )
        {
            throw new NotImplementedException("CMapWorld_IsSelected is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetChildCount")]
        public static int CMapWorld_GetChildCount( void* self )
        {
            throw new NotImplementedException("CMapWorld_GetChildCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetChild")]
        public static int CMapWorld_GetChild( void* self, int i )
        {
            throw new NotImplementedException("CMapWorld_GetChild is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetFirstDescendent")]
        public static int CMapWorld_GetFirstDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapWorld_GetFirstDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetNextDescendent")]
        public static int CMapWorld_GetNextDescendent( void* self, void* pos )
        {
            throw new NotImplementedException("CMapWorld_GetNextDescendent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_AsMapEntity")]
        public static int CMapWorld_AsMapEntity( void* self )
        {
            throw new NotImplementedException("CMapWorld_AsMapEntity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetOrigin")]
        public static void* CMapWorld_GetOrigin( void* self )
        {
            throw new NotImplementedException("CMapWorld_GetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetAngles")]
        public static void* CMapWorld_GetAngles( void* self )
        {
            throw new NotImplementedException("CMapWorld_GetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetScales")]
        public static void* CMapWorld_GetScales( void* self )
        {
            throw new NotImplementedException("CMapWorld_GetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetOrigin")]
        public static void* CMapWorld_SetOrigin( void* self, void* origin )
        {
            throw new NotImplementedException("CMapWorld_SetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetAngles")]
        public static void* CMapWorld_SetAngles( void* self, void* angles )
        {
            throw new NotImplementedException("CMapWorld_SetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetScales")]
        public static void* CMapWorld_SetScales( void* self, void* scales )
        {
            throw new NotImplementedException("CMapWorld_SetScales is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_BeginTransformOperation")]
        public static void* CMapWorld_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            throw new NotImplementedException("CMapWorld_BeginTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_Transform")]
        public static void* CMapWorld_Transform( void* self, void* matrix, long nFlags )
        {
            throw new NotImplementedException("CMapWorld_Transform is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_EndTransformOperation")]
        public static void* CMapWorld_EndTransformOperation( void* self )
        {
            throw new NotImplementedException("CMapWorld_EndTransformOperation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_MarkBoundsDirty")]
        public static void* CMapWorld_MarkBoundsDirty( void* self )
        {
            throw new NotImplementedException("CMapWorld_MarkBoundsDirty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_FullBoundsUpdate")]
        public static void* CMapWorld_FullBoundsUpdate( void* self )
        {
            throw new NotImplementedException("CMapWorld_FullBoundsUpdate is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetModifiedFlag")]
        public static void* CMapWorld_SetModifiedFlag( void* self )
        {
            throw new NotImplementedException("CMapWorld_SetModifiedFlag is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GeneratesEntityModelGeometry")]
        public static int CMapWorld_GeneratesEntityModelGeometry( void* self )
        {
            throw new NotImplementedException("CMapWorld_GeneratesEntityModelGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_DescriptionChanged")]
        public static void* CMapWorld_DescriptionChanged( void* self )
        {
            throw new NotImplementedException("CMapWorld_DescriptionChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_CoreAttributeChanged")]
        public static void* CMapWorld_CoreAttributeChanged( void* self )
        {
            throw new NotImplementedException("CMapWorld_CoreAttributeChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CFramelessMainWindow_To_CQHammerMainWnd", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_CFramelessMainWindow_To_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CFramelessMainWindow_From_CQHammerMainWnd", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_CFramelessMainWindow_From_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_QMainWindow_To_CQHammerMainWnd", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_QMainWindow_To_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_QMainWindow_From_CQHammerMainWnd", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_QMainWindow_From_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_QWidget_To_CQHammerMainWnd", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_QWidget_To_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_QWidget_From_CQHammerMainWnd", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_QWidget_From_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_QObject_To_CQHammerMainWnd", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* From_QObject_To_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_QObject_From_CQHammerMainWnd", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* To_QObject_From_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_CreateEverything")]
        public static void* CQHammerMainWnd_CreateEverything( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_CreateEverything is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_CreateMenus")]
        public static void* CQHammerMainWnd_CreateMenus( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_CreateMenus is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_SetupDefaultLayout")]
        public static void* CQHammerMainWnd_SetupDefaultLayout( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_SetupDefaultLayout is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_SetTitleBarWidgets")]
        public static void* CQHammerMainWnd_SetTitleBarWidgets( void* self, void* icon, void* titleLabel, void* menuBar, void* grabber, void* minimizeButton, void* maximizeButton, void* closeButton )
        {
            throw new NotImplementedException("CQHammerMainWnd_SetTitleBarWidgets is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_iconSize")]
        public static void* CQHammerMainWnd_iconSize( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_iconSize is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setIconSize")]
        public static void* CQHammerMainWnd_setIconSize( void* self, void* iconSize )
        {
            throw new NotImplementedException("CQHammerMainWnd_setIconSize is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_menuBar")]
        public static void* CQHammerMainWnd_menuBar( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_menuBar is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setMenuBar")]
        public static void* CQHammerMainWnd_setMenuBar( void* self, void* menubar )
        {
            throw new NotImplementedException("CQHammerMainWnd_setMenuBar is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setMenuWidget")]
        public static void* CQHammerMainWnd_setMenuWidget( void* self, void* menubar )
        {
            throw new NotImplementedException("CQHammerMainWnd_setMenuWidget is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_statusBar")]
        public static void* CQHammerMainWnd_statusBar( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_statusBar is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setStatusBar")]
        public static void* CQHammerMainWnd_setStatusBar( void* self, void* statusbar )
        {
            throw new NotImplementedException("CQHammerMainWnd_setStatusBar is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_centralWidget")]
        public static void* CQHammerMainWnd_centralWidget( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_centralWidget is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setCentralWidget")]
        public static void* CQHammerMainWnd_setCentralWidget( void* self, void* widget )
        {
            throw new NotImplementedException("CQHammerMainWnd_setCentralWidget is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isAnimated")]
        public static int CQHammerMainWnd_isAnimated( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_isAnimated is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setAnimated")]
        public static void* CQHammerMainWnd_setAnimated( void* self, int enabled )
        {
            throw new NotImplementedException("CQHammerMainWnd_setAnimated is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_addToolBarBreak")]
        public static void* CQHammerMainWnd_addToolBarBreak( void* self, long area )
        {
            throw new NotImplementedException("CQHammerMainWnd_addToolBarBreak is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_insertToolBarBreak")]
        public static void* CQHammerMainWnd_insertToolBarBreak( void* self, void* before )
        {
            throw new NotImplementedException("CQHammerMainWnd_insertToolBarBreak is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_addToolBar")]
        public static void* CQHammerMainWnd_addToolBar( void* self, long area, void* toolbar )
        {
            throw new NotImplementedException("CQHammerMainWnd_addToolBar is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_addToolBar_1")]
        public static void* CQHammerMainWnd_addToolBar_1( void* self, void* toolbar )
        {
            throw new NotImplementedException("CQHammerMainWnd_addToolBar_1 is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_insertToolBar")]
        public static void* CQHammerMainWnd_insertToolBar( void* self, void* before, void* toolbar )
        {
            throw new NotImplementedException("CQHammerMainWnd_insertToolBar is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_removeToolBar")]
        public static void* CQHammerMainWnd_removeToolBar( void* self, void* toolbar )
        {
            throw new NotImplementedException("CQHammerMainWnd_removeToolBar is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_removeToolBarBreak")]
        public static void* CQHammerMainWnd_removeToolBarBreak( void* self, void* before )
        {
            throw new NotImplementedException("CQHammerMainWnd_removeToolBarBreak is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_saveState")]
        public static void* CQHammerMainWnd_saveState( void* self, int version )
        {
            throw new NotImplementedException("CQHammerMainWnd_saveState is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_restoreState")]
        public static int CQHammerMainWnd_restoreState( void* self, void* state )
        {
            throw new NotImplementedException("CQHammerMainWnd_restoreState is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isTopLevel")]
        public static int CQHammerMainWnd_isTopLevel( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_isTopLevel is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isWindow")]
        public static int CQHammerMainWnd_isWindow( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_isWindow is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isModal")]
        public static int CQHammerMainWnd_isModal( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_isModal is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setStyleSheet")]
        public static void* CQHammerMainWnd_setStyleSheet( void* self, void* sheet )
        {
            throw new NotImplementedException("CQHammerMainWnd_setStyleSheet is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_windowTitle")]
        public static void* CQHammerMainWnd_windowTitle( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_windowTitle is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowTitle")]
        public static void* CQHammerMainWnd_setWindowTitle( void* self, void* title )
        {
            throw new NotImplementedException("CQHammerMainWnd_setWindowTitle is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowFlags")]
        public static void* CQHammerMainWnd_setWindowFlags( void* self, long type )
        {
            throw new NotImplementedException("CQHammerMainWnd_setWindowFlags is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_windowFlags")]
        public static long CQHammerMainWnd_windowFlags( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_windowFlags is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_size")]
        public static void* CQHammerMainWnd_size( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_size is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_resize")]
        public static void* CQHammerMainWnd_resize( void* self, void* x )
        {
            throw new NotImplementedException("CQHammerMainWnd_resize is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_minimumSize")]
        public static void* CQHammerMainWnd_minimumSize( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_minimumSize is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setMinimumSize")]
        public static void* CQHammerMainWnd_setMinimumSize( void* self, void* x )
        {
            throw new NotImplementedException("CQHammerMainWnd_setMinimumSize is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_maximumSize")]
        public static void* CQHammerMainWnd_maximumSize( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_maximumSize is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setMaximumSize")]
        public static void* CQHammerMainWnd_setMaximumSize( void* self, void* x )
        {
            throw new NotImplementedException("CQHammerMainWnd_setMaximumSize is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_sizeHint")]
        public static void* CQHammerMainWnd_sizeHint( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_sizeHint is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_pos")]
        public static void* CQHammerMainWnd_pos( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_pos is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_move")]
        public static void* CQHammerMainWnd_move( void* self, void* x )
        {
            throw new NotImplementedException("CQHammerMainWnd_move is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isEnabled")]
        public static int CQHammerMainWnd_isEnabled( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_isEnabled is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setEnabled")]
        public static void* CQHammerMainWnd_setEnabled( void* self, int x )
        {
            throw new NotImplementedException("CQHammerMainWnd_setEnabled is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setVisible")]
        public static void* CQHammerMainWnd_setVisible( void* self, int visible )
        {
            throw new NotImplementedException("CQHammerMainWnd_setVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_show")]
        public static void* CQHammerMainWnd_show( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_show is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_hide")]
        public static void* CQHammerMainWnd_hide( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_hide is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_showMinimized")]
        public static void* CQHammerMainWnd_showMinimized( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_showMinimized is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_showMaximized")]
        public static void* CQHammerMainWnd_showMaximized( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_showMaximized is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_showFullScreen")]
        public static void* CQHammerMainWnd_showFullScreen( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_showFullScreen is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_showNormal")]
        public static void* CQHammerMainWnd_showNormal( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_showNormal is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_close")]
        public static int CQHammerMainWnd_close( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_close is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_raise")]
        public static void* CQHammerMainWnd_raise( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_raise is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_lower")]
        public static void* CQHammerMainWnd_lower( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_lower is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isVisible")]
        public static int CQHammerMainWnd_isVisible( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_isVisible is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isHidden")]
        public static int CQHammerMainWnd_isHidden( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_isHidden is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setHidden")]
        public static void* CQHammerMainWnd_setHidden( void* self, int b )
        {
            throw new NotImplementedException("CQHammerMainWnd_setHidden is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setAttribute")]
        public static void* CQHammerMainWnd_setAttribute( void* self, long a, int on )
        {
            throw new NotImplementedException("CQHammerMainWnd_setAttribute is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_testAttribute")]
        public static int CQHammerMainWnd_testAttribute( void* self, long a )
        {
            throw new NotImplementedException("CQHammerMainWnd_testAttribute is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_acceptDrops")]
        public static int CQHammerMainWnd_acceptDrops( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_acceptDrops is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setAcceptDrops")]
        public static void* CQHammerMainWnd_setAcceptDrops( void* self, int on )
        {
            throw new NotImplementedException("CQHammerMainWnd_setAcceptDrops is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_updateGeometry")]
        public static void* CQHammerMainWnd_updateGeometry( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_updateGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_update")]
        public static void* CQHammerMainWnd_update( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_update is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_repaint")]
        public static void* CQHammerMainWnd_repaint( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_repaint is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setCursor")]
        public static void* CQHammerMainWnd_setCursor( void* self, long shape )
        {
            throw new NotImplementedException("CQHammerMainWnd_setCursor is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setCursor_1")]
        public static void* CQHammerMainWnd_setCursor_1( void* self, void* pixmap )
        {
            throw new NotImplementedException("CQHammerMainWnd_setCursor_1 is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_unsetCursor")]
        public static void* CQHammerMainWnd_unsetCursor( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_unsetCursor is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowIcon")]
        public static void* CQHammerMainWnd_setWindowIcon( void* self, void* icon )
        {
            throw new NotImplementedException("CQHammerMainWnd_setWindowIcon is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowIconFromPixmap")]
        public static void* CQHammerMainWnd_setWindowIconFromPixmap( void* self, void* icon )
        {
            throw new NotImplementedException("CQHammerMainWnd_setWindowIconFromPixmap is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowIconText")]
        public static void* CQHammerMainWnd_setWindowIconText( void* self, void* str )
        {
            throw new NotImplementedException("CQHammerMainWnd_setWindowIconText is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowOpacity")]
        public static void* CQHammerMainWnd_setWindowOpacity( void* self, float level )
        {
            throw new NotImplementedException("CQHammerMainWnd_setWindowOpacity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_windowOpacity")]
        public static float CQHammerMainWnd_windowOpacity( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_windowOpacity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isMinimized")]
        public static int CQHammerMainWnd_isMinimized( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_isMinimized is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isMaximized")]
        public static int CQHammerMainWnd_isMaximized( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_isMaximized is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isFullScreen")]
        public static int CQHammerMainWnd_isFullScreen( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_isFullScreen is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setMouseTracking")]
        public static void* CQHammerMainWnd_setMouseTracking( void* self, int enable )
        {
            throw new NotImplementedException("CQHammerMainWnd_setMouseTracking is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_hasMouseTracking")]
        public static int CQHammerMainWnd_hasMouseTracking( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_hasMouseTracking is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_underMouse")]
        public static int CQHammerMainWnd_underMouse( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_underMouse is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_mapToGlobal")]
        public static void* CQHammerMainWnd_mapToGlobal( void* self, void* p )
        {
            throw new NotImplementedException("CQHammerMainWnd_mapToGlobal is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_mapFromGlobal")]
        public static void* CQHammerMainWnd_mapFromGlobal( void* self, void* p )
        {
            throw new NotImplementedException("CQHammerMainWnd_mapFromGlobal is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_hasFocus")]
        public static int CQHammerMainWnd_hasFocus( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_hasFocus is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_focusPolicy")]
        public static long CQHammerMainWnd_focusPolicy( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_focusPolicy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFocusPolicy")]
        public static void* CQHammerMainWnd_setFocusPolicy( void* self, long policy )
        {
            throw new NotImplementedException("CQHammerMainWnd_setFocusPolicy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFocusProxy")]
        public static void* CQHammerMainWnd_setFocusProxy( void* self, void* widget )
        {
            throw new NotImplementedException("CQHammerMainWnd_setFocusProxy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isActiveWindow")]
        public static int CQHammerMainWnd_isActiveWindow( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_isActiveWindow is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_updatesEnabled")]
        public static int CQHammerMainWnd_updatesEnabled( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_updatesEnabled is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setUpdatesEnabled")]
        public static void* CQHammerMainWnd_setUpdatesEnabled( void* self, int enable )
        {
            throw new NotImplementedException("CQHammerMainWnd_setUpdatesEnabled is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFocus")]
        public static void* CQHammerMainWnd_setFocus( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_setFocus is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_activateWindow")]
        public static void* CQHammerMainWnd_activateWindow( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_activateWindow is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_clearFocus")]
        public static void* CQHammerMainWnd_clearFocus( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_clearFocus is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_devicePixelRatioF")]
        public static float CQHammerMainWnd_devicePixelRatioF( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_devicePixelRatioF is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_saveGeometry")]
        public static void* CQHammerMainWnd_saveGeometry( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_saveGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_restoreGeometry")]
        public static int CQHammerMainWnd_restoreGeometry( void* self, void* state )
        {
            throw new NotImplementedException("CQHammerMainWnd_restoreGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_addAction")]
        public static void* CQHammerMainWnd_addAction( void* self, void* action )
        {
            throw new NotImplementedException("CQHammerMainWnd_addAction is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_removeAction")]
        public static void* CQHammerMainWnd_removeAction( void* self, void* action )
        {
            throw new NotImplementedException("CQHammerMainWnd_removeAction is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setParent")]
        public static void* CQHammerMainWnd_setParent( void* self, void* parent )
        {
            throw new NotImplementedException("CQHammerMainWnd_setParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_parentWidget")]
        public static void* CQHammerMainWnd_parentWidget( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_parentWidget is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_window")]
        public static void* CQHammerMainWnd_window( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_window is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_AddClassName")]
        public static void* CQHammerMainWnd_AddClassName( void* self, void* name )
        {
            throw new NotImplementedException("CQHammerMainWnd_AddClassName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_Polish")]
        public static void* CQHammerMainWnd_Polish( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_Polish is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_toolTip")]
        public static void* CQHammerMainWnd_toolTip( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_toolTip is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setToolTip")]
        public static void* CQHammerMainWnd_setToolTip( void* self, void* str )
        {
            throw new NotImplementedException("CQHammerMainWnd_setToolTip is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_statusTip")]
        public static void* CQHammerMainWnd_statusTip( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_statusTip is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setStatusTip")]
        public static void* CQHammerMainWnd_setStatusTip( void* self, void* str )
        {
            throw new NotImplementedException("CQHammerMainWnd_setStatusTip is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_toolTipDuration")]
        public static int CQHammerMainWnd_toolTipDuration( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_toolTipDuration is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setToolTipDuration")]
        public static void* CQHammerMainWnd_setToolTipDuration( void* self, int x )
        {
            throw new NotImplementedException("CQHammerMainWnd_setToolTipDuration is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_autoFillBackground")]
        public static int CQHammerMainWnd_autoFillBackground( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_autoFillBackground is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setAutoFillBackground")]
        public static void* CQHammerMainWnd_setAutoFillBackground( void* self, int enabled )
        {
            throw new NotImplementedException("CQHammerMainWnd_setAutoFillBackground is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_adjustSize")]
        public static void* CQHammerMainWnd_adjustSize( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_adjustSize is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_windowModality")]
        public static long CQHammerMainWnd_windowModality( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_windowModality is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowModality")]
        public static void* CQHammerMainWnd_setWindowModality( void* self, long windowModality )
        {
            throw new NotImplementedException("CQHammerMainWnd_setWindowModality is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_ScreenGeometry")]
        public static void* CQHammerMainWnd_ScreenGeometry( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_ScreenGeometry is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setContentsMargins")]
        public static void* CQHammerMainWnd_setContentsMargins( void* self, int left, int top, int right, int bottom )
        {
            throw new NotImplementedException("CQHammerMainWnd_setContentsMargins is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_contentsMargins")]
        public static void* CQHammerMainWnd_contentsMargins( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_contentsMargins is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_layout")]
        public static void* CQHammerMainWnd_layout( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_layout is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setLayout")]
        public static void* CQHammerMainWnd_setLayout( void* self, void* l )
        {
            throw new NotImplementedException("CQHammerMainWnd_setLayout is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_contentsRect")]
        public static void* CQHammerMainWnd_contentsRect( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_contentsRect is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_SetEffectOpacity")]
        public static void* CQHammerMainWnd_SetEffectOpacity( void* self, float opacity )
        {
            throw new NotImplementedException("CQHammerMainWnd_SetEffectOpacity is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setSizePolicy")]
        public static void* CQHammerMainWnd_setSizePolicy( void* self, long x, long y )
        {
            throw new NotImplementedException("CQHammerMainWnd_setSizePolicy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_GetHorizontalSizePolicy")]
        public static long CQHammerMainWnd_GetHorizontalSizePolicy( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_GetHorizontalSizePolicy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_GetVerticalSizePolicy")]
        public static long CQHammerMainWnd_GetVerticalSizePolicy( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_GetVerticalSizePolicy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_SetHorizontalSizePolicy")]
        public static void* CQHammerMainWnd_SetHorizontalSizePolicy( void* self, long mode )
        {
            throw new NotImplementedException("CQHammerMainWnd_SetHorizontalSizePolicy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_SetVerticalSizePolicy")]
        public static void* CQHammerMainWnd_SetVerticalSizePolicy( void* self, long mode )
        {
            throw new NotImplementedException("CQHammerMainWnd_SetVerticalSizePolicy is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFixedSize")]
        public static void* CQHammerMainWnd_setFixedSize( void* self, int w, int h )
        {
            throw new NotImplementedException("CQHammerMainWnd_setFixedSize is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFixedWidth")]
        public static void* CQHammerMainWnd_setFixedWidth( void* self, int w )
        {
            throw new NotImplementedException("CQHammerMainWnd_setFixedWidth is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFixedHeight")]
        public static void* CQHammerMainWnd_setFixedHeight( void* self, int h )
        {
            throw new NotImplementedException("CQHammerMainWnd_setFixedHeight is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_winId")]
        public static void* CQHammerMainWnd_winId( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_winId is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_deleteLater")]
        public static void* CQHammerMainWnd_deleteLater( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_deleteLater is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_objectName")]
        public static void* CQHammerMainWnd_objectName( void* self )
        {
            throw new NotImplementedException("CQHammerMainWnd_objectName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setObjectName")]
        public static void* CQHammerMainWnd_setObjectName( void* self, void* name )
        {
            throw new NotImplementedException("CQHammerMainWnd_setObjectName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setParent_1")]
        public static void* CQHammerMainWnd_setParent_1( void* self, void* parent )
        {
            throw new NotImplementedException("CQHammerMainWnd_setParent_1 is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setProperty")]
        public static void* CQHammerMainWnd_setProperty( void* self, void* key, int value )
        {
            throw new NotImplementedException("CQHammerMainWnd_setProperty is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setProperty_1")]
        public static void* CQHammerMainWnd_setProperty_1( void* self, void* key, float value )
        {
            throw new NotImplementedException("CQHammerMainWnd_setProperty_1 is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setProperty_2")]
        public static void* CQHammerMainWnd_setProperty_2( void* self, void* key, void* value )
        {
            throw new NotImplementedException("CQHammerMainWnd_setProperty_2 is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_DeleteThis")]
        public static void* CSavedObjects_DeleteThis( void* self )
        {
            throw new NotImplementedException("CSavedObjects_DeleteThis is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_Create")]
        public static void* CSavedObjects_Create()
        {
            throw new NotImplementedException("CSavedObjects_Create is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_RemoveAll")]
        public static void* CSavedObjects_RemoveAll( void* self )
        {
            throw new NotImplementedException("CSavedObjects_RemoveAll is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_SaveObject")]
        public static void* CSavedObjects_SaveObject( void* self, void* pObject )
        {
            throw new NotImplementedException("CSavedObjects_SaveObject is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_RestoreObjects")]
        public static void* CSavedObjects_RestoreObjects( void* self )
        {
            throw new NotImplementedException("CSavedObjects_RestoreObjects is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_Count")]
        public static int CSavedObjects_Count( void* self )
        {
            throw new NotImplementedException("CSavedObjects_Count is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_GetMode")]
        public static long CSelection_GetMode( void* self )
        {
            throw new NotImplementedException("CSelection_GetMode is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_SetMode")]
        public static void* CSelection_SetMode( void* self, long mode, long method )
        {
            throw new NotImplementedException("CSelection_SetMode is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_ActiveSelectionSet")]
        public static void* CSelection_ActiveSelectionSet( void* self )
        {
            throw new NotImplementedException("CSelection_ActiveSelectionSet is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_GetSelectionSetForMode")]
        public static void* CSelection_GetSelectionSetForMode( void* self, long nMode )
        {
            throw new NotImplementedException("CSelection_GetSelectionSetForMode is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_GetNumSelectionSets")]
        public static int CSelection_GetNumSelectionSets( void* self )
        {
            throw new NotImplementedException("CSelection_GetNumSelectionSets is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_GetSelectionSet")]
        public static void* CSelection_GetSelectionSet( void* self, int nIndex )
        {
            throw new NotImplementedException("CSelection_GetSelectionSet is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlock_OnObjectTypeChanged")]
        public static void* CToolBlock_OnObjectTypeChanged( void* self )
        {
            throw new NotImplementedException("CToolBlock_OnObjectTypeChanged is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlock_SetPrimitiveType2D")]
        public static void* CToolBlock_SetPrimitiveType2D( void* self, int primitiveIs2D )
        {
            throw new NotImplementedException("CToolBlock_SetPrimitiveType2D is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlock_GetOrientPrimitives")]
        public static int CToolBlock_GetOrientPrimitives( void* self )
        {
            throw new NotImplementedException("CToolBlock_GetOrientPrimitives is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlock_SetOrientPrimitives")]
        public static void* CToolBlock_SetOrientPrimitives( void* self, int bOrientPrimitives )
        {
            throw new NotImplementedException("CToolBlock_SetOrientPrimitives is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "_Get__CToolBlock_m_OverrideMaterial", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* _Get__CToolBlock_m_OverrideMaterial( void* self )
        {
            throw new NotImplementedException("CToolBlock_m_OverrideMaterial");
        }
        [UnmanagedCallersOnly(EntryPoint = "_Set__CToolBlock_m_OverrideMaterial", CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
        public static void* _Set__CToolBlock_m_OverrideMaterial( void* self, void* value )
        {
            throw new NotImplementedException("CToolBlock_m_OverrideMaterial");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlockState_GetAABBBounds")]
        public static void* CToolBlockState_GetAABBBounds( void* self )
        {
            throw new NotImplementedException("CToolBlockState_GetAABBBounds is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlockState_GetDragWorkPlane")]
        public static void* CToolBlockState_GetDragWorkPlane( void* self )
        {
            throw new NotImplementedException("CToolBlockState_GetDragWorkPlane is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_GetOrigin")]
        public static void* CToolCamera_GetOrigin( void* self )
        {
            throw new NotImplementedException("CToolCamera_GetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_GetAngles")]
        public static void* CToolCamera_GetAngles( void* self )
        {
            throw new NotImplementedException("CToolCamera_GetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_SetOrigin")]
        public static void* CToolCamera_SetOrigin( void* self, void* origin )
        {
            throw new NotImplementedException("CToolCamera_SetOrigin is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_SetAngles")]
        public static void* CToolCamera_SetAngles( void* self, void* angles )
        {
            throw new NotImplementedException("CToolCamera_SetAngles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_GetWidth")]
        public static float CToolCamera_GetWidth( void* self )
        {
            throw new NotImplementedException("CToolCamera_GetWidth is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_GetHeight")]
        public static float CToolCamera_GetHeight( void* self )
        {
            throw new NotImplementedException("CToolCamera_GetHeight is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_GetCameraFOV")]
        public static float CToolCamera_GetCameraFOV( void* self )
        {
            throw new NotImplementedException("CToolCamera_GetCameraFOV is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_BuildRay")]
        public static void* CToolCamera_BuildRay( void* self, void* view, void* start, void* end )
        {
            throw new NotImplementedException("CToolCamera_BuildRay is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolEntity_StartBlockEntityCreation")]
        public static void* CToolEntity_StartBlockEntityCreation( void* self, void* className )
        {
            throw new NotImplementedException("CToolEntity_StartBlockEntityCreation is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CWorkPlane_GetWorkPlaneToWorldTransform")]
        public static void* CWorkPlane_GetWorkPlaneToWorldTransform( void* self )
        {
            throw new NotImplementedException("CWorkPlane_GetWorkPlaneToWorldTransform is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "global_MaterialGetMappingWidth")]
        public static int global_MaterialGetMappingWidth( void* material )
        {
            throw new NotImplementedException("global_MaterialGetMappingWidth is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "global_MaterialGetMappingHeight")]
        public static int global_MaterialGetMappingHeight( void* material )
        {
            throw new NotImplementedException("global_MaterialGetMappingHeight is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_SelectObject")]
        public static int ISelectionSet_SelectObject( void* self, void* pObject, long selectOp )
        {
            throw new NotImplementedException("ISelectionSet_SelectObject is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_GetSelectedObject")]
        public static int ISelectionSet_GetSelectedObject( void* self, int nIndex )
        {
            throw new NotImplementedException("ISelectionSet_GetSelectedObject is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_Count")]
        public static int ISelectionSet_Count( void* self )
        {
            throw new NotImplementedException("ISelectionSet_Count is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_RemoveAll")]
        public static void* ISelectionSet_RemoveAll( void* self )
        {
            throw new NotImplementedException("ISelectionSet_RemoveAll is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_SelectAll")]
        public static void* ISelectionSet_SelectAll( void* self )
        {
            throw new NotImplementedException("ISelectionSet_SelectAll is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_InvertSelection")]
        public static void* ISelectionSet_InvertSelection( void* self )
        {
            throw new NotImplementedException("ISelectionSet_InvertSelection is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_GetPivotPosition")]
        public static void* ISelectionSet_GetPivotPosition( void* self )
        {
            throw new NotImplementedException("ISelectionSet_GetPivotPosition is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_SetPivot")]
        public static void* ISelectionSet_SetPivot( void* self, void* pivot )
        {
            throw new NotImplementedException("ISelectionSet_SetPivot is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "NativeHammer_Options_GetShowHelpers")]
        public static int NativeHammer_Options_GetShowHelpers()
        {
            throw new NotImplementedException("NativeHammer_Options_GetShowHelpers is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "NativeHammer_Options_GetShowGameObjectsOnly")]
        public static int NativeHammer_Options_GetShowGameObjectsOnly()
        {
            throw new NotImplementedException("NativeHammer_Options_GetShowGameObjectsOnly is not yet implemented in the linux emulation layer");
        }
    }
    public static unsafe partial class Imports
    {
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_BlockToolGlue_BuildGeometry;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_BlockToolGlue_BuildUI;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_BlockToolGlue_SetInProgress;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_BlockToolGlue_SetOverrideEntity;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_EntityToolGlue_Create;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_Hammer_Init;
        public static delegate* unmanaged<void*> _ptr_dtrMpdtr_Hammer_RunFrame;
        public static delegate* unmanaged<void*> _ptr_dtrMpdtr_Hammer_Shutdown;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_Hammer_UpdateActiveMaterial;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_Hammer_RenderMapViewHUD;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_Hammer_PreSaveMap;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_Hammer_PostLoadMap;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_Hammer_MapAssetSaved;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_Hammer_OnMapViewOpenContextMenu;
        public static delegate* unmanaged<void*> _ptr_dtrMpdtr_Hammer_OnCreateGameObjectMenu;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HammerEvents_OnMapNodeDescriptionChanged;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_HammerEvents_OnObjectAddedToDocument;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_HammerEvents_OnObjectRemovedFromDocument;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HammerEvents_OnMeshesTiedToGameObject;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HammerMainWindow_WindowInit;
        public static delegate* unmanaged<void*, void*, void*, void*, void*, long, long, float, void*> _ptr_dtrMpdtr_HammerMainWindow_AddNativeDock;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_HammerMainWindow_CreateDynamicViewMenu;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HammerMainWindow_ToggleAssetBrowser;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_HammerMainWindow_ToggleFullscreenLayout;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HammerMainWindow_InitHammerMainWindow;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HmmrMngdnspctr_Create;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_HmmrMngdnspctr_Inspect;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HmmrMngdnspctr_GetWidget;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_HammerSession_Create;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HammerSession_Destroyed;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HammerSession_LaunchMapInEngine;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HammerSession_OnPreCompileFinished;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HammerSession_OnPostCompileFinished;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_HammerSession_OnPaste;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_IEntityTool_CreateUI;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_IEntityTool_GetCurrentEntityClassName;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_IPathTool_CreateUI;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_IPathTool_GetCurrentEntityClassName;
        public static delegate* unmanaged<void*, float> _ptr_dtrMpdtr_IPathTool_GetRadiusOffset;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_IPathTool_IsRadiusOffsetEnabled;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_OnAddToWorld;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_OnRemoveFromWorld;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_PreSaveToFile;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_PostLoadFromFile;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_PostLoadDocument;
        public static delegate* unmanaged<void*, void*, void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_OnCopyFrom;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_OnParentChanged;
        public static delegate* unmanaged<void*, void*, void*, void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_OnTransformChanged;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_OnSetEnabled;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_GetMimeData;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_GetGameObjectName;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_MapNodeCallbacks_GetWorldResourceReferencesAndDependencies;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_MpVwDrpTrgt_OnDragEnter;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_MpVwDrpTrgt_OnDragMove;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_MpVwDrpTrgt_OnDrop;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMpdtr_MpVwDrpTrgt_OnDragLeave;
        public static delegate* unmanaged<void*> _ptr_dtrMpdtr_MpVwDrpTrgt_GetDragAndDropActive;
        public static delegate* unmanaged<void*, void*, void*> _ptr_dtrMpdtr_MapViewRender_OnPreRender;
        public static delegate* unmanaged<void*, void*, void*, void*> _ptr_dtrMpdtr_MapViewRender_TraceManagedGizmos;
        public static delegate* unmanaged<void*> _ptr_dtrMpdtr_PathToolGlue_Create;
        public static delegate* unmanaged<void*> _ptr_dtrMpdtr_Selection_OnSelectionChanged;
        public static void StoreImport_dtrMpdtr_BlockToolGlue_BuildGeometry(void* ptr)
        {
            _ptr_dtrMpdtr_BlockToolGlue_BuildGeometry = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_BlockToolGlue_BuildUI(void* ptr)
        {
            _ptr_dtrMpdtr_BlockToolGlue_BuildUI = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_BlockToolGlue_SetInProgress(void* ptr)
        {
            _ptr_dtrMpdtr_BlockToolGlue_SetInProgress = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_BlockToolGlue_SetOverrideEntity(void* ptr)
        {
            _ptr_dtrMpdtr_BlockToolGlue_SetOverrideEntity = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_EntityToolGlue_Create(void* ptr)
        {
            _ptr_dtrMpdtr_EntityToolGlue_Create = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_Hammer_Init(void* ptr)
        {
            _ptr_dtrMpdtr_Hammer_Init = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_Hammer_RunFrame(void* ptr)
        {
            _ptr_dtrMpdtr_Hammer_RunFrame = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_Hammer_Shutdown(void* ptr)
        {
            _ptr_dtrMpdtr_Hammer_Shutdown = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_Hammer_UpdateActiveMaterial(void* ptr)
        {
            _ptr_dtrMpdtr_Hammer_UpdateActiveMaterial = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_Hammer_RenderMapViewHUD(void* ptr)
        {
            _ptr_dtrMpdtr_Hammer_RenderMapViewHUD = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_Hammer_PreSaveMap(void* ptr)
        {
            _ptr_dtrMpdtr_Hammer_PreSaveMap = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_Hammer_PostLoadMap(void* ptr)
        {
            _ptr_dtrMpdtr_Hammer_PostLoadMap = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_Hammer_MapAssetSaved(void* ptr)
        {
            _ptr_dtrMpdtr_Hammer_MapAssetSaved = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_Hammer_OnMapViewOpenContextMenu(void* ptr)
        {
            _ptr_dtrMpdtr_Hammer_OnMapViewOpenContextMenu = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_Hammer_OnCreateGameObjectMenu(void* ptr)
        {
            _ptr_dtrMpdtr_Hammer_OnCreateGameObjectMenu = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerEvents_OnMapNodeDescriptionChanged(void* ptr)
        {
            _ptr_dtrMpdtr_HammerEvents_OnMapNodeDescriptionChanged = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerEvents_OnObjectAddedToDocument(void* ptr)
        {
            _ptr_dtrMpdtr_HammerEvents_OnObjectAddedToDocument = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerEvents_OnObjectRemovedFromDocument(void* ptr)
        {
            _ptr_dtrMpdtr_HammerEvents_OnObjectRemovedFromDocument = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerEvents_OnMeshesTiedToGameObject(void* ptr)
        {
            _ptr_dtrMpdtr_HammerEvents_OnMeshesTiedToGameObject = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerMainWindow_WindowInit(void* ptr)
        {
            _ptr_dtrMpdtr_HammerMainWindow_WindowInit = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerMainWindow_AddNativeDock(void* ptr)
        {
            _ptr_dtrMpdtr_HammerMainWindow_AddNativeDock = (delegate* unmanaged<void*, void*, void*, void*, void*, long, long, float, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerMainWindow_CreateDynamicViewMenu(void* ptr)
        {
            _ptr_dtrMpdtr_HammerMainWindow_CreateDynamicViewMenu = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerMainWindow_ToggleAssetBrowser(void* ptr)
        {
            _ptr_dtrMpdtr_HammerMainWindow_ToggleAssetBrowser = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerMainWindow_ToggleFullscreenLayout(void* ptr)
        {
            _ptr_dtrMpdtr_HammerMainWindow_ToggleFullscreenLayout = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerMainWindow_InitHammerMainWindow(void* ptr)
        {
            _ptr_dtrMpdtr_HammerMainWindow_InitHammerMainWindow = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HmmrMngdnspctr_Create(void* ptr)
        {
            _ptr_dtrMpdtr_HmmrMngdnspctr_Create = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HmmrMngdnspctr_Inspect(void* ptr)
        {
            _ptr_dtrMpdtr_HmmrMngdnspctr_Inspect = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HmmrMngdnspctr_GetWidget(void* ptr)
        {
            _ptr_dtrMpdtr_HmmrMngdnspctr_GetWidget = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerSession_Create(void* ptr)
        {
            _ptr_dtrMpdtr_HammerSession_Create = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerSession_Destroyed(void* ptr)
        {
            _ptr_dtrMpdtr_HammerSession_Destroyed = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerSession_LaunchMapInEngine(void* ptr)
        {
            _ptr_dtrMpdtr_HammerSession_LaunchMapInEngine = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerSession_OnPreCompileFinished(void* ptr)
        {
            _ptr_dtrMpdtr_HammerSession_OnPreCompileFinished = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerSession_OnPostCompileFinished(void* ptr)
        {
            _ptr_dtrMpdtr_HammerSession_OnPostCompileFinished = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_HammerSession_OnPaste(void* ptr)
        {
            _ptr_dtrMpdtr_HammerSession_OnPaste = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_IEntityTool_CreateUI(void* ptr)
        {
            _ptr_dtrMpdtr_IEntityTool_CreateUI = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_IEntityTool_GetCurrentEntityClassName(void* ptr)
        {
            _ptr_dtrMpdtr_IEntityTool_GetCurrentEntityClassName = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_IPathTool_CreateUI(void* ptr)
        {
            _ptr_dtrMpdtr_IPathTool_CreateUI = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_IPathTool_GetCurrentEntityClassName(void* ptr)
        {
            _ptr_dtrMpdtr_IPathTool_GetCurrentEntityClassName = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_IPathTool_GetRadiusOffset(void* ptr)
        {
            _ptr_dtrMpdtr_IPathTool_GetRadiusOffset = (delegate* unmanaged<void*, float>)ptr;
        }
        public static void StoreImport_dtrMpdtr_IPathTool_IsRadiusOffsetEnabled(void* ptr)
        {
            _ptr_dtrMpdtr_IPathTool_IsRadiusOffsetEnabled = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_OnAddToWorld(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_OnAddToWorld = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_OnRemoveFromWorld(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_OnRemoveFromWorld = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_PreSaveToFile(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_PreSaveToFile = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_PostLoadFromFile(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_PostLoadFromFile = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_PostLoadDocument(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_PostLoadDocument = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_OnCopyFrom(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_OnCopyFrom = (delegate* unmanaged<void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_OnParentChanged(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_OnParentChanged = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_OnTransformChanged(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_OnTransformChanged = (delegate* unmanaged<void*, void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_OnSetEnabled(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_OnSetEnabled = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_GetMimeData(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_GetMimeData = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_GetGameObjectName(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_GetGameObjectName = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapNodeCallbacks_GetWorldResourceReferencesAndDependencies(void* ptr)
        {
            _ptr_dtrMpdtr_MapNodeCallbacks_GetWorldResourceReferencesAndDependencies = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MpVwDrpTrgt_OnDragEnter(void* ptr)
        {
            _ptr_dtrMpdtr_MpVwDrpTrgt_OnDragEnter = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MpVwDrpTrgt_OnDragMove(void* ptr)
        {
            _ptr_dtrMpdtr_MpVwDrpTrgt_OnDragMove = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MpVwDrpTrgt_OnDrop(void* ptr)
        {
            _ptr_dtrMpdtr_MpVwDrpTrgt_OnDrop = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MpVwDrpTrgt_OnDragLeave(void* ptr)
        {
            _ptr_dtrMpdtr_MpVwDrpTrgt_OnDragLeave = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MpVwDrpTrgt_GetDragAndDropActive(void* ptr)
        {
            _ptr_dtrMpdtr_MpVwDrpTrgt_GetDragAndDropActive = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapViewRender_OnPreRender(void* ptr)
        {
            _ptr_dtrMpdtr_MapViewRender_OnPreRender = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_MapViewRender_TraceManagedGizmos(void* ptr)
        {
            _ptr_dtrMpdtr_MapViewRender_TraceManagedGizmos = (delegate* unmanaged<void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_PathToolGlue_Create(void* ptr)
        {
            _ptr_dtrMpdtr_PathToolGlue_Create = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_dtrMpdtr_Selection_OnSelectionChanged(void* ptr)
        {
            _ptr_dtrMpdtr_Selection_OnSelectionChanged = (delegate* unmanaged<void*>)ptr;
        }

// Editor.MapDoc.MapDocument
// Editor.MapDoc.MapEntity
// Editor.MapDoc.MapGameObject
// Editor.MapDoc.MapGroup
// Editor.MapDoc.MapInstance
// Editor.MapDoc.MapMesh
// Editor.MapDoc.MapNode
// Editor.MapDoc.MapPath
// Editor.MapDoc.MapPathNode
// Editor.MapDoc.MapStaticOverlay
// Editor.MapDoc.MapWorld
// Editor.MapEditor.BlockToolGlue
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_BlockToolGlue_BuildGeometry")]
public static void* dtrMpdtr_BlockToolGlue_BuildGeometry( void* mesh, void* blockState )
{
    return (void*)Imports._ptr_dtrMpdtr_BlockToolGlue_BuildGeometry( mesh, blockState );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_BlockToolGlue_BuildUI")]
public static void* dtrMpdtr_BlockToolGlue_BuildUI( void* tool )
{
    return (void*)Imports._ptr_dtrMpdtr_BlockToolGlue_BuildUI( tool );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_BlockToolGlue_SetInProgress")]
public static void* dtrMpdtr_BlockToolGlue_SetInProgress( void* inProgress )
{
    return (void*)Imports._ptr_dtrMpdtr_BlockToolGlue_SetInProgress( inProgress );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_BlockToolGlue_SetOverrideEntity")]
public static void* dtrMpdtr_BlockToolGlue_SetOverrideEntity( void* entityName )
{
    return (void*)Imports._ptr_dtrMpdtr_BlockToolGlue_SetOverrideEntity( entityName );
}

// Editor.MapEditor.EntityToolGlue
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_EntityToolGlue_Create")]
public static uint dtrMpdtr_EntityToolGlue_Create( void* tool )
{
    return (uint)Imports._ptr_dtrMpdtr_EntityToolGlue_Create( tool );
}

// Editor.MapEditor.Hammer
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_Hammer_Init")]
public static void* dtrMpdtr_Hammer_Init( void* app )
{
    return (void*)Imports._ptr_dtrMpdtr_Hammer_Init( app );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_Hammer_RunFrame")]
public static void* dtrMpdtr_Hammer_RunFrame()
{
    return (void*)Imports._ptr_dtrMpdtr_Hammer_RunFrame();
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_Hammer_Shutdown")]
public static void* dtrMpdtr_Hammer_Shutdown()
{
    return (void*)Imports._ptr_dtrMpdtr_Hammer_Shutdown();
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_Hammer_UpdateActiveMaterial")]
public static void* dtrMpdtr_Hammer_UpdateActiveMaterial( void* material )
{
    return (void*)Imports._ptr_dtrMpdtr_Hammer_UpdateActiveMaterial( material );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_Hammer_RenderMapViewHUD")]
public static void* dtrMpdtr_Hammer_RenderMapViewHUD( void* mapView, void* renderContext )
{
    return (void*)Imports._ptr_dtrMpdtr_Hammer_RenderMapViewHUD( mapView, renderContext );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_Hammer_PreSaveMap")]
public static void* dtrMpdtr_Hammer_PreSaveMap( void* mapDoc )
{
    return (void*)Imports._ptr_dtrMpdtr_Hammer_PreSaveMap( mapDoc );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_Hammer_PostLoadMap")]
public static void* dtrMpdtr_Hammer_PostLoadMap( void* mapDoc )
{
    return (void*)Imports._ptr_dtrMpdtr_Hammer_PostLoadMap( mapDoc );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_Hammer_MapAssetSaved")]
public static void* dtrMpdtr_Hammer_MapAssetSaved( void* asset, void* mapDoc )
{
    return (void*)Imports._ptr_dtrMpdtr_Hammer_MapAssetSaved( asset, mapDoc );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_Hammer_OnMapViewOpenContextMenu")]
public static void* dtrMpdtr_Hammer_OnMapViewOpenContextMenu( void* view, void* menu )
{
    return (void*)Imports._ptr_dtrMpdtr_Hammer_OnMapViewOpenContextMenu( view, menu );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_Hammer_OnCreateGameObjectMenu")]
public static void* dtrMpdtr_Hammer_OnCreateGameObjectMenu()
{
    return (void*)Imports._ptr_dtrMpdtr_Hammer_OnCreateGameObjectMenu();
}

// Editor.MapEditor.MapView
// Editor.MapEditor.HammerEvents
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerEvents_OnMapNodeDescriptionChanged")]
public static void* dtrMpdtr_HammerEvents_OnMapNodeDescriptionChanged( void* mapNode )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerEvents_OnMapNodeDescriptionChanged( mapNode );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerEvents_OnObjectAddedToDocument")]
public static void* dtrMpdtr_HammerEvents_OnObjectAddedToDocument( void* mapNode, void* mapWorld )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerEvents_OnObjectAddedToDocument( mapNode, mapWorld );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerEvents_OnObjectRemovedFromDocument")]
public static void* dtrMpdtr_HammerEvents_OnObjectRemovedFromDocument( void* mapNode, void* mapWorld )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerEvents_OnObjectRemovedFromDocument( mapNode, mapWorld );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerEvents_OnMeshesTiedToGameObject")]
public static void* dtrMpdtr_HammerEvents_OnMeshesTiedToGameObject( void* mapGameObject )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerEvents_OnMeshesTiedToGameObject( mapGameObject );
}

// Editor.MapEditor.HammerMainWindow
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerMainWindow_WindowInit")]
public static void* dtrMpdtr_HammerMainWindow_WindowInit( void* self )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerMainWindow_WindowInit( self );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerMainWindow_AddNativeDock")]
public static void* dtrMpdtr_HammerMainWindow_AddNativeDock( void* self, void* name, void* icon, void* sibling, void* window, long dockArea, long properties, float split )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerMainWindow_AddNativeDock( self, name, icon, sibling, window, dockArea, properties, split );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerMainWindow_CreateDynamicViewMenu")]
public static void* dtrMpdtr_HammerMainWindow_CreateDynamicViewMenu( void* self, void* menu )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerMainWindow_CreateDynamicViewMenu( self, menu );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerMainWindow_ToggleAssetBrowser")]
public static void* dtrMpdtr_HammerMainWindow_ToggleAssetBrowser( void* self )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerMainWindow_ToggleAssetBrowser( self );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerMainWindow_ToggleFullscreenLayout")]
public static void* dtrMpdtr_HammerMainWindow_ToggleFullscreenLayout( void* self, void* fullscreen )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerMainWindow_ToggleFullscreenLayout( self, fullscreen );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerMainWindow_InitHammerMainWindow")]
public static void* dtrMpdtr_HammerMainWindow_InitHammerMainWindow( void* window )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerMainWindow_InitHammerMainWindow( window );
}

// Editor.MapEditor.HammerManagedInspector
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HmmrMngdnspctr_Create")]
public static uint dtrMpdtr_HmmrMngdnspctr_Create( void* parent )
{
    return (uint)Imports._ptr_dtrMpdtr_HmmrMngdnspctr_Create( parent );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HmmrMngdnspctr_Inspect")]
public static int dtrMpdtr_HmmrMngdnspctr_Inspect( void* self, void* mapnode )
{
    return (int)Imports._ptr_dtrMpdtr_HmmrMngdnspctr_Inspect( self, mapnode );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HmmrMngdnspctr_GetWidget")]
public static void* dtrMpdtr_HmmrMngdnspctr_GetWidget( void* self )
{
    return (void*)Imports._ptr_dtrMpdtr_HmmrMngdnspctr_GetWidget( self );
}

// Editor.MapEditor.HammerSession
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerSession_Create")]
public static uint dtrMpdtr_HammerSession_Create( void* native, void* hammer )
{
    return (uint)Imports._ptr_dtrMpdtr_HammerSession_Create( native, hammer );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerSession_Destroyed")]
public static void* dtrMpdtr_HammerSession_Destroyed( void* self )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerSession_Destroyed( self );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerSession_LaunchMapInEngine")]
public static void* dtrMpdtr_HammerSession_LaunchMapInEngine( void* self )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerSession_LaunchMapInEngine( self );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerSession_OnPreCompileFinished")]
public static void* dtrMpdtr_HammerSession_OnPreCompileFinished( void* self )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerSession_OnPreCompileFinished( self );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerSession_OnPostCompileFinished")]
public static void* dtrMpdtr_HammerSession_OnPostCompileFinished( void* self )
{
    return (void*)Imports._ptr_dtrMpdtr_HammerSession_OnPostCompileFinished( self );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_HammerSession_OnPaste")]
public static int dtrMpdtr_HammerSession_OnPaste( void* self )
{
    return (int)Imports._ptr_dtrMpdtr_HammerSession_OnPaste( self );
}

// Editor.MapEditor.IEntityTool
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_IEntityTool_CreateUI")]
public static void* dtrMpdtr_IEntityTool_CreateUI( void* self, void* container )
{
    return (void*)Imports._ptr_dtrMpdtr_IEntityTool_CreateUI( self, container );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_IEntityTool_GetCurrentEntityClassName")]
public static void* dtrMpdtr_IEntityTool_GetCurrentEntityClassName( void* self )
{
    return (void*)Imports._ptr_dtrMpdtr_IEntityTool_GetCurrentEntityClassName( self );
}

// Editor.MapEditor.IPathTool
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_IPathTool_CreateUI")]
public static void* dtrMpdtr_IPathTool_CreateUI( void* self, void* container )
{
    return (void*)Imports._ptr_dtrMpdtr_IPathTool_CreateUI( self, container );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_IPathTool_GetCurrentEntityClassName")]
public static void* dtrMpdtr_IPathTool_GetCurrentEntityClassName( void* self )
{
    return (void*)Imports._ptr_dtrMpdtr_IPathTool_GetCurrentEntityClassName( self );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_IPathTool_GetRadiusOffset")]
public static float dtrMpdtr_IPathTool_GetRadiusOffset( void* self )
{
    return (float)Imports._ptr_dtrMpdtr_IPathTool_GetRadiusOffset( self );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_IPathTool_IsRadiusOffsetEnabled")]
public static int dtrMpdtr_IPathTool_IsRadiusOffsetEnabled( void* self )
{
    return (int)Imports._ptr_dtrMpdtr_IPathTool_IsRadiusOffsetEnabled( self );
}

// Editor.MapEditor.MapNodeCallbacks
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_OnAddToWorld")]
public static void* dtrMpdtr_MapNodeCallbacks_OnAddToWorld( void* node, void* world )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_OnAddToWorld( node, world );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_OnRemoveFromWorld")]
public static void* dtrMpdtr_MapNodeCallbacks_OnRemoveFromWorld( void* node, void* world )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_OnRemoveFromWorld( node, world );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_PreSaveToFile")]
public static void* dtrMpdtr_MapNodeCallbacks_PreSaveToFile( void* node )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_PreSaveToFile( node );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_PostLoadFromFile")]
public static void* dtrMpdtr_MapNodeCallbacks_PostLoadFromFile( void* node )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_PostLoadFromFile( node );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_PostLoadDocument")]
public static void* dtrMpdtr_MapNodeCallbacks_PostLoadDocument( void* doc )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_PostLoadDocument( doc );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_OnCopyFrom")]
public static void* dtrMpdtr_MapNodeCallbacks_OnCopyFrom( void* toNode, void* fromNode, void* copyFlags )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_OnCopyFrom( toNode, fromNode, copyFlags );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_OnParentChanged")]
public static void* dtrMpdtr_MapNodeCallbacks_OnParentChanged( void* node, void* parent )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_OnParentChanged( node, parent );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_OnTransformChanged")]
public static void* dtrMpdtr_MapNodeCallbacks_OnTransformChanged( void* node, void* position, void* angle, void* scale )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_OnTransformChanged( node, position, angle, scale );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_OnSetEnabled")]
public static void* dtrMpdtr_MapNodeCallbacks_OnSetEnabled( void* node, void* enabled )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_OnSetEnabled( node, enabled );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_GetMimeData")]
public static void* dtrMpdtr_MapNodeCallbacks_GetMimeData( void* node, void* data )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_GetMimeData( node, data );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_GetGameObjectName")]
public static void* dtrMpdtr_MapNodeCallbacks_GetGameObjectName( void* node )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_GetGameObjectName( node );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapNodeCallbacks_GetWorldResourceReferencesAndDependencies")]
public static void* dtrMpdtr_MapNodeCallbacks_GetWorldResourceReferencesAndDependencies( void* world, void* references )
{
    return (void*)Imports._ptr_dtrMpdtr_MapNodeCallbacks_GetWorldResourceReferencesAndDependencies( world, references );
}

// Editor.MapEditor.MapViewDropTarget
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MpVwDrpTrgt_OnDragEnter")]
public static int dtrMpdtr_MpVwDrpTrgt_OnDragEnter( void* e, void* mapView )
{
    return (int)Imports._ptr_dtrMpdtr_MpVwDrpTrgt_OnDragEnter( e, mapView );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MpVwDrpTrgt_OnDragMove")]
public static int dtrMpdtr_MpVwDrpTrgt_OnDragMove( void* e, void* mapView )
{
    return (int)Imports._ptr_dtrMpdtr_MpVwDrpTrgt_OnDragMove( e, mapView );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MpVwDrpTrgt_OnDrop")]
public static int dtrMpdtr_MpVwDrpTrgt_OnDrop( void* e, void* mapView )
{
    return (int)Imports._ptr_dtrMpdtr_MpVwDrpTrgt_OnDrop( e, mapView );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MpVwDrpTrgt_OnDragLeave")]
public static void* dtrMpdtr_MpVwDrpTrgt_OnDragLeave( void* mapView )
{
    return (void*)Imports._ptr_dtrMpdtr_MpVwDrpTrgt_OnDragLeave( mapView );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MpVwDrpTrgt_GetDragAndDropActive")]
public static int dtrMpdtr_MpVwDrpTrgt_GetDragAndDropActive()
{
    return (int)Imports._ptr_dtrMpdtr_MpVwDrpTrgt_GetDragAndDropActive();
}

// Editor.MapEditor.MapViewRender
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapViewRender_OnPreRender")]
public static void* dtrMpdtr_MapViewRender_OnPreRender( void* mapView, void* sceneView )
{
    return (void*)Imports._ptr_dtrMpdtr_MapViewRender_OnPreRender( mapView, sceneView );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_MapViewRender_TraceManagedGizmos")]
public static int dtrMpdtr_MapViewRender_TraceManagedGizmos( void* mapView, void* vecPoint2D, void* hitInfo )
{
    return (int)Imports._ptr_dtrMpdtr_MapViewRender_TraceManagedGizmos( mapView, vecPoint2D, hitInfo );
}

// Editor.MapEditor.PathToolGlue
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_PathToolGlue_Create")]
public static uint dtrMpdtr_PathToolGlue_Create()
{
    return (uint)Imports._ptr_dtrMpdtr_PathToolGlue_Create();
}

// Editor.MapEditor.Selection
[UnmanagedCallersOnly(EntryPoint = "dtrMpdtr_Selection_OnSelectionChanged")]
public static void* dtrMpdtr_Selection_OnSelectionChanged()
{
    return (void*)Imports._ptr_dtrMpdtr_Selection_OnSelectionChanged();
}

// Sandbox.DecalSceneObject
// Sandbox.PhysicsBody
// Sandbox.PhysicsGroup
// Sandbox.PhysicsShape
// Sandbox.PhysicsWorld
// Sandbox.SceneCubemap
// Sandbox.SceneLight
// Sandbox.SceneLightProbe
// Sandbox.SceneModel
// Sandbox.SceneObject
// Sandbox.SceneSkyBox
// Sandbox.SceneWorld
// Sandbox.Physics.PhysicsJoint
    }

}
