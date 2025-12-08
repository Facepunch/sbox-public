using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;

namespace Sandbox.Engine.Emulation.Generated
{
    public static unsafe partial class Exports
    {
	public static unsafe void FillNativeFunctionsAssetsystem(void** managedFunctions, void** nativeFunctions, int* structSizes)
 	{
 		var i = 0;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged<IntPtr, void>)&Sandbox.Engine.Emulation.EngineExports.DebugError;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&sstPckdWrppr_DeleteThis;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void* >)&sstPckdWrppr_Create;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&sstPckdWrppr_AddAsset;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&CResourceStream_WriteBytes;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int, void* >)&CResourceStream_Align;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CResourceStream_AlignPointer;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, uint >)&CResourceStream_Tell;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CtlVctrsst_DeleteThis;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< int, int, void* >)&CtlVctrsst_Create;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CtlVctrsst_Count;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CtlVctrsst_Element;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&CtlVctrsstTyp_DeleteThis;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< int, int, void* >)&CtlVctrsstTyp_Create;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&CtlVctrsstTyp_Count;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&CtlVctrsstTyp_Element;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&g_psstPrvwSystm_RenderAssetThumbnail;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&g_psstPrvwSystm_OnThumbnailGenerated;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&g_psstPrvwSystm_GetModelForAsset;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&g_pAssetSystem_RecordAssetOpen;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&g_pAssetSystem_RecordAssetOpen_1;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&g_pAssetSystem_FindAssetByFilename;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&g_pAssetSystem_FindAssetByAssetRelativePath;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&g_pAssetSystem_RegisterAssetFile;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void* >)&g_pAssetSystem_RunFrame;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void* >)&g_pAssetSystem_UpdateMods;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< int >)&g_pAssetSystem_GetAllModsCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, int >)&g_pAssetSystem_RecompileAsset;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&g_pAssetSystem_UpdateGameResourceType;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void* >)&g_pAssetSystem_LoadWorkingSetsAndTags;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAsset_GetFriendlyName_Transient;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void* >)&IAsset_GetRelativePath_Transient;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void* >)&IAsset_GetAbsolutePath_Transient;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_HasAnyFiles;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_IsCached;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_CanReload;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_CanRecompile;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, uint >)&IAsset_GetAssetIndexInt;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_OpenInTool;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&IAsset_OpenInSecondaryTool;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&IAsset_GetAssetsIDependOn;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&IAsset_GetAssetsIParent;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&IAsset_GetAssetsIReference;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&IAsset_GetAssetsDependingOnMe;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&IAsset_GetAssetsReferencingMe;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&IAsset_GetAssetsParentingMe;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_AdditionalRelatedFileCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_AdditionalInputDependencyCount;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&IAsset_GetAdditionalRelatedFile_Transient;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&IAsset_GetAdditionalInputDependency_Transient;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, long, void*, void* >)&IAsset_GetUnrecognizedRelatedPaths;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAsset_RequireDependencyInfo_Virtual;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_NeedAnyDependencyUpdate_Virtual;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_IsTrivialChildAsset;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_HasHiddenAssetFlag;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_GetAssetTypeId;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&IAsset_CacheAsset;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAsset_UncacheAsset;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_HasSourceFile;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_HasCompiledFile;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_IsCompiled;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_IsCompiledAndUpToDate;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_IsCompileFailed;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAsset_CompileIfNeeded;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&IAsset_FindIntEditInfo;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&IAsset_FindStringEditInfo;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAsset_GetCompileStateReason_Transient;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&IAsset_SetInMemoryReplacement;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAsset_DiscardInMemoryReplacement;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&sstPckrLstnr_NotifyAssetPicked;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAssetType_GetFriendlyName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAssetType_GetIconLg;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAssetType_GetIconSm;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAssetType_GetPrimaryExtension;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAssetType_GetColor;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAssetType_GetCategory;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAssetType_HideTypeByDefault;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAssetType_IgnoreCompiledState;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAssetType_IsContentFileText;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAssetType_IsSimpleAsset;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAssetType_HasDependencies;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&IAssetType_PrefersIconForThumbnail;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&IAssetType_GetAssetTypeName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&IAssetType_GetAdditionalExtensions;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&RsrcCmplrCntxt_SetExtension;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&RsrcCmplrCntxt_SetCompiler;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&RsrcCmplrCntxt_FullPath;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&RsrcCmplrCntxt_RelativePath;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&RsrcCmplrCntxt_ResourceName;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int, void* >)&RsrcCmplrCntxt_SpecifyResourceVersion;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, uint, uint, void* >)&RsrcCmplrCntxt_RegisterSpecialDependency;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int >)&RsrcCmplrCntxt_RegisterReference;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, int, void* >)&RsrcCmplrCntxt_RegisterInputFileDependency;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void*, int, int >)&RsrcCmplrCntxt_WriteBlock;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&RsrcCmplrCntxt_GetOverrideData;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&RsrcCmplrCntxt_Data;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void* >)&RsrcCmplrCntxt_StreamingData;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&RsrcCmplrCntxt_CreateChildContext;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, void*, void* >)&RsrcCmplrCntxtCh_SetOverrideInputData;
 		nativeFunctions[i++] = (void*)(delegate* unmanaged< void*, int >)&RsrcCmplrCntxtCh_CompileImmediately;
 	}

        [UnmanagedCallersOnly(EntryPoint = "sstPckdWrppr_DeleteThis")]
        public static void* sstPckdWrppr_DeleteThis( void* self )
        {
            throw new NotImplementedException("sstPckdWrppr_DeleteThis is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "sstPckdWrppr_Create")]
        public static void* sstPckdWrppr_Create()
        {
            throw new NotImplementedException("sstPckdWrppr_Create is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "sstPckdWrppr_AddAsset")]
        public static void* sstPckdWrppr_AddAsset( void* self, void* asset )
        {
            throw new NotImplementedException("sstPckdWrppr_AddAsset is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CResourceStream_WriteBytes")]
        public static void* CResourceStream_WriteBytes( void* self, void* data, int size )
        {
            throw new NotImplementedException("CResourceStream_WriteBytes is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CResourceStream_Align")]
        public static void* CResourceStream_Align( void* self, int alignment, int offset )
        {
            throw new NotImplementedException("CResourceStream_Align is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CResourceStream_AlignPointer")]
        public static void* CResourceStream_AlignPointer( void* self )
        {
            throw new NotImplementedException("CResourceStream_AlignPointer is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CResourceStream_Tell")]
        public static uint CResourceStream_Tell( void* self )
        {
            throw new NotImplementedException("CResourceStream_Tell is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CtlVctrsst_DeleteThis")]
        public static void* CtlVctrsst_DeleteThis( void* self )
        {
            throw new NotImplementedException("CtlVctrsst_DeleteThis is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CtlVctrsst_Create")]
        public static void* CtlVctrsst_Create( int growsize, int initialcapacity )
        {
            throw new NotImplementedException("CtlVctrsst_Create is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CtlVctrsst_Count")]
        public static int CtlVctrsst_Count( void* self )
        {
            throw new NotImplementedException("CtlVctrsst_Count is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CtlVctrsst_Element")]
        public static void* CtlVctrsst_Element( void* self, int i )
        {
            throw new NotImplementedException("CtlVctrsst_Element is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CtlVctrsstTyp_DeleteThis")]
        public static void* CtlVctrsstTyp_DeleteThis( void* self )
        {
            throw new NotImplementedException("CtlVctrsstTyp_DeleteThis is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CtlVctrsstTyp_Create")]
        public static void* CtlVctrsstTyp_Create( int growsize, int initialcapacity )
        {
            throw new NotImplementedException("CtlVctrsstTyp_Create is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CtlVctrsstTyp_Count")]
        public static int CtlVctrsstTyp_Count( void* self )
        {
            throw new NotImplementedException("CtlVctrsstTyp_Count is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "CtlVctrsstTyp_Element")]
        public static void* CtlVctrsstTyp_Element( void* self, int i )
        {
            throw new NotImplementedException("CtlVctrsstTyp_Element is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_psstPrvwSystm_RenderAssetThumbnail")]
        public static int g_psstPrvwSystm_RenderAssetThumbnail( void* pAsset, void* targetPixmap )
        {
            throw new NotImplementedException("g_psstPrvwSystm_RenderAssetThumbnail is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_psstPrvwSystm_OnThumbnailGenerated")]
        public static void* g_psstPrvwSystm_OnThumbnailGenerated( void* asset, void* renderedPixmap )
        {
            throw new NotImplementedException("g_psstPrvwSystm_OnThumbnailGenerated is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_psstPrvwSystm_GetModelForAsset")]
        public static void* g_psstPrvwSystm_GetModelForAsset( void* pAsset )
        {
            throw new NotImplementedException("g_psstPrvwSystm_GetModelForAsset is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pAssetSystem_RecordAssetOpen")]
        public static void* g_pAssetSystem_RecordAssetOpen( void* pAsset )
        {
            throw new NotImplementedException("g_pAssetSystem_RecordAssetOpen is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pAssetSystem_RecordAssetOpen_1")]
        public static void* g_pAssetSystem_RecordAssetOpen_1( void* pAssetFileName )
        {
            throw new NotImplementedException("g_pAssetSystem_RecordAssetOpen_1 is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pAssetSystem_FindAssetByFilename")]
        public static void* g_pAssetSystem_FindAssetByFilename( void* pFilename )
        {
            throw new NotImplementedException("g_pAssetSystem_FindAssetByFilename is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pAssetSystem_FindAssetByAssetRelativePath")]
        public static void* g_pAssetSystem_FindAssetByAssetRelativePath( void* pFilename )
        {
            throw new NotImplementedException("g_pAssetSystem_FindAssetByAssetRelativePath is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pAssetSystem_RegisterAssetFile")]
        public static void* g_pAssetSystem_RegisterAssetFile( void* pFilename )
        {
            throw new NotImplementedException("g_pAssetSystem_RegisterAssetFile is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pAssetSystem_RunFrame")]
        public static void* g_pAssetSystem_RunFrame()
        {
            throw new NotImplementedException("g_pAssetSystem_RunFrame is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pAssetSystem_UpdateMods")]
        public static void* g_pAssetSystem_UpdateMods()
        {
            throw new NotImplementedException("g_pAssetSystem_UpdateMods is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pAssetSystem_GetAllModsCount")]
        public static int g_pAssetSystem_GetAllModsCount()
        {
            throw new NotImplementedException("g_pAssetSystem_GetAllModsCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pAssetSystem_RecompileAsset")]
        public static int g_pAssetSystem_RecompileAsset( void* asset, int full )
        {
            throw new NotImplementedException("g_pAssetSystem_RecompileAsset is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pAssetSystem_UpdateGameResourceType")]
        public static void* g_pAssetSystem_UpdateGameResourceType( void* title, void* extension )
        {
            throw new NotImplementedException("g_pAssetSystem_UpdateGameResourceType is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "g_pAssetSystem_LoadWorkingSetsAndTags")]
        public static void* g_pAssetSystem_LoadWorkingSetsAndTags()
        {
            throw new NotImplementedException("g_pAssetSystem_LoadWorkingSetsAndTags is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetFriendlyName_Transient")]
        public static void* IAsset_GetFriendlyName_Transient( void* self )
        {
            throw new NotImplementedException("IAsset_GetFriendlyName_Transient is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetRelativePath_Transient")]
        public static void* IAsset_GetRelativePath_Transient( void* self, long source )
        {
            throw new NotImplementedException("IAsset_GetRelativePath_Transient is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetAbsolutePath_Transient")]
        public static void* IAsset_GetAbsolutePath_Transient( void* self, long source )
        {
            throw new NotImplementedException("IAsset_GetAbsolutePath_Transient is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_HasAnyFiles")]
        public static int IAsset_HasAnyFiles( void* self )
        {
            throw new NotImplementedException("IAsset_HasAnyFiles is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_IsCached")]
        public static int IAsset_IsCached( void* self )
        {
            throw new NotImplementedException("IAsset_IsCached is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_CanReload")]
        public static int IAsset_CanReload( void* self )
        {
            throw new NotImplementedException("IAsset_CanReload is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_CanRecompile")]
        public static int IAsset_CanRecompile( void* self )
        {
            throw new NotImplementedException("IAsset_CanRecompile is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetAssetIndexInt")]
        public static uint IAsset_GetAssetIndexInt( void* self )
        {
            throw new NotImplementedException("IAsset_GetAssetIndexInt is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_OpenInTool")]
        public static int IAsset_OpenInTool( void* self )
        {
            throw new NotImplementedException("IAsset_OpenInTool is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_OpenInSecondaryTool")]
        public static int IAsset_OpenInSecondaryTool( void* self, void* tool )
        {
            throw new NotImplementedException("IAsset_OpenInSecondaryTool is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetAssetsIDependOn")]
        public static void* IAsset_GetAssetsIDependOn( void* self, void* pOutAssetsIDependOn, int bDeep )
        {
            throw new NotImplementedException("IAsset_GetAssetsIDependOn is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetAssetsIParent")]
        public static void* IAsset_GetAssetsIParent( void* self, void* pOutChildren, int bDeep )
        {
            throw new NotImplementedException("IAsset_GetAssetsIParent is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetAssetsIReference")]
        public static void* IAsset_GetAssetsIReference( void* self, void* pOutReferencers, int bDeep )
        {
            throw new NotImplementedException("IAsset_GetAssetsIReference is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetAssetsDependingOnMe")]
        public static void* IAsset_GetAssetsDependingOnMe( void* self, void* pOutDependencies, int bDeep )
        {
            throw new NotImplementedException("IAsset_GetAssetsDependingOnMe is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetAssetsReferencingMe")]
        public static void* IAsset_GetAssetsReferencingMe( void* self, void* pOutReferencers, int bDeep )
        {
            throw new NotImplementedException("IAsset_GetAssetsReferencingMe is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetAssetsParentingMe")]
        public static void* IAsset_GetAssetsParentingMe( void* self, void* pOutParents, int bDeep )
        {
            throw new NotImplementedException("IAsset_GetAssetsParentingMe is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_AdditionalRelatedFileCount")]
        public static int IAsset_AdditionalRelatedFileCount( void* self )
        {
            throw new NotImplementedException("IAsset_AdditionalRelatedFileCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_AdditionalInputDependencyCount")]
        public static int IAsset_AdditionalInputDependencyCount( void* self )
        {
            throw new NotImplementedException("IAsset_AdditionalInputDependencyCount is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetAdditionalRelatedFile_Transient")]
        public static void* IAsset_GetAdditionalRelatedFile_Transient( void* self, int nIndex )
        {
            throw new NotImplementedException("IAsset_GetAdditionalRelatedFile_Transient is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetAdditionalInputDependency_Transient")]
        public static void* IAsset_GetAdditionalInputDependency_Transient( void* self, int nIndex )
        {
            throw new NotImplementedException("IAsset_GetAdditionalInputDependency_Transient is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetUnrecognizedRelatedPaths")]
        public static void* IAsset_GetUnrecognizedRelatedPaths( void* self, long filterType, void* paths )
        {
            throw new NotImplementedException("IAsset_GetUnrecognizedRelatedPaths is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_RequireDependencyInfo_Virtual")]
        public static void* IAsset_RequireDependencyInfo_Virtual( void* self )
        {
            throw new NotImplementedException("IAsset_RequireDependencyInfo_Virtual is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_NeedAnyDependencyUpdate_Virtual")]
        public static int IAsset_NeedAnyDependencyUpdate_Virtual( void* self )
        {
            throw new NotImplementedException("IAsset_NeedAnyDependencyUpdate_Virtual is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_IsTrivialChildAsset")]
        public static int IAsset_IsTrivialChildAsset( void* self )
        {
            throw new NotImplementedException("IAsset_IsTrivialChildAsset is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_HasHiddenAssetFlag")]
        public static int IAsset_HasHiddenAssetFlag( void* self )
        {
            throw new NotImplementedException("IAsset_HasHiddenAssetFlag is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetAssetTypeId")]
        public static int IAsset_GetAssetTypeId( void* self )
        {
            throw new NotImplementedException("IAsset_GetAssetTypeId is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_CacheAsset")]
        public static void* IAsset_CacheAsset( void* self, int bIsBlocking )
        {
            throw new NotImplementedException("IAsset_CacheAsset is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_UncacheAsset")]
        public static void* IAsset_UncacheAsset( void* self )
        {
            throw new NotImplementedException("IAsset_UncacheAsset is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_HasSourceFile")]
        public static int IAsset_HasSourceFile( void* self )
        {
            throw new NotImplementedException("IAsset_HasSourceFile is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_HasCompiledFile")]
        public static int IAsset_HasCompiledFile( void* self )
        {
            throw new NotImplementedException("IAsset_HasCompiledFile is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_IsCompiled")]
        public static int IAsset_IsCompiled( void* self )
        {
            throw new NotImplementedException("IAsset_IsCompiled is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_IsCompiledAndUpToDate")]
        public static int IAsset_IsCompiledAndUpToDate( void* self )
        {
            throw new NotImplementedException("IAsset_IsCompiledAndUpToDate is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_IsCompileFailed")]
        public static int IAsset_IsCompileFailed( void* self )
        {
            throw new NotImplementedException("IAsset_IsCompileFailed is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_CompileIfNeeded")]
        public static int IAsset_CompileIfNeeded( void* self )
        {
            throw new NotImplementedException("IAsset_CompileIfNeeded is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_FindIntEditInfo")]
        public static int IAsset_FindIntEditInfo( void* self, void* name )
        {
            throw new NotImplementedException("IAsset_FindIntEditInfo is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_FindStringEditInfo")]
        public static void* IAsset_FindStringEditInfo( void* self, void* name )
        {
            throw new NotImplementedException("IAsset_FindStringEditInfo is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_GetCompileStateReason_Transient")]
        public static void* IAsset_GetCompileStateReason_Transient( void* self )
        {
            throw new NotImplementedException("IAsset_GetCompileStateReason_Transient is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_SetInMemoryReplacement")]
        public static int IAsset_SetInMemoryReplacement( void* self, void* data )
        {
            throw new NotImplementedException("IAsset_SetInMemoryReplacement is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAsset_DiscardInMemoryReplacement")]
        public static void* IAsset_DiscardInMemoryReplacement( void* self )
        {
            throw new NotImplementedException("IAsset_DiscardInMemoryReplacement is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "sstPckrLstnr_NotifyAssetPicked")]
        public static void* sstPckrLstnr_NotifyAssetPicked( void* self, void* wrapper )
        {
            throw new NotImplementedException("sstPckrLstnr_NotifyAssetPicked is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_GetFriendlyName")]
        public static void* IAssetType_GetFriendlyName( void* self )
        {
            throw new NotImplementedException("IAssetType_GetFriendlyName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_GetIconLg")]
        public static void* IAssetType_GetIconLg( void* self )
        {
            throw new NotImplementedException("IAssetType_GetIconLg is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_GetIconSm")]
        public static void* IAssetType_GetIconSm( void* self )
        {
            throw new NotImplementedException("IAssetType_GetIconSm is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_GetPrimaryExtension")]
        public static void* IAssetType_GetPrimaryExtension( void* self )
        {
            throw new NotImplementedException("IAssetType_GetPrimaryExtension is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_GetColor")]
        public static void* IAssetType_GetColor( void* self )
        {
            throw new NotImplementedException("IAssetType_GetColor is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_GetCategory")]
        public static void* IAssetType_GetCategory( void* self )
        {
            throw new NotImplementedException("IAssetType_GetCategory is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_HideTypeByDefault")]
        public static int IAssetType_HideTypeByDefault( void* self )
        {
            throw new NotImplementedException("IAssetType_HideTypeByDefault is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_IgnoreCompiledState")]
        public static int IAssetType_IgnoreCompiledState( void* self )
        {
            throw new NotImplementedException("IAssetType_IgnoreCompiledState is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_IsContentFileText")]
        public static int IAssetType_IsContentFileText( void* self )
        {
            throw new NotImplementedException("IAssetType_IsContentFileText is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_IsSimpleAsset")]
        public static int IAssetType_IsSimpleAsset( void* self )
        {
            throw new NotImplementedException("IAssetType_IsSimpleAsset is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_HasDependencies")]
        public static int IAssetType_HasDependencies( void* self )
        {
            throw new NotImplementedException("IAssetType_HasDependencies is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_PrefersIconForThumbnail")]
        public static int IAssetType_PrefersIconForThumbnail( void* self )
        {
            throw new NotImplementedException("IAssetType_PrefersIconForThumbnail is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_GetAssetTypeName")]
        public static void* IAssetType_GetAssetTypeName( void* self )
        {
            throw new NotImplementedException("IAssetType_GetAssetTypeName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "IAssetType_GetAdditionalExtensions")]
        public static void* IAssetType_GetAdditionalExtensions( void* self, void* pExts )
        {
            throw new NotImplementedException("IAssetType_GetAdditionalExtensions is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_SetExtension")]
        public static void* RsrcCmplrCntxt_SetExtension( void* self, void* name )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_SetExtension is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_SetCompiler")]
        public static void* RsrcCmplrCntxt_SetCompiler( void* self, void* name )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_SetCompiler is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_FullPath")]
        public static void* RsrcCmplrCntxt_FullPath( void* self )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_FullPath is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_RelativePath")]
        public static void* RsrcCmplrCntxt_RelativePath( void* self )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_RelativePath is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_ResourceName")]
        public static void* RsrcCmplrCntxt_ResourceName( void* self )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_ResourceName is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_SpecifyResourceVersion")]
        public static void* RsrcCmplrCntxt_SpecifyResourceVersion( void* self, int nVersion )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_SpecifyResourceVersion is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_RegisterSpecialDependency")]
        public static void* RsrcCmplrCntxt_RegisterSpecialDependency( void* self, void* str, uint nUserData, uint nFingerprint )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_RegisterSpecialDependency is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_RegisterReference")]
        public static int RsrcCmplrCntxt_RegisterReference( void* self, void* filename )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_RegisterReference is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_RegisterInputFileDependency")]
        public static void* RsrcCmplrCntxt_RegisterInputFileDependency( void* self, void* filename, int flags )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_RegisterInputFileDependency is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_WriteBlock")]
        public static int RsrcCmplrCntxt_WriteBlock( void* self, void* blockName, void* data, int count )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_WriteBlock is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_GetOverrideData")]
        public static void* RsrcCmplrCntxt_GetOverrideData( void* self )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_GetOverrideData is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_Data")]
        public static void* RsrcCmplrCntxt_Data( void* self )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_Data is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_StreamingData")]
        public static void* RsrcCmplrCntxt_StreamingData( void* self )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_StreamingData is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxt_CreateChildContext")]
        public static void* RsrcCmplrCntxt_CreateChildContext( void* self, void* pFileName )
        {
            throw new NotImplementedException("RsrcCmplrCntxt_CreateChildContext is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxtCh_SetOverrideInputData")]
        public static void* RsrcCmplrCntxtCh_SetOverrideInputData( void* self, void* data )
        {
            throw new NotImplementedException("RsrcCmplrCntxtCh_SetOverrideInputData is not yet implemented in the linux emulation layer");
        }
        [UnmanagedCallersOnly(EntryPoint = "RsrcCmplrCntxtCh_CompileImmediately")]
        public static int RsrcCmplrCntxtCh_CompileImmediately( void* self )
        {
            throw new NotImplementedException("RsrcCmplrCntxtCh_CompileImmediately is not yet implemented in the linux emulation layer");
        }
    }
    public static unsafe partial class Imports
    {
        public static delegate* unmanaged<void*> _ptr_Editor_AssetSystem_PreInitialize;
        public static delegate* unmanaged<void*, void*> _ptr_Editor_AssetSystem_AssetAdded;
        public static delegate* unmanaged<void*, void*> _ptr_Editor_AssetSystem_AssetRemoved;
        public static delegate* unmanaged<void*, void*> _ptr_Editor_AssetSystem_AssetChanged;
        public static delegate* unmanaged<void*, void*> _ptr_Editor_AssetSystem_UpdateAssetAutoTags;
        public static delegate* unmanaged<void*, void*> _ptr_Editor_AssetSystem_RecordAssetOpen;
        public static delegate* unmanaged<void*> _ptr_Editor_AssetSystem_AssetScanComplete;
        public static delegate* unmanaged<void*, void*, void*> _ptr_Editor_AssetSystem_RegisterAssetType;
        public static delegate* unmanaged<void*, void*, void*> _ptr_Editor_AssetSystem_InitializeCompilerForFilename;
        public static delegate* unmanaged<void*, void*> _ptr_Editor_AssetSystem_TryManagedCompile;
        public static delegate* unmanaged<void*, void*> _ptr_Editor_AssetSystem_OnSoundReload;
        public static delegate* unmanaged<void*, void*> _ptr_Editor_AssetSystem_OnSoundReloaded;
        public static delegate* unmanaged<void*, void*, void*> _ptr_Editor_AssetSystem_OnDemandRecompile;
        public static delegate* unmanaged<void*, void*, void*, void*, void*, void*, void*, void*, void*> _ptr_Editor_AssetSystem_OpenPicker;
        public static delegate* unmanaged<void*, void*, void*> _ptr_Editor_AssetSystem_PopulateAssetMenu;
        public static void StoreImport_Editor_AssetSystem_PreInitialize(void* ptr)
        {
            _ptr_Editor_AssetSystem_PreInitialize = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_AssetAdded(void* ptr)
        {
            _ptr_Editor_AssetSystem_AssetAdded = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_AssetRemoved(void* ptr)
        {
            _ptr_Editor_AssetSystem_AssetRemoved = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_AssetChanged(void* ptr)
        {
            _ptr_Editor_AssetSystem_AssetChanged = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_UpdateAssetAutoTags(void* ptr)
        {
            _ptr_Editor_AssetSystem_UpdateAssetAutoTags = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_RecordAssetOpen(void* ptr)
        {
            _ptr_Editor_AssetSystem_RecordAssetOpen = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_AssetScanComplete(void* ptr)
        {
            _ptr_Editor_AssetSystem_AssetScanComplete = (delegate* unmanaged<void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_RegisterAssetType(void* ptr)
        {
            _ptr_Editor_AssetSystem_RegisterAssetType = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_InitializeCompilerForFilename(void* ptr)
        {
            _ptr_Editor_AssetSystem_InitializeCompilerForFilename = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_TryManagedCompile(void* ptr)
        {
            _ptr_Editor_AssetSystem_TryManagedCompile = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_OnSoundReload(void* ptr)
        {
            _ptr_Editor_AssetSystem_OnSoundReload = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_OnSoundReloaded(void* ptr)
        {
            _ptr_Editor_AssetSystem_OnSoundReloaded = (delegate* unmanaged<void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_OnDemandRecompile(void* ptr)
        {
            _ptr_Editor_AssetSystem_OnDemandRecompile = (delegate* unmanaged<void*, void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_OpenPicker(void* ptr)
        {
            _ptr_Editor_AssetSystem_OpenPicker = (delegate* unmanaged<void*, void*, void*, void*, void*, void*, void*, void*, void*>)ptr;
        }
        public static void StoreImport_Editor_AssetSystem_PopulateAssetMenu(void* ptr)
        {
            _ptr_Editor_AssetSystem_PopulateAssetMenu = (delegate* unmanaged<void*, void*, void*>)ptr;
        }

// Editor.AssetSystem
[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_PreInitialize")]
public static void* Editor_AssetSystem_PreInitialize()
{
    return (void*)Imports._ptr_Editor_AssetSystem_PreInitialize();
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_AssetAdded")]
public static void* Editor_AssetSystem_AssetAdded( void* asset )
{
    return (void*)Imports._ptr_Editor_AssetSystem_AssetAdded( asset );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_AssetRemoved")]
public static void* Editor_AssetSystem_AssetRemoved( void* index )
{
    return (void*)Imports._ptr_Editor_AssetSystem_AssetRemoved( index );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_AssetChanged")]
public static void* Editor_AssetSystem_AssetChanged( void* index )
{
    return (void*)Imports._ptr_Editor_AssetSystem_AssetChanged( index );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_UpdateAssetAutoTags")]
public static void* Editor_AssetSystem_UpdateAssetAutoTags( void* index )
{
    return (void*)Imports._ptr_Editor_AssetSystem_UpdateAssetAutoTags( index );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_RecordAssetOpen")]
public static void* Editor_AssetSystem_RecordAssetOpen( void* index )
{
    return (void*)Imports._ptr_Editor_AssetSystem_RecordAssetOpen( index );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_AssetScanComplete")]
public static void* Editor_AssetSystem_AssetScanComplete()
{
    return (void*)Imports._ptr_Editor_AssetSystem_AssetScanComplete();
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_RegisterAssetType")]
public static void* Editor_AssetSystem_RegisterAssetType( void* none, void* assetType )
{
    return (void*)Imports._ptr_Editor_AssetSystem_RegisterAssetType( none, assetType );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_InitializeCompilerForFilename")]
public static void* Editor_AssetSystem_InitializeCompilerForFilename( void* context, void* filename )
{
    return (void*)Imports._ptr_Editor_AssetSystem_InitializeCompilerForFilename( context, filename );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_TryManagedCompile")]
public static int Editor_AssetSystem_TryManagedCompile( void* context )
{
    return (int)Imports._ptr_Editor_AssetSystem_TryManagedCompile( context );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_OnSoundReload")]
public static void* Editor_AssetSystem_OnSoundReload( void* filename )
{
    return (void*)Imports._ptr_Editor_AssetSystem_OnSoundReload( filename );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_OnSoundReloaded")]
public static void* Editor_AssetSystem_OnSoundReloaded( void* filename )
{
    return (void*)Imports._ptr_Editor_AssetSystem_OnSoundReloaded( filename );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_OnDemandRecompile")]
public static void* Editor_AssetSystem_OnDemandRecompile( void* index, void* reason )
{
    return (void*)Imports._ptr_Editor_AssetSystem_OnDemandRecompile( index, reason );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_OpenPicker")]
public static void* Editor_AssetSystem_OpenPicker( void* pParentWidget, void* assetTypes, void* callback, void* viewmode, void* selectedAsset, void* titleAndSettingsName, void* cloudAllowed, void* initialSearchText )
{
    return (void*)Imports._ptr_Editor_AssetSystem_OpenPicker( pParentWidget, assetTypes, callback, viewmode, selectedAsset, titleAndSettingsName, cloudAllowed, initialSearchText );
}

[UnmanagedCallersOnly(EntryPoint = "Editor_AssetSystem_PopulateAssetMenu")]
public static void* Editor_AssetSystem_PopulateAssetMenu( void* qMenu, void* asset )
{
    return (void*)Imports._ptr_Editor_AssetSystem_PopulateAssetMenu( qMenu, asset );
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
