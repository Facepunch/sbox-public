using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;

namespace Sandbox.Engine.Emulation.Generated
{
    public static unsafe partial class Exports
    {
	public static unsafe void FillNativeFunctionsModeldoc(void** managedFunctions, void** nativeFunctions, int* structSizes)
 	{
 		var i = 0;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged<IntPtr, void>)&Sandbox.Engine.Emulation.EngineExports.DebugError;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CModelDoc_DeleteThis;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void* >)&CModelDoc_Create;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&CModelDoc_SaveToFile;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMdlDcdtrpp_RefreshGameData;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CMdlDcdtrpp_GetSessionModel;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CModelMesh_DeleteThis;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void* >)&CModelMesh_Create;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CModelMesh_AddVertices;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CModelMesh_AddFaceGroup;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&CModelMesh_SetPositions;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&CModelMesh_SetTexCoords;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&CModelMesh_SetNormals;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void*, int, void* >)&CModelMesh_AddFace;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&g_pModelDocUtils_InitFromMesh;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, int >)&NativeEngine_ModelDoc_CreateModelFromMeshFile;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&NativeEngine_ModelDoc_CreateModelFromMesh;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, int >)&NativeEngine_ModelDoc_CreateModelFromMeshes;
 	}

        [UnmanagedCallersOnly(EntryPoint = "CModelDoc_DeleteThis")]
        public static void* CModelDoc_DeleteThis( void* self )
        {
            throw new NotImplementedException("CModelDoc_DeleteThis is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CModelDoc_Create")]
        public static void* CModelDoc_Create()
        {
            throw new NotImplementedException("CModelDoc_Create is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CModelDoc_SaveToFile")]
        public static int CModelDoc_SaveToFile( void* self, void* pFullPath )
        {
            throw new NotImplementedException("CModelDoc_SaveToFile is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMdlDcdtrpp_RefreshGameData")]
        public static void* CMdlDcdtrpp_RefreshGameData( void* self )
        {
            throw new NotImplementedException("CMdlDcdtrpp_RefreshGameData is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CMdlDcdtrpp_GetSessionModel")]
        public static void* CMdlDcdtrpp_GetSessionModel( void* self )
        {
            throw new NotImplementedException("CMdlDcdtrpp_GetSessionModel is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CModelMesh_DeleteThis")]
        public static void* CModelMesh_DeleteThis( void* self )
        {
            throw new NotImplementedException("CModelMesh_DeleteThis is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CModelMesh_Create")]
        public static void* CModelMesh_Create()
        {
            throw new NotImplementedException("CModelMesh_Create is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CModelMesh_AddVertices")]
        public static void* CModelMesh_AddVertices( void* self, int nNumVertices )
        {
            throw new NotImplementedException("CModelMesh_AddVertices is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CModelMesh_AddFaceGroup")]
        public static void* CModelMesh_AddFaceGroup( void* self, void* pMaterialName )
        {
            throw new NotImplementedException("CModelMesh_AddFaceGroup is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CModelMesh_SetPositions")]
        public static void* CModelMesh_SetPositions( void* self, void* pPositions, int nNumPositions )
        {
            throw new NotImplementedException("CModelMesh_SetPositions is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CModelMesh_SetTexCoords")]
        public static void* CModelMesh_SetTexCoords( void* self, void* pTexCoords, int nNumTexCoords )
        {
            throw new NotImplementedException("CModelMesh_SetTexCoords is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CModelMesh_SetNormals")]
        public static void* CModelMesh_SetNormals( void* self, void* pNormals, int nNumNormals )
        {
            throw new NotImplementedException("CModelMesh_SetNormals is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CModelMesh_AddFace")]
        public static void* CModelMesh_AddFace( void* self, int nGroupIndex, void* pVertices, int nNumVertices )
        {
            throw new NotImplementedException("CModelMesh_AddFace is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pModelDocUtils_InitFromMesh")]
        public static int g_pModelDocUtils_InitFromMesh( void* pModelDoc, void* pFilename )
        {
            throw new NotImplementedException("g_pModelDocUtils_InitFromMesh is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "NativeEngine_ModelDoc_CreateModelFromMeshFile")]
        public static int NativeEngine_ModelDoc_CreateModelFromMeshFile( void* pVmdlFileFullPath, void* meshRelativePath, void* material )
        {
            throw new NotImplementedException("NativeEngine_ModelDoc_CreateModelFromMeshFile is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "NativeEngine_ModelDoc_CreateModelFromMesh")]
        public static int NativeEngine_ModelDoc_CreateModelFromMesh( void* pVmdlFileFullPath, void* pMesh )
        {
            throw new NotImplementedException("NativeEngine_ModelDoc_CreateModelFromMesh is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "NativeEngine_ModelDoc_CreateModelFromMeshes")]
        public static int NativeEngine_ModelDoc_CreateModelFromMeshes( void* pVmdlFileFullPath, void* pMeshes, int nMeshCount )
        {
            throw new NotImplementedException("NativeEngine_ModelDoc_CreateModelFromMeshes is not yet implemented in the linux emulation layer");
        }
    }
    public static unsafe partial class Imports
    {
        public static delegate* unmanaged<void*, void*> _ptr_dtrMdldtr_ModelDoc_Init;
        public static delegate* unmanaged<void*, void*> _ptr_dtrMdldtr_ModelDoc_OnToolsMenu;
        public static void StoreImport_dtrMdldtr_ModelDoc_Init(void* ptr)
        {
            _ptr_dtrMdldtr_ModelDoc_Init = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_dtrMdldtr_ModelDoc_OnToolsMenu(void* ptr)
        {
            _ptr_dtrMdldtr_ModelDoc_OnToolsMenu = (delegate* unmanaged<void*, void*>)ptr;
        }

// Editor.ModelEditor.ModelDoc
[UnmanagedCallersOnly(EntryPoint = "dtrMdldtr_ModelDoc_Init")]
public static void* dtrMdldtr_ModelDoc_Init( void* app )
{
    return (void*)Imports._ptr_dtrMdldtr_ModelDoc_Init( app );
}

[UnmanagedCallersOnly(EntryPoint = "dtrMdldtr_ModelDoc_OnToolsMenu")]
public static void* dtrMdldtr_ModelDoc_OnToolsMenu( void* menu )
{
    return (void*)Imports._ptr_dtrMdldtr_ModelDoc_OnToolsMenu( menu );
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
