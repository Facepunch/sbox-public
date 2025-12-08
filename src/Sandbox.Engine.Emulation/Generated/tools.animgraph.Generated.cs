using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;

namespace Sandbox.Engine.Emulation.Generated
{
    public static unsafe partial class Exports
    {
	public static unsafe void FillNativeFunctionsAnimgraph(void** managedFunctions, void** nativeFunctions, int* structSizes)
 	{
 		var i = 0;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged<IntPtr, void>)&Sandbox.Engine.Emulation.EngineExports.DebugError;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CQnmGrphPrvwDckW_GetPreviewModel;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&CQnmGrphPrvwDckW_SetPreviewModel;
 	}

        [UnmanagedCallersOnly(EntryPoint = "CQnmGrphPrvwDckW_GetPreviewModel")]
        public static void* CQnmGrphPrvwDckW_GetPreviewModel( void* self )
        {
            throw new NotImplementedException("CQnmGrphPrvwDckW_GetPreviewModel is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CQnmGrphPrvwDckW_SetPreviewModel")]
        public static void* CQnmGrphPrvwDckW_SetPreviewModel( void* self, void* model )
        {
            throw new NotImplementedException("CQnmGrphPrvwDckW_SetPreviewModel is not yet implemented in the linux emulation layer");
        }
    }
    public static unsafe partial class Imports
    {
        public static delegate* unmanaged<void*, void*> _ptr_Editor_Animgraph_CreateModelPicker;
        public static void StoreImport_Editor_Animgraph_CreateModelPicker(void* ptr)
        {
            _ptr_Editor_Animgraph_CreateModelPicker = (delegate* unmanaged<void*, void*>)ptr;
        }

// Editor.Animgraph
[UnmanagedCallersOnly(EntryPoint = "Editor_Animgraph_CreateModelPicker")]
public static void* Editor_Animgraph_CreateModelPicker( void* widget )
{
    return (void*)Imports._ptr_Editor_Animgraph_CreateModelPicker( widget );
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
