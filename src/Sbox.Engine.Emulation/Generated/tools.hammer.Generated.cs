using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;

namespace Sbox.Engine.Emulation.Generated
{
    public static unsafe partial class Exports
    {
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_RefreshEntitiesGameData")]
        public static void* CHammerApp_RefreshEntitiesGameData( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_GetCurrentMaterial")]
        public static void* CHammerApp_GetCurrentMaterial( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_SetCurrentTexture")]
        public static void* CHammerApp_SetCurrentTexture( void* self, void* assetFilename )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_GetActiveMapAsset")]
        public static int CHammerApp_GetActiveMapAsset( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_GetActiveMapDoc")]
        public static void* CHammerApp_GetActiveMapDoc( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_MarkAllViewHudsDirty")]
        public static void* CHammerApp_MarkAllViewHudsDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_SelectObjectsUsingAsset")]
        public static void* CHammerApp_SelectObjectsUsingAsset( void* self, void* assetFilename )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_SelectFacesUsingMaterial")]
        public static void* CHammerApp_SelectFacesUsingMaterial( void* self, void* assetFilename )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_AssignAssetToSelection")]
        public static void* CHammerApp_AssignAssetToSelection( void* self, void* assetFilename )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_ShowEntityReportForAsset")]
        public static void* CHammerApp_ShowEntityReportForAsset( void* self, void* assetFilename )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHammerApp_OnFileReload")]
        public static void* CHammerApp_OnFileReload( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHmmrdtrSssn_ShowLoadingProgressBar")]
        public static void* CHmmrdtrSssn_ShowLoadingProgressBar( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHmmrdtrSssn_HideLoadingProgressBar")]
        public static void* CHmmrdtrSssn_HideLoadingProgressBar( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHmmrdtrSssn_GetMapAsset")]
        public static void* CHmmrdtrSssn_GetMapAsset( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHmmrdtrSssn_GetMapDoc")]
        public static void* CHmmrdtrSssn_GetMapDoc( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHistory_MarkUndoPosition")]
        public static void* CHistory_MarkUndoPosition( void* self, void* pSelection, void* pszName )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHistory_Keep")]
        public static void* CHistory_Keep( void* self, void* pUndoable )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHistory_KeepNew")]
        public static void* CHistory_KeepNew( void* self, void* pUndoable )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CHistory_GetHistory")]
        public static void* CHistory_GetHistory()
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_GetMapWorld")]
        public static void* CMapDoc_GetMapWorld( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_GetPathName")]
        public static void* CMapDoc_GetPathName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_GetSelection")]
        public static void* CMapDoc_GetSelection( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_AddObjectToDocument")]
        public static void* CMapDoc_AddObjectToDocument( void* self, void* pObject, void* pParent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_DeleteObject")]
        public static void* CMapDoc_DeleteObject( void* self, void* node )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateEmptyMesh")]
        public static void* CMapDoc_CreateEmptyMesh( void* self, int addToDocument )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateEntity")]
        public static void* CMapDoc_CreateEntity( void* self, int addToDocument )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateGameObject")]
        public static void* CMapDoc_CreateGameObject( void* self, int addToDocument )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateMapGroup")]
        public static void* CMapDoc_CreateMapGroup( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateMapInstance")]
        public static void* CMapDoc_CreateMapInstance( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateMapPath")]
        public static void* CMapDoc_CreateMapPath( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapDoc_CreateStaticOverlay")]
        public static void* CMapDoc_CreateStaticOverlay( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapEntity")]
        public static void* From_CMapNode_To_CMapEntity( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapEntity")]
        public static void* To_CMapNode_From_CMapEntity( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetClassName")]
        public static void* CMapEntity_GetClassName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetClass")]
        public static void* CMapEntity_SetClass( void* self, void* classname )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetKeyValue")]
        public static void* CMapEntity_SetKeyValue( void* self, void* key, void* value )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetKeyValue")]
        public static void* CMapEntity_GetKeyValue( void* self, void* key )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_TargetNameMatches")]
        public static int CMapEntity_TargetNameMatches( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetDefaultBounds")]
        public static void* CMapEntity_SetDefaultBounds( void* self, void* minBounds, void* maxBounds )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetNodeID")]
        public static int CMapEntity_GetNodeID( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetName")]
        public static void* CMapEntity_GetName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetName")]
        public static void* CMapEntity_SetName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetParent")]
        public static void* CMapEntity_GetParent( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetParent")]
        public static void* CMapEntity_SetParent( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetParentWorld")]
        public static void* CMapEntity_GetParentWorld( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetRootDocument")]
        public static void* CMapEntity_GetRootDocument( void* self, long nDocumentLoaded )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetDescription")]
        public static void* CMapEntity_GetDescription( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetTypeString")]
        public static void* CMapEntity_GetTypeString( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_Copy")]
        public static void* CMapEntity_Copy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_IsVisible")]
        public static int CMapEntity_IsVisible( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetVisible")]
        public static void* CMapEntity_SetVisible( void* self, int bVisible )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_IsSelected")]
        public static int CMapEntity_IsSelected( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetChildCount")]
        public static int CMapEntity_GetChildCount( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetChild")]
        public static void* CMapEntity_GetChild( void* self, int i )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetFirstDescendent")]
        public static void* CMapEntity_GetFirstDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetNextDescendent")]
        public static void* CMapEntity_GetNextDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_AsMapEntity")]
        public static void* CMapEntity_AsMapEntity( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetOrigin")]
        public static void* CMapEntity_GetOrigin( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetAngles")]
        public static void* CMapEntity_GetAngles( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GetScales")]
        public static void* CMapEntity_GetScales( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetOrigin")]
        public static void* CMapEntity_SetOrigin( void* self, void* origin )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetAngles")]
        public static void* CMapEntity_SetAngles( void* self, void* angles )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetScales")]
        public static void* CMapEntity_SetScales( void* self, void* scales )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_BeginTransformOperation")]
        public static void* CMapEntity_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_Transform")]
        public static void* CMapEntity_Transform( void* self, void* matrix, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_EndTransformOperation")]
        public static void* CMapEntity_EndTransformOperation( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_MarkBoundsDirty")]
        public static void* CMapEntity_MarkBoundsDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_FullBoundsUpdate")]
        public static void* CMapEntity_FullBoundsUpdate( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_SetModifiedFlag")]
        public static void* CMapEntity_SetModifiedFlag( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_GeneratesEntityModelGeometry")]
        public static int CMapEntity_GeneratesEntityModelGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_DescriptionChanged")]
        public static void* CMapEntity_DescriptionChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapEntity_CoreAttributeChanged")]
        public static void* CMapEntity_CoreAttributeChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapGameObject")]
        public static void* From_CMapNode_To_CMapGameObject( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapGameObject")]
        public static void* To_CMapNode_From_CMapGameObject( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetGUID")]
        public static void* CMapGameObject_SetGUID( void* self, void* json )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetGUID")]
        public static void* CMapGameObject_GetGUID( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetNodeID")]
        public static int CMapGameObject_GetNodeID( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetName")]
        public static void* CMapGameObject_GetName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetName")]
        public static void* CMapGameObject_SetName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetParent")]
        public static void* CMapGameObject_GetParent( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetParent")]
        public static void* CMapGameObject_SetParent( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetParentWorld")]
        public static void* CMapGameObject_GetParentWorld( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetRootDocument")]
        public static void* CMapGameObject_GetRootDocument( void* self, long nDocumentLoaded )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetDescription")]
        public static void* CMapGameObject_GetDescription( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetTypeString")]
        public static void* CMapGameObject_GetTypeString( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_Copy")]
        public static void* CMapGameObject_Copy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_IsVisible")]
        public static int CMapGameObject_IsVisible( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetVisible")]
        public static void* CMapGameObject_SetVisible( void* self, int bVisible )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_IsSelected")]
        public static int CMapGameObject_IsSelected( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetChildCount")]
        public static int CMapGameObject_GetChildCount( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetChild")]
        public static void* CMapGameObject_GetChild( void* self, int i )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetFirstDescendent")]
        public static void* CMapGameObject_GetFirstDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetNextDescendent")]
        public static void* CMapGameObject_GetNextDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_AsMapEntity")]
        public static void* CMapGameObject_AsMapEntity( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetOrigin")]
        public static void* CMapGameObject_GetOrigin( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetAngles")]
        public static void* CMapGameObject_GetAngles( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GetScales")]
        public static void* CMapGameObject_GetScales( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetOrigin")]
        public static void* CMapGameObject_SetOrigin( void* self, void* origin )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetAngles")]
        public static void* CMapGameObject_SetAngles( void* self, void* angles )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetScales")]
        public static void* CMapGameObject_SetScales( void* self, void* scales )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_BeginTransformOperation")]
        public static void* CMapGameObject_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_Transform")]
        public static void* CMapGameObject_Transform( void* self, void* matrix, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_EndTransformOperation")]
        public static void* CMapGameObject_EndTransformOperation( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_MarkBoundsDirty")]
        public static void* CMapGameObject_MarkBoundsDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_FullBoundsUpdate")]
        public static void* CMapGameObject_FullBoundsUpdate( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_SetModifiedFlag")]
        public static void* CMapGameObject_SetModifiedFlag( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_GeneratesEntityModelGeometry")]
        public static int CMapGameObject_GeneratesEntityModelGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_DescriptionChanged")]
        public static void* CMapGameObject_DescriptionChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGameObject_CoreAttributeChanged")]
        public static void* CMapGameObject_CoreAttributeChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapGroup")]
        public static void* From_CMapNode_To_CMapGroup( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapGroup")]
        public static void* To_CMapNode_From_CMapGroup( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetNodeID")]
        public static int CMapGroup_GetNodeID( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetName")]
        public static void* CMapGroup_GetName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetName")]
        public static void* CMapGroup_SetName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetParent")]
        public static void* CMapGroup_GetParent( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetParent")]
        public static void* CMapGroup_SetParent( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetParentWorld")]
        public static void* CMapGroup_GetParentWorld( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetRootDocument")]
        public static void* CMapGroup_GetRootDocument( void* self, long nDocumentLoaded )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetDescription")]
        public static void* CMapGroup_GetDescription( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetTypeString")]
        public static void* CMapGroup_GetTypeString( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_Copy")]
        public static void* CMapGroup_Copy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_IsVisible")]
        public static int CMapGroup_IsVisible( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetVisible")]
        public static void* CMapGroup_SetVisible( void* self, int bVisible )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_IsSelected")]
        public static int CMapGroup_IsSelected( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetChildCount")]
        public static int CMapGroup_GetChildCount( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetChild")]
        public static void* CMapGroup_GetChild( void* self, int i )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetFirstDescendent")]
        public static void* CMapGroup_GetFirstDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetNextDescendent")]
        public static void* CMapGroup_GetNextDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_AsMapEntity")]
        public static void* CMapGroup_AsMapEntity( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetOrigin")]
        public static void* CMapGroup_GetOrigin( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetAngles")]
        public static void* CMapGroup_GetAngles( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GetScales")]
        public static void* CMapGroup_GetScales( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetOrigin")]
        public static void* CMapGroup_SetOrigin( void* self, void* origin )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetAngles")]
        public static void* CMapGroup_SetAngles( void* self, void* angles )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetScales")]
        public static void* CMapGroup_SetScales( void* self, void* scales )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_BeginTransformOperation")]
        public static void* CMapGroup_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_Transform")]
        public static void* CMapGroup_Transform( void* self, void* matrix, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_EndTransformOperation")]
        public static void* CMapGroup_EndTransformOperation( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_MarkBoundsDirty")]
        public static void* CMapGroup_MarkBoundsDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_FullBoundsUpdate")]
        public static void* CMapGroup_FullBoundsUpdate( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_SetModifiedFlag")]
        public static void* CMapGroup_SetModifiedFlag( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_GeneratesEntityModelGeometry")]
        public static int CMapGroup_GeneratesEntityModelGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_DescriptionChanged")]
        public static void* CMapGroup_DescriptionChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapGroup_CoreAttributeChanged")]
        public static void* CMapGroup_CoreAttributeChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapInstance")]
        public static void* From_CMapNode_To_CMapInstance( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapInstance")]
        public static void* To_CMapNode_From_CMapInstance( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetTarget")]
        public static void* CMapInstance_SetTarget( void* self, void* node )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetTarget")]
        public static void* CMapInstance_GetTarget( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetNodeID")]
        public static int CMapInstance_GetNodeID( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetName")]
        public static void* CMapInstance_GetName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetName")]
        public static void* CMapInstance_SetName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetParent")]
        public static void* CMapInstance_GetParent( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetParent")]
        public static void* CMapInstance_SetParent( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetParentWorld")]
        public static void* CMapInstance_GetParentWorld( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetRootDocument")]
        public static void* CMapInstance_GetRootDocument( void* self, long nDocumentLoaded )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetDescription")]
        public static void* CMapInstance_GetDescription( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetTypeString")]
        public static void* CMapInstance_GetTypeString( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_Copy")]
        public static void* CMapInstance_Copy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_IsVisible")]
        public static int CMapInstance_IsVisible( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetVisible")]
        public static void* CMapInstance_SetVisible( void* self, int bVisible )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_IsSelected")]
        public static int CMapInstance_IsSelected( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetChildCount")]
        public static int CMapInstance_GetChildCount( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetChild")]
        public static void* CMapInstance_GetChild( void* self, int i )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetFirstDescendent")]
        public static void* CMapInstance_GetFirstDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetNextDescendent")]
        public static void* CMapInstance_GetNextDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_AsMapEntity")]
        public static void* CMapInstance_AsMapEntity( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetOrigin")]
        public static void* CMapInstance_GetOrigin( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetAngles")]
        public static void* CMapInstance_GetAngles( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GetScales")]
        public static void* CMapInstance_GetScales( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetOrigin")]
        public static void* CMapInstance_SetOrigin( void* self, void* origin )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetAngles")]
        public static void* CMapInstance_SetAngles( void* self, void* angles )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetScales")]
        public static void* CMapInstance_SetScales( void* self, void* scales )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_BeginTransformOperation")]
        public static void* CMapInstance_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_Transform")]
        public static void* CMapInstance_Transform( void* self, void* matrix, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_EndTransformOperation")]
        public static void* CMapInstance_EndTransformOperation( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_MarkBoundsDirty")]
        public static void* CMapInstance_MarkBoundsDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_FullBoundsUpdate")]
        public static void* CMapInstance_FullBoundsUpdate( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_SetModifiedFlag")]
        public static void* CMapInstance_SetModifiedFlag( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_GeneratesEntityModelGeometry")]
        public static int CMapInstance_GeneratesEntityModelGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_DescriptionChanged")]
        public static void* CMapInstance_DescriptionChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapInstance_CoreAttributeChanged")]
        public static void* CMapInstance_CoreAttributeChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapMesh")]
        public static void* From_CMapNode_To_CMapMesh( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapMesh")]
        public static void* To_CMapNode_From_CMapMesh( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_AssignMaterialToMesh")]
        public static void* CMapMesh_AssignMaterialToMesh( void* self, void* materialName )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_ConstructFromData")]
        public static void* CMapMesh_ConstructFromData( void* self, int numVerticies, void* vertexPositions, void* vertexTexCoords, int numIndices, void* vertexIndices, int numFaces, void* faceVertexCounts, void* faceMaterialsPtr, int mergeVertices, float vertexMergeTolerance )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetFaceMaterials")]
        public static void* CMapMesh_GetFaceMaterials( void* self, void* materials )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetNodeID")]
        public static int CMapMesh_GetNodeID( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetName")]
        public static void* CMapMesh_GetName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetName")]
        public static void* CMapMesh_SetName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetParent")]
        public static void* CMapMesh_GetParent( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetParent")]
        public static void* CMapMesh_SetParent( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetParentWorld")]
        public static void* CMapMesh_GetParentWorld( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetRootDocument")]
        public static void* CMapMesh_GetRootDocument( void* self, long nDocumentLoaded )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetDescription")]
        public static void* CMapMesh_GetDescription( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetTypeString")]
        public static void* CMapMesh_GetTypeString( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_Copy")]
        public static void* CMapMesh_Copy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_IsVisible")]
        public static int CMapMesh_IsVisible( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetVisible")]
        public static void* CMapMesh_SetVisible( void* self, int bVisible )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_IsSelected")]
        public static int CMapMesh_IsSelected( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetChildCount")]
        public static int CMapMesh_GetChildCount( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetChild")]
        public static void* CMapMesh_GetChild( void* self, int i )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetFirstDescendent")]
        public static void* CMapMesh_GetFirstDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetNextDescendent")]
        public static void* CMapMesh_GetNextDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_AsMapEntity")]
        public static void* CMapMesh_AsMapEntity( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetOrigin")]
        public static void* CMapMesh_GetOrigin( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetAngles")]
        public static void* CMapMesh_GetAngles( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GetScales")]
        public static void* CMapMesh_GetScales( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetOrigin")]
        public static void* CMapMesh_SetOrigin( void* self, void* origin )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetAngles")]
        public static void* CMapMesh_SetAngles( void* self, void* angles )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetScales")]
        public static void* CMapMesh_SetScales( void* self, void* scales )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_BeginTransformOperation")]
        public static void* CMapMesh_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_Transform")]
        public static void* CMapMesh_Transform( void* self, void* matrix, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_EndTransformOperation")]
        public static void* CMapMesh_EndTransformOperation( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_MarkBoundsDirty")]
        public static void* CMapMesh_MarkBoundsDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_FullBoundsUpdate")]
        public static void* CMapMesh_FullBoundsUpdate( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_SetModifiedFlag")]
        public static void* CMapMesh_SetModifiedFlag( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_GeneratesEntityModelGeometry")]
        public static int CMapMesh_GeneratesEntityModelGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_DescriptionChanged")]
        public static void* CMapMesh_DescriptionChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapMesh_CoreAttributeChanged")]
        public static void* CMapMesh_CoreAttributeChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetNodeID")]
        public static int CMapNode_GetNodeID( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetName")]
        public static void* CMapNode_GetName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetName")]
        public static void* CMapNode_SetName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetParent")]
        public static void* CMapNode_GetParent( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetParent")]
        public static void* CMapNode_SetParent( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetParentWorld")]
        public static void* CMapNode_GetParentWorld( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetRootDocument")]
        public static void* CMapNode_GetRootDocument( void* self, long nDocumentLoaded )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetDescription")]
        public static void* CMapNode_GetDescription( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetTypeString")]
        public static void* CMapNode_GetTypeString( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_Copy")]
        public static void* CMapNode_Copy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_IsVisible")]
        public static int CMapNode_IsVisible( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetVisible")]
        public static void* CMapNode_SetVisible( void* self, int bVisible )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_IsSelected")]
        public static int CMapNode_IsSelected( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetChildCount")]
        public static int CMapNode_GetChildCount( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetChild")]
        public static void* CMapNode_GetChild( void* self, int i )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetFirstDescendent")]
        public static void* CMapNode_GetFirstDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetNextDescendent")]
        public static void* CMapNode_GetNextDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_AsMapEntity")]
        public static void* CMapNode_AsMapEntity( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetOrigin")]
        public static void* CMapNode_GetOrigin( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetAngles")]
        public static void* CMapNode_GetAngles( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GetScales")]
        public static void* CMapNode_GetScales( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetOrigin")]
        public static void* CMapNode_SetOrigin( void* self, void* origin )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetAngles")]
        public static void* CMapNode_SetAngles( void* self, void* angles )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetScales")]
        public static void* CMapNode_SetScales( void* self, void* scales )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_BeginTransformOperation")]
        public static void* CMapNode_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_Transform")]
        public static void* CMapNode_Transform( void* self, void* matrix, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_EndTransformOperation")]
        public static void* CMapNode_EndTransformOperation( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_MarkBoundsDirty")]
        public static void* CMapNode_MarkBoundsDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_FullBoundsUpdate")]
        public static void* CMapNode_FullBoundsUpdate( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_SetModifiedFlag")]
        public static void* CMapNode_SetModifiedFlag( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_GeneratesEntityModelGeometry")]
        public static int CMapNode_GeneratesEntityModelGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_DescriptionChanged")]
        public static void* CMapNode_DescriptionChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapNode_CoreAttributeChanged")]
        public static void* CMapNode_CoreAttributeChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapEntity_To_CMapPath")]
        public static void* From_CMapEntity_To_CMapPath( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapEntity_From_CMapPath")]
        public static void* To_CMapEntity_From_CMapPath( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapPath")]
        public static void* From_CMapNode_To_CMapPath( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapPath")]
        public static void* To_CMapNode_From_CMapPath( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_AddNewNodeToPath")]
        public static void* CMapPath_AddNewNodeToPath( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetClassName")]
        public static void* CMapPath_GetClassName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetClass")]
        public static void* CMapPath_SetClass( void* self, void* classname )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetKeyValue")]
        public static void* CMapPath_SetKeyValue( void* self, void* key, void* value )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetKeyValue")]
        public static void* CMapPath_GetKeyValue( void* self, void* key )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_TargetNameMatches")]
        public static int CMapPath_TargetNameMatches( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetDefaultBounds")]
        public static void* CMapPath_SetDefaultBounds( void* self, void* minBounds, void* maxBounds )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetNodeID")]
        public static int CMapPath_GetNodeID( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetName")]
        public static void* CMapPath_GetName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetName")]
        public static void* CMapPath_SetName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetParent")]
        public static void* CMapPath_GetParent( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetParent")]
        public static void* CMapPath_SetParent( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetParentWorld")]
        public static void* CMapPath_GetParentWorld( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetRootDocument")]
        public static void* CMapPath_GetRootDocument( void* self, long nDocumentLoaded )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetDescription")]
        public static void* CMapPath_GetDescription( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetTypeString")]
        public static void* CMapPath_GetTypeString( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_Copy")]
        public static void* CMapPath_Copy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_IsVisible")]
        public static int CMapPath_IsVisible( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetVisible")]
        public static void* CMapPath_SetVisible( void* self, int bVisible )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_IsSelected")]
        public static int CMapPath_IsSelected( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetChildCount")]
        public static int CMapPath_GetChildCount( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetChild")]
        public static void* CMapPath_GetChild( void* self, int i )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetFirstDescendent")]
        public static void* CMapPath_GetFirstDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetNextDescendent")]
        public static void* CMapPath_GetNextDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_AsMapEntity")]
        public static void* CMapPath_AsMapEntity( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetOrigin")]
        public static void* CMapPath_GetOrigin( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetAngles")]
        public static void* CMapPath_GetAngles( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GetScales")]
        public static void* CMapPath_GetScales( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetOrigin")]
        public static void* CMapPath_SetOrigin( void* self, void* origin )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetAngles")]
        public static void* CMapPath_SetAngles( void* self, void* angles )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetScales")]
        public static void* CMapPath_SetScales( void* self, void* scales )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_BeginTransformOperation")]
        public static void* CMapPath_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_Transform")]
        public static void* CMapPath_Transform( void* self, void* matrix, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_EndTransformOperation")]
        public static void* CMapPath_EndTransformOperation( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_MarkBoundsDirty")]
        public static void* CMapPath_MarkBoundsDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_FullBoundsUpdate")]
        public static void* CMapPath_FullBoundsUpdate( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_SetModifiedFlag")]
        public static void* CMapPath_SetModifiedFlag( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_GeneratesEntityModelGeometry")]
        public static int CMapPath_GeneratesEntityModelGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_DescriptionChanged")]
        public static void* CMapPath_DescriptionChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPath_CoreAttributeChanged")]
        public static void* CMapPath_CoreAttributeChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapEntity_To_CMapPathNode")]
        public static void* From_CMapEntity_To_CMapPathNode( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapEntity_From_CMapPathNode")]
        public static void* To_CMapEntity_From_CMapPathNode( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapPathNode")]
        public static void* From_CMapNode_To_CMapPathNode( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapPathNode")]
        public static void* To_CMapNode_From_CMapPathNode( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetClassName")]
        public static void* CMapPathNode_GetClassName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetClass")]
        public static void* CMapPathNode_SetClass( void* self, void* classname )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetKeyValue")]
        public static void* CMapPathNode_SetKeyValue( void* self, void* key, void* value )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetKeyValue")]
        public static void* CMapPathNode_GetKeyValue( void* self, void* key )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_TargetNameMatches")]
        public static int CMapPathNode_TargetNameMatches( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetDefaultBounds")]
        public static void* CMapPathNode_SetDefaultBounds( void* self, void* minBounds, void* maxBounds )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetNodeID")]
        public static int CMapPathNode_GetNodeID( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetName")]
        public static void* CMapPathNode_GetName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetName")]
        public static void* CMapPathNode_SetName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetParent")]
        public static void* CMapPathNode_GetParent( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetParent")]
        public static void* CMapPathNode_SetParent( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetParentWorld")]
        public static void* CMapPathNode_GetParentWorld( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetRootDocument")]
        public static void* CMapPathNode_GetRootDocument( void* self, long nDocumentLoaded )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetDescription")]
        public static void* CMapPathNode_GetDescription( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetTypeString")]
        public static void* CMapPathNode_GetTypeString( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_Copy")]
        public static void* CMapPathNode_Copy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_IsVisible")]
        public static int CMapPathNode_IsVisible( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetVisible")]
        public static void* CMapPathNode_SetVisible( void* self, int bVisible )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_IsSelected")]
        public static int CMapPathNode_IsSelected( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetChildCount")]
        public static int CMapPathNode_GetChildCount( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetChild")]
        public static void* CMapPathNode_GetChild( void* self, int i )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetFirstDescendent")]
        public static void* CMapPathNode_GetFirstDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetNextDescendent")]
        public static void* CMapPathNode_GetNextDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_AsMapEntity")]
        public static void* CMapPathNode_AsMapEntity( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetOrigin")]
        public static void* CMapPathNode_GetOrigin( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetAngles")]
        public static void* CMapPathNode_GetAngles( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GetScales")]
        public static void* CMapPathNode_GetScales( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetOrigin")]
        public static void* CMapPathNode_SetOrigin( void* self, void* origin )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetAngles")]
        public static void* CMapPathNode_SetAngles( void* self, void* angles )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetScales")]
        public static void* CMapPathNode_SetScales( void* self, void* scales )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_BeginTransformOperation")]
        public static void* CMapPathNode_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_Transform")]
        public static void* CMapPathNode_Transform( void* self, void* matrix, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_EndTransformOperation")]
        public static void* CMapPathNode_EndTransformOperation( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_MarkBoundsDirty")]
        public static void* CMapPathNode_MarkBoundsDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_FullBoundsUpdate")]
        public static void* CMapPathNode_FullBoundsUpdate( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_SetModifiedFlag")]
        public static void* CMapPathNode_SetModifiedFlag( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_GeneratesEntityModelGeometry")]
        public static int CMapPathNode_GeneratesEntityModelGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_DescriptionChanged")]
        public static void* CMapPathNode_DescriptionChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapPathNode_CoreAttributeChanged")]
        public static void* CMapPathNode_CoreAttributeChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapMesh_To_CMapStaticOverlay")]
        public static void* From_CMapMesh_To_CMapStaticOverlay( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapMesh_From_CMapStaticOverlay")]
        public static void* To_CMapMesh_From_CMapStaticOverlay( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapStaticOverlay")]
        public static void* From_CMapNode_To_CMapStaticOverlay( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapStaticOverlay")]
        public static void* To_CMapNode_From_CMapStaticOverlay( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_CreateCenteredQuad")]
        public static void* CMpSttcvrly_CreateCenteredQuad( void* self, float flWidth, float flHeight, void* pMaterialName )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_AssignMaterialToMesh")]
        public static void* CMpSttcvrly_AssignMaterialToMesh( void* self, void* materialName )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_ConstructFromData")]
        public static void* CMpSttcvrly_ConstructFromData( void* self, int numVerticies, void* vertexPositions, void* vertexTexCoords, int numIndices, void* vertexIndices, int numFaces, void* faceVertexCounts, void* faceMaterialsPtr, int mergeVertices, float vertexMergeTolerance )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetFaceMaterials")]
        public static void* CMpSttcvrly_GetFaceMaterials( void* self, void* materials )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetNodeID")]
        public static int CMpSttcvrly_GetNodeID( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetName")]
        public static void* CMpSttcvrly_GetName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetName")]
        public static void* CMpSttcvrly_SetName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetParent")]
        public static void* CMpSttcvrly_GetParent( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetParent")]
        public static void* CMpSttcvrly_SetParent( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetParentWorld")]
        public static void* CMpSttcvrly_GetParentWorld( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetRootDocument")]
        public static void* CMpSttcvrly_GetRootDocument( void* self, long nDocumentLoaded )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetDescription")]
        public static void* CMpSttcvrly_GetDescription( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetTypeString")]
        public static void* CMpSttcvrly_GetTypeString( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_Copy")]
        public static void* CMpSttcvrly_Copy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_IsVisible")]
        public static int CMpSttcvrly_IsVisible( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetVisible")]
        public static void* CMpSttcvrly_SetVisible( void* self, int bVisible )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_IsSelected")]
        public static int CMpSttcvrly_IsSelected( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetChildCount")]
        public static int CMpSttcvrly_GetChildCount( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetChild")]
        public static void* CMpSttcvrly_GetChild( void* self, int i )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetFirstDescendent")]
        public static void* CMpSttcvrly_GetFirstDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetNextDescendent")]
        public static void* CMpSttcvrly_GetNextDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_AsMapEntity")]
        public static void* CMpSttcvrly_AsMapEntity( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetOrigin")]
        public static void* CMpSttcvrly_GetOrigin( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetAngles")]
        public static void* CMpSttcvrly_GetAngles( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GetScales")]
        public static void* CMpSttcvrly_GetScales( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetOrigin")]
        public static void* CMpSttcvrly_SetOrigin( void* self, void* origin )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetAngles")]
        public static void* CMpSttcvrly_SetAngles( void* self, void* angles )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetScales")]
        public static void* CMpSttcvrly_SetScales( void* self, void* scales )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_BeginTransformOperation")]
        public static void* CMpSttcvrly_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_Transform")]
        public static void* CMpSttcvrly_Transform( void* self, void* matrix, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_EndTransformOperation")]
        public static void* CMpSttcvrly_EndTransformOperation( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_MarkBoundsDirty")]
        public static void* CMpSttcvrly_MarkBoundsDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_FullBoundsUpdate")]
        public static void* CMpSttcvrly_FullBoundsUpdate( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_SetModifiedFlag")]
        public static void* CMpSttcvrly_SetModifiedFlag( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_GeneratesEntityModelGeometry")]
        public static int CMpSttcvrly_GeneratesEntityModelGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_DescriptionChanged")]
        public static void* CMpSttcvrly_DescriptionChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMpSttcvrly_CoreAttributeChanged")]
        public static void* CMpSttcvrly_CoreAttributeChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_GetMapDoc")]
        public static void* CMapView_GetMapDoc( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_MarkHudDirty")]
        public static void* CMapView_MarkHudDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_GetCamera")]
        public static void* CMapView_GetCamera( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_IsActive")]
        public static int CMapView_IsActive( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_GetMousePosition")]
        public static void* CMapView_GetMousePosition( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_EnterFreeDragMode")]
        public static void* CMapView_EnterFreeDragMode( void* self, void* vClientPos, void* pObjectToSelect, void* vecInitialHitNormal, int bAlignToSurface )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_UpdateFreeDragMode")]
        public static void* CMapView_UpdateFreeDragMode( void* self, void* vClientPos, int bModifyAlignToSurface )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_ExitFreeDragMode")]
        public static void* CMapView_ExitFreeDragMode( void* self, int bRestoreToolState )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_GetDropTarget")]
        public static void* CMapView_GetDropTarget( void* self, void* pVecNormalOut, void* pVecDropPointOut, void* vecPoint2D )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_UpdateManagedGizmoState")]
        public static void* CMapView_UpdateManagedGizmoState( void* self, int isHoveredOverSomething, float distanceFromCamera )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_GetManipulationMode")]
        public static long CMapView_GetManipulationMode( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapView_HitDistanceAtMouse")]
        public static float CMapView_HitDistanceAtMouse( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CMapNode_To_CMapWorld")]
        public static void* From_CMapNode_To_CMapWorld( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CMapNode_From_CMapWorld")]
        public static void* To_CMapNode_From_CMapWorld( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetSerializedScene")]
        public static void* CMapWorld_SetSerializedScene( void* self, void* json )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetSerializedScene")]
        public static void* CMapWorld_GetSerializedScene( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_FindNodeByID")]
        public static void* CMapWorld_FindNodeByID( void* self, int nodeId )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_FindEntityByNodeId")]
        public static void* CMapWorld_FindEntityByNodeId( void* self, int nodeId )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_Trace")]
        public static void* CMapWorld_Trace( void* self, void* traceRequest )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetNodeID")]
        public static int CMapWorld_GetNodeID( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetName")]
        public static void* CMapWorld_GetName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetName")]
        public static void* CMapWorld_SetName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetParent")]
        public static void* CMapWorld_GetParent( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetParent")]
        public static void* CMapWorld_SetParent( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetParentWorld")]
        public static void* CMapWorld_GetParentWorld( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetRootDocument")]
        public static void* CMapWorld_GetRootDocument( void* self, long nDocumentLoaded )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetDescription")]
        public static void* CMapWorld_GetDescription( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetTypeString")]
        public static void* CMapWorld_GetTypeString( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_Copy")]
        public static void* CMapWorld_Copy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_IsVisible")]
        public static int CMapWorld_IsVisible( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetVisible")]
        public static void* CMapWorld_SetVisible( void* self, int bVisible )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_IsSelected")]
        public static int CMapWorld_IsSelected( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetChildCount")]
        public static int CMapWorld_GetChildCount( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetChild")]
        public static void* CMapWorld_GetChild( void* self, int i )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetFirstDescendent")]
        public static void* CMapWorld_GetFirstDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetNextDescendent")]
        public static void* CMapWorld_GetNextDescendent( void* self, void* pos )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_AsMapEntity")]
        public static void* CMapWorld_AsMapEntity( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetOrigin")]
        public static void* CMapWorld_GetOrigin( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetAngles")]
        public static void* CMapWorld_GetAngles( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GetScales")]
        public static void* CMapWorld_GetScales( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetOrigin")]
        public static void* CMapWorld_SetOrigin( void* self, void* origin )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetAngles")]
        public static void* CMapWorld_SetAngles( void* self, void* angles )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetScales")]
        public static void* CMapWorld_SetScales( void* self, void* scales )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_BeginTransformOperation")]
        public static void* CMapWorld_BeginTransformOperation( void* self, long nTransformType, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_Transform")]
        public static void* CMapWorld_Transform( void* self, void* matrix, long nFlags )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_EndTransformOperation")]
        public static void* CMapWorld_EndTransformOperation( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_MarkBoundsDirty")]
        public static void* CMapWorld_MarkBoundsDirty( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_FullBoundsUpdate")]
        public static void* CMapWorld_FullBoundsUpdate( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_SetModifiedFlag")]
        public static void* CMapWorld_SetModifiedFlag( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_GeneratesEntityModelGeometry")]
        public static int CMapWorld_GeneratesEntityModelGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_DescriptionChanged")]
        public static void* CMapWorld_DescriptionChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CMapWorld_CoreAttributeChanged")]
        public static void* CMapWorld_CoreAttributeChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "From_CFramelessMainWindow_To_CQHammerMainWnd")]
        public static void* From_CFramelessMainWindow_To_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_CFramelessMainWindow_From_CQHammerMainWnd")]
        public static void* To_CFramelessMainWindow_From_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_QMainWindow_To_CQHammerMainWnd")]
        public static void* From_QMainWindow_To_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_QMainWindow_From_CQHammerMainWnd")]
        public static void* To_QMainWindow_From_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_QWidget_To_CQHammerMainWnd")]
        public static void* From_QWidget_To_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_QWidget_From_CQHammerMainWnd")]
        public static void* To_QWidget_From_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "From_QObject_To_CQHammerMainWnd")]
        public static void* From_QObject_To_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "To_QObject_From_CQHammerMainWnd")]
        public static void* To_QObject_From_CQHammerMainWnd( void* ptr ) => ptr;
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_CreateEverything")]
        public static void* CQHammerMainWnd_CreateEverything( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_CreateMenus")]
        public static void* CQHammerMainWnd_CreateMenus( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_SetupDefaultLayout")]
        public static void* CQHammerMainWnd_SetupDefaultLayout( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_SetTitleBarWidgets")]
        public static void* CQHammerMainWnd_SetTitleBarWidgets( void* self, void* icon, void* titleLabel, void* menuBar, void* grabber, void* minimizeButton, void* maximizeButton, void* closeButton )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_iconSize")]
        public static void* CQHammerMainWnd_iconSize( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setIconSize")]
        public static void* CQHammerMainWnd_setIconSize( void* self, void* iconSize )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_menuBar")]
        public static void* CQHammerMainWnd_menuBar( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setMenuBar")]
        public static void* CQHammerMainWnd_setMenuBar( void* self, void* menubar )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setMenuWidget")]
        public static void* CQHammerMainWnd_setMenuWidget( void* self, void* menubar )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_statusBar")]
        public static void* CQHammerMainWnd_statusBar( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setStatusBar")]
        public static void* CQHammerMainWnd_setStatusBar( void* self, void* statusbar )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_centralWidget")]
        public static void* CQHammerMainWnd_centralWidget( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setCentralWidget")]
        public static void* CQHammerMainWnd_setCentralWidget( void* self, void* widget )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isAnimated")]
        public static int CQHammerMainWnd_isAnimated( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setAnimated")]
        public static void* CQHammerMainWnd_setAnimated( void* self, int enabled )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_addToolBarBreak")]
        public static void* CQHammerMainWnd_addToolBarBreak( void* self, long area )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_insertToolBarBreak")]
        public static void* CQHammerMainWnd_insertToolBarBreak( void* self, void* before )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_addToolBar")]
        public static void* CQHammerMainWnd_addToolBar( void* self, long area, void* toolbar )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_addToolBar_1")]
        public static void* CQHammerMainWnd_addToolBar_1( void* self, void* toolbar )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_insertToolBar")]
        public static void* CQHammerMainWnd_insertToolBar( void* self, void* before, void* toolbar )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_removeToolBar")]
        public static void* CQHammerMainWnd_removeToolBar( void* self, void* toolbar )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_removeToolBarBreak")]
        public static void* CQHammerMainWnd_removeToolBarBreak( void* self, void* before )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_saveState")]
        public static void* CQHammerMainWnd_saveState( void* self, int version )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_restoreState")]
        public static int CQHammerMainWnd_restoreState( void* self, void* state )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isTopLevel")]
        public static int CQHammerMainWnd_isTopLevel( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isWindow")]
        public static int CQHammerMainWnd_isWindow( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isModal")]
        public static int CQHammerMainWnd_isModal( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setStyleSheet")]
        public static void* CQHammerMainWnd_setStyleSheet( void* self, void* sheet )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_windowTitle")]
        public static void* CQHammerMainWnd_windowTitle( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowTitle")]
        public static void* CQHammerMainWnd_setWindowTitle( void* self, void* title )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowFlags")]
        public static void* CQHammerMainWnd_setWindowFlags( void* self, long type )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_windowFlags")]
        public static long CQHammerMainWnd_windowFlags( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_size")]
        public static void* CQHammerMainWnd_size( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_resize")]
        public static void* CQHammerMainWnd_resize( void* self, void* x )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_minimumSize")]
        public static void* CQHammerMainWnd_minimumSize( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setMinimumSize")]
        public static void* CQHammerMainWnd_setMinimumSize( void* self, void* x )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_maximumSize")]
        public static void* CQHammerMainWnd_maximumSize( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setMaximumSize")]
        public static void* CQHammerMainWnd_setMaximumSize( void* self, void* x )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_sizeHint")]
        public static void* CQHammerMainWnd_sizeHint( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_pos")]
        public static void* CQHammerMainWnd_pos( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_move")]
        public static void* CQHammerMainWnd_move( void* self, void* x )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isEnabled")]
        public static int CQHammerMainWnd_isEnabled( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setEnabled")]
        public static void* CQHammerMainWnd_setEnabled( void* self, int x )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setVisible")]
        public static void* CQHammerMainWnd_setVisible( void* self, int visible )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_show")]
        public static void* CQHammerMainWnd_show( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_hide")]
        public static void* CQHammerMainWnd_hide( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_showMinimized")]
        public static void* CQHammerMainWnd_showMinimized( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_showMaximized")]
        public static void* CQHammerMainWnd_showMaximized( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_showFullScreen")]
        public static void* CQHammerMainWnd_showFullScreen( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_showNormal")]
        public static void* CQHammerMainWnd_showNormal( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_close")]
        public static int CQHammerMainWnd_close( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_raise")]
        public static void* CQHammerMainWnd_raise( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_lower")]
        public static void* CQHammerMainWnd_lower( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isVisible")]
        public static int CQHammerMainWnd_isVisible( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isHidden")]
        public static int CQHammerMainWnd_isHidden( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setHidden")]
        public static void* CQHammerMainWnd_setHidden( void* self, int b )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setAttribute")]
        public static void* CQHammerMainWnd_setAttribute( void* self, long a, int on )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_testAttribute")]
        public static int CQHammerMainWnd_testAttribute( void* self, long a )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_acceptDrops")]
        public static int CQHammerMainWnd_acceptDrops( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setAcceptDrops")]
        public static void* CQHammerMainWnd_setAcceptDrops( void* self, int on )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_updateGeometry")]
        public static void* CQHammerMainWnd_updateGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_update")]
        public static void* CQHammerMainWnd_update( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_repaint")]
        public static void* CQHammerMainWnd_repaint( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setCursor")]
        public static void* CQHammerMainWnd_setCursor( void* self, long shape )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setCursor_1")]
        public static void* CQHammerMainWnd_setCursor_1( void* self, void* pixmap )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_unsetCursor")]
        public static void* CQHammerMainWnd_unsetCursor( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowIcon")]
        public static void* CQHammerMainWnd_setWindowIcon( void* self, void* icon )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowIconFromPixmap")]
        public static void* CQHammerMainWnd_setWindowIconFromPixmap( void* self, void* icon )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowIconText")]
        public static void* CQHammerMainWnd_setWindowIconText( void* self, void* str )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowOpacity")]
        public static void* CQHammerMainWnd_setWindowOpacity( void* self, float level )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_windowOpacity")]
        public static float CQHammerMainWnd_windowOpacity( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isMinimized")]
        public static int CQHammerMainWnd_isMinimized( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isMaximized")]
        public static int CQHammerMainWnd_isMaximized( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isFullScreen")]
        public static int CQHammerMainWnd_isFullScreen( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setMouseTracking")]
        public static void* CQHammerMainWnd_setMouseTracking( void* self, int enable )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_hasMouseTracking")]
        public static int CQHammerMainWnd_hasMouseTracking( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_underMouse")]
        public static int CQHammerMainWnd_underMouse( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_mapToGlobal")]
        public static void* CQHammerMainWnd_mapToGlobal( void* self, void* p )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_mapFromGlobal")]
        public static void* CQHammerMainWnd_mapFromGlobal( void* self, void* p )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_hasFocus")]
        public static int CQHammerMainWnd_hasFocus( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_focusPolicy")]
        public static long CQHammerMainWnd_focusPolicy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFocusPolicy")]
        public static void* CQHammerMainWnd_setFocusPolicy( void* self, long policy )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFocusProxy")]
        public static void* CQHammerMainWnd_setFocusProxy( void* self, void* widget )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_isActiveWindow")]
        public static int CQHammerMainWnd_isActiveWindow( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_updatesEnabled")]
        public static int CQHammerMainWnd_updatesEnabled( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setUpdatesEnabled")]
        public static void* CQHammerMainWnd_setUpdatesEnabled( void* self, int enable )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFocus")]
        public static void* CQHammerMainWnd_setFocus( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_activateWindow")]
        public static void* CQHammerMainWnd_activateWindow( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_clearFocus")]
        public static void* CQHammerMainWnd_clearFocus( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_devicePixelRatioF")]
        public static float CQHammerMainWnd_devicePixelRatioF( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_saveGeometry")]
        public static void* CQHammerMainWnd_saveGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_restoreGeometry")]
        public static int CQHammerMainWnd_restoreGeometry( void* self, void* state )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_addAction")]
        public static void* CQHammerMainWnd_addAction( void* self, void* action )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_removeAction")]
        public static void* CQHammerMainWnd_removeAction( void* self, void* action )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setParent")]
        public static void* CQHammerMainWnd_setParent( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_parentWidget")]
        public static void* CQHammerMainWnd_parentWidget( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_window")]
        public static void* CQHammerMainWnd_window( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_AddClassName")]
        public static void* CQHammerMainWnd_AddClassName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_Polish")]
        public static void* CQHammerMainWnd_Polish( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_toolTip")]
        public static void* CQHammerMainWnd_toolTip( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setToolTip")]
        public static void* CQHammerMainWnd_setToolTip( void* self, void* str )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_statusTip")]
        public static void* CQHammerMainWnd_statusTip( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setStatusTip")]
        public static void* CQHammerMainWnd_setStatusTip( void* self, void* str )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_toolTipDuration")]
        public static int CQHammerMainWnd_toolTipDuration( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setToolTipDuration")]
        public static void* CQHammerMainWnd_setToolTipDuration( void* self, int x )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_autoFillBackground")]
        public static int CQHammerMainWnd_autoFillBackground( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setAutoFillBackground")]
        public static void* CQHammerMainWnd_setAutoFillBackground( void* self, int enabled )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_adjustSize")]
        public static void* CQHammerMainWnd_adjustSize( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_windowModality")]
        public static long CQHammerMainWnd_windowModality( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setWindowModality")]
        public static void* CQHammerMainWnd_setWindowModality( void* self, long windowModality )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_ScreenGeometry")]
        public static void* CQHammerMainWnd_ScreenGeometry( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setContentsMargins")]
        public static void* CQHammerMainWnd_setContentsMargins( void* self, int left, int top, int right, int bottom )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_contentsMargins")]
        public static void* CQHammerMainWnd_contentsMargins( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_layout")]
        public static void* CQHammerMainWnd_layout( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setLayout")]
        public static void* CQHammerMainWnd_setLayout( void* self, void* l )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_contentsRect")]
        public static void* CQHammerMainWnd_contentsRect( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_SetEffectOpacity")]
        public static void* CQHammerMainWnd_SetEffectOpacity( void* self, float opacity )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setSizePolicy")]
        public static void* CQHammerMainWnd_setSizePolicy( void* self, long x, long y )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_GetHorizontalSizePolicy")]
        public static long CQHammerMainWnd_GetHorizontalSizePolicy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_GetVerticalSizePolicy")]
        public static long CQHammerMainWnd_GetVerticalSizePolicy( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_SetHorizontalSizePolicy")]
        public static void* CQHammerMainWnd_SetHorizontalSizePolicy( void* self, long mode )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_SetVerticalSizePolicy")]
        public static void* CQHammerMainWnd_SetVerticalSizePolicy( void* self, long mode )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFixedSize")]
        public static void* CQHammerMainWnd_setFixedSize( void* self, int w, int h )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFixedWidth")]
        public static void* CQHammerMainWnd_setFixedWidth( void* self, int w )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setFixedHeight")]
        public static void* CQHammerMainWnd_setFixedHeight( void* self, int h )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_winId")]
        public static void* CQHammerMainWnd_winId( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_deleteLater")]
        public static void* CQHammerMainWnd_deleteLater( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_objectName")]
        public static void* CQHammerMainWnd_objectName( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setObjectName")]
        public static void* CQHammerMainWnd_setObjectName( void* self, void* name )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setParent_1")]
        public static void* CQHammerMainWnd_setParent_1( void* self, void* parent )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setProperty")]
        public static void* CQHammerMainWnd_setProperty( void* self, void* key, int value )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setProperty_1")]
        public static void* CQHammerMainWnd_setProperty_1( void* self, void* key, float value )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CQHammerMainWnd_setProperty_2")]
        public static void* CQHammerMainWnd_setProperty_2( void* self, void* key, void* value )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_DeleteThis")]
        public static void* CSavedObjects_DeleteThis( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_Create")]
        public static void* CSavedObjects_Create()
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_RemoveAll")]
        public static void* CSavedObjects_RemoveAll( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_SaveObject")]
        public static void* CSavedObjects_SaveObject( void* self, void* pObject )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_RestoreObjects")]
        public static void* CSavedObjects_RestoreObjects( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSavedObjects_Count")]
        public static int CSavedObjects_Count( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_GetMode")]
        public static long CSelection_GetMode( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_SetMode")]
        public static void* CSelection_SetMode( void* self, long mode, long method )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_ActiveSelectionSet")]
        public static void* CSelection_ActiveSelectionSet( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_GetSelectionSetForMode")]
        public static void* CSelection_GetSelectionSetForMode( void* self, long nMode )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_GetNumSelectionSets")]
        public static int CSelection_GetNumSelectionSets( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CSelection_GetSelectionSet")]
        public static void* CSelection_GetSelectionSet( void* self, int nIndex )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlock_OnObjectTypeChanged")]
        public static void* CToolBlock_OnObjectTypeChanged( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlock_SetPrimitiveType2D")]
        public static void* CToolBlock_SetPrimitiveType2D( void* self, int primitiveIs2D )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlock_GetOrientPrimitives")]
        public static int CToolBlock_GetOrientPrimitives( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlock_SetOrientPrimitives")]
        public static void* CToolBlock_SetOrientPrimitives( void* self, int bOrientPrimitives )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "_Get__CToolBlock_m_OverrideMaterial")]
        public static void* _Get__CToolBlock_m_OverrideMaterial( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "_Set__CToolBlock_m_OverrideMaterial")]
        public static void _Set__CToolBlock_m_OverrideMaterial( void* self, void* value )
        {
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlockState_GetAABBBounds")]
        public static void* CToolBlockState_GetAABBBounds( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolBlockState_GetDragWorkPlane")]
        public static void* CToolBlockState_GetDragWorkPlane( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_GetOrigin")]
        public static void* CToolCamera_GetOrigin( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_GetAngles")]
        public static void* CToolCamera_GetAngles( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_SetOrigin")]
        public static void* CToolCamera_SetOrigin( void* self, void* origin )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_SetAngles")]
        public static void* CToolCamera_SetAngles( void* self, void* angles )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_GetWidth")]
        public static float CToolCamera_GetWidth( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_GetHeight")]
        public static float CToolCamera_GetHeight( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_GetCameraFOV")]
        public static float CToolCamera_GetCameraFOV( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolCamera_BuildRay")]
        public static void* CToolCamera_BuildRay( void* self, void* view, void* start, void* end )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CToolEntity_StartBlockEntityCreation")]
        public static void* CToolEntity_StartBlockEntityCreation( void* self, void* className )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "CWorkPlane_GetWorkPlaneToWorldTransform")]
        public static void* CWorkPlane_GetWorkPlaneToWorldTransform( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "global_MaterialGetMappingWidth")]
        public static int global_MaterialGetMappingWidth( void* material )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "global_MaterialGetMappingHeight")]
        public static int global_MaterialGetMappingHeight( void* material )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_SelectObject")]
        public static int ISelectionSet_SelectObject( void* self, void* pObject, long selectOp )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_GetSelectedObject")]
        public static void* ISelectionSet_GetSelectedObject( void* self, int nIndex )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_Count")]
        public static int ISelectionSet_Count( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_RemoveAll")]
        public static void* ISelectionSet_RemoveAll( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_SelectAll")]
        public static void* ISelectionSet_SelectAll( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_InvertSelection")]
        public static void* ISelectionSet_InvertSelection( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_GetPivotPosition")]
        public static void* ISelectionSet_GetPivotPosition( void* self )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "ISelectionSet_SetPivot")]
        public static void* ISelectionSet_SetPivot( void* self, void* pivot )
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "NativeHammer_Options_GetShowHelpers")]
        public static int NativeHammer_Options_GetShowHelpers()
        {
            return default;
        }
        [UnmanagedCallersOnly(EntryPoint = "NativeHammer_Options_GetShowGameObjectsOnly")]
        public static int NativeHammer_Options_GetShowGameObjectsOnly()
        {
            return default;
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
