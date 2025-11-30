using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;

namespace Sbox.Engine.Emulation.Generated
{
    public static unsafe partial class Exports
    {
        [UnmanagedCallersOnly(EntryPoint = "CQnmGrphPrvwDckW_GetPreviewModel")]
        public static void* CQnmGrphPrvwDckW_GetPreviewModel( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQnmGrphPrvwDckW_SetPreviewModel")]
        public static void* CQnmGrphPrvwDckW_SetPreviewModel( void* self, void* model )
        {
            return default;
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
