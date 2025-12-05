using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Common;

namespace Sandbox.Engine.Emulation.Model;

/// <summary>
/// Module d'émulation pour les fonctions CModel (CModel_*).
/// Gère les modèles 3D chargés depuis les ressources.
/// </summary>
public static unsafe class ModelSystem
{
    /// <summary>
    /// Données internes pour un modèle émulé.
    /// Pattern identique à MaterialData dans MaterialSystem.cs
    /// </summary>
    internal class ModelData
    {
        public string Name { get; set; } = "";
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero; // Pointeur de binding unique (handle HandleManager)
        public bool IsError { get; set; } = false;
        public bool IsLoaded { get; set; } = true; // Par défaut, considéré comme chargé
    }
    
    /// <summary>
    /// Initialise le module ModelSystem en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 15288-15351 (541-604)
    /// </summary>
    public static void Init(void** native)
    {
        // CModel functions essentielles (indices 541-546 depuis Interop.Engine.cs)
        native[541] = (void*)(delegate* unmanaged<IntPtr, void>)&CModel_DestroyStrongHandle;
        native[542] = (void*)(delegate* unmanaged<IntPtr, int>)&CModel_IsStrongHandleValid;
        native[543] = (void*)(delegate* unmanaged<IntPtr, int>)&CModel_IsError;
        native[544] = (void*)(delegate* unmanaged<IntPtr, int>)&CModel_IsStrongHandleLoaded;
        native[545] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CModel_CopyStrongHandle;
        native[546] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CModel_GetBindingPtr;
        
        // CModel functions supplémentaires (indices 547+)
        native[547] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&CModel_GetModelName;
        native[548] = (void*)(delegate* unmanaged<IntPtr, int>)&CModel_IsTranslucent;
        native[549] = (void*)(delegate* unmanaged<IntPtr, int>)&CModel_IsTranslucentTwoPass;
        native[550] = (void*)(delegate* unmanaged<IntPtr, int>)&CModel_HasPhysics;
        
        // TODO: Patcher les autres fonctions CModel_* (indices 551-604) quand elles seront implémentées
        
        // TODO: Implémenter les autres fonctions CModel_* (indices 551-604)
        // Pour l'instant, on implémente seulement les fonctions essentielles pour que le système fonctionne
        
        Console.WriteLine("[NativeAOT] ModelSystem module initialized");
    }
    
    /// <summary>
    /// Helper pour créer un modèle depuis une ressource (appelable depuis le code managé).
    /// Utilisé par EngineGlue.Glue_Resources_GetModel.
    /// </summary>
    public static IntPtr CreateModelFromResourceHelper(string resourceName)
    {
        if (string.IsNullOrEmpty(resourceName)) return IntPtr.Zero;
        
        // Chercher un modèle existant avec ce nom (O(1) via index)
        var existingModel = HandleManager.FindByName<ModelData>(resourceName);
        if (existingModel != null && existingModel.BindingPtr != IntPtr.Zero)
        {
            // Trouver un handle existant pour ce modèle via le BindingHandle
            int existingBindingHandle = (int)existingModel.BindingPtr;
            int existingHandle = HandleManager.GetHandleByBindingHandle(existingBindingHandle);
            if (existingHandle != 0)
            {
                // Obtenir tous les handles pour ce modèle et retourner le premier
                var allHandles = HandleManager.GetAllHandles(existingHandle);
                if (allHandles.Length > 0)
                {
                    Console.WriteLine($"[NativeAOT] ModelSystem.CreateModelFromResourceHelper: found existing {resourceName}");
                    return (IntPtr)allHandles[0];
                }
            }
        }
        
        // Créer un nouveau modèle
        var modelData = new ModelData
        {
            Name = resourceName
        };
        
        // Enregistrer dans HandleManager pour obtenir un handle unique
        int handle = HandleManager.Register(modelData);
        if (handle == 0) return IntPtr.Zero;
        
        int bindingHandle = HandleManager.GetBindingHandle(handle);
        modelData.BindingPtr = (IntPtr)bindingHandle;
        
        // Enregistrer dans l'index Name pour recherche O(1)
        HandleManager.RegisterNameIndex(resourceName, bindingHandle);
        
        Console.WriteLine($"[NativeAOT] ModelSystem.CreateModelFromResourceHelper: created new {resourceName}, handle={handle}");
        return (IntPtr)handle;
    }
    
    /// <summary>
    /// Libère un handle fort de modèle.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, void &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static void CModel_DestroyStrongHandle(IntPtr self)
    {
        if (self == IntPtr.Zero) return;
        
        var modelData = HandleManager.Get<ModelData>((int)self);
        if (modelData != null)
        {
            // Nettoyer les index secondaires avant Unregister
            if (!string.IsNullOrEmpty(modelData.Name))
            {
                HandleManager.UnindexName(modelData.Name);
            }
        }
        
        HandleManager.Unregister((int)self);
        Console.WriteLine($"[NativeAOT] CModel_DestroyStrongHandle: {self}");
    }
    
    /// <summary>
    /// Vérifie si un handle fort de modèle est valide.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged[SuppressGCTransition]&lt; IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int CModel_IsStrongHandleValid(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        return HandleManager.Exists((int)self) ? 1 : 0;
    }
    
    /// <summary>
    /// Vérifie si un modèle est en erreur.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged[SuppressGCTransition]&lt; IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int CModel_IsError(IntPtr self)
    {
        if (self == IntPtr.Zero) return 1; // Null = erreur
        
        var modelData = HandleManager.Get<ModelData>((int)self);
        if (modelData != null)
        {
            return modelData.IsError ? 1 : 0;
        }
        
        return 1; // Handle invalide = erreur
    }
    
    /// <summary>
    /// Vérifie si un modèle est chargé.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged[SuppressGCTransition]&lt; IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int CModel_IsStrongHandleLoaded(IntPtr self)
    {
        if (self == IntPtr.Zero) return 0;
        
        var modelData = HandleManager.Get<ModelData>((int)self);
        if (modelData != null)
        {
            return modelData.IsLoaded ? 1 : 0;
        }
        
        return 0;
    }
    
    /// <summary>
    /// Crée une copie d'un handle fort de modèle.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged[SuppressGCTransition]&lt; IntPtr, IntPtr &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IntPtr CModel_CopyStrongHandle(IntPtr self)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        int newHandle = HandleManager.CopyHandle((int)self);
        if (newHandle != 0)
        {
            Console.WriteLine($"[NativeAOT] CModel_CopyStrongHandle: {self} -> {newHandle} (refs={HandleManager.GetReferenceCount((int)self)})");
            return (IntPtr)newHandle;
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Obtient le pointeur de binding d'un modèle.
    /// Le binding pointer est un identifiant unique qui identifie le modèle natif,
    /// utilisé pour comparer si deux handles pointent vers le même modèle.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged[SuppressGCTransition]&lt; IntPtr, IntPtr &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IntPtr CModel_GetBindingPtr(IntPtr self)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        int bindingHandle = HandleManager.GetBindingHandle((int)self);
        return bindingHandle != 0 ? (IntPtr)bindingHandle : IntPtr.Zero;
    }
    
    /// <summary>
    /// Obtient le nom d'un modèle.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, IntPtr &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr CModel_GetModelName(IntPtr self)
    {
        if (self == IntPtr.Zero) return IntPtr.Zero;
        
        var modelData = HandleManager.Get<ModelData>((int)self);
        if (modelData != null)
        {
            // Allouer de la mémoire pour le nom (le moteur est responsable de la libération)
            return Marshal.StringToHGlobalAnsi(modelData.Name);
        }
        
        return IntPtr.Zero;
    }
    
    /// <summary>
    /// Vérifie si un modèle est translucide.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CModel_IsTranslucent(IntPtr self)
    {
        // TODO: Implémenter la logique réelle basée sur les matériaux du modèle
        throw new NotImplementedException("CModel_IsTranslucent not implemented");
    }
    
    /// <summary>
    /// Vérifie si un modèle nécessite un rendu en deux passes (translucide).
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CModel_IsTranslucentTwoPass(IntPtr self)
    {
        // TODO: Implémenter la logique réelle
        throw new NotImplementedException( "CModel_IsTranslucentTwoPass not implemented" );
    }
    
    /// <summary>
    /// Vérifie si un modèle a de la physique.
    /// Signature exacte depuis Interop.Engine.cs: delegate* unmanaged&lt; IntPtr, int &gt;
    /// </summary>
    [UnmanagedCallersOnly]
    public static int CModel_HasPhysics(IntPtr self)
    {
        // TODO: Implémenter la logique réelle basée sur les données du modèle
        throw new NotImplementedException( "CModel_HasPhysics not implemented" );
    }
}

