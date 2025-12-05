#pragma warning disable IDE0005, CS8019
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
#pragma warning restore IDE0005, CS8019

namespace Sandbox.Engine.Emulation.Common;

/// <summary>
/// Gestionnaire centralisé de handles pour tous les objets émulés.
/// Thread-safe, utilise ConcurrentDictionary pour la sécurité multi-thread.
/// Supporte le pattern Source 2 avec reference counting et plusieurs handles pointant vers le même objet.
/// </summary>
public static class HandleManager
{
    // Constantes
    private const int MIN_HANDLE = 1001;
    private const int MIN_BINDING_HANDLE = 2000;
    private const int MAX_HANDLE = int.MaxValue - 1000;
    private const int LOCK_TIMEOUT_MS = 1000;

    // Compteurs atomiques
    private static int _nextHandle = MIN_HANDLE; // Handles normaux (impairs)
    private static int _nextBindingHandle = MIN_BINDING_HANDLE; // BindingHandles (pairs)
    private static long _registerCount;

    // Dictionnaires de gestion
    private static readonly ConcurrentDictionary<int, HandleEntry> _entries = new();
    private static readonly ConcurrentDictionary<int, int> _bindingToEntry = new(); // BindingHandle → handle principal
    private static readonly ConcurrentQueue<HandleEntry> _entryPool = new();

    // Index secondaires (lazy initialization)
    private static ConcurrentDictionary<uint, int>? _openGLHandleIndex;
    private static ConcurrentDictionary<string, int>? _nameIndex;
    private const int MAX_NAME_LENGTH = 256;

    // Métriques de performance
    private static long _copyHandleCount;
    private static long _unregisterCount;
    private static long _getCount;

    // Cache pour GetAllObjects avec versioning
    private static int _cacheVersion;
    private static int _lastCacheVersion;
    private static object[]? _cachedObjects;

    public static long RegisterCount => Interlocked.Read(ref _registerCount);
    public static int EntryPoolSize => _entryPool.Count;
    public static long CopyHandleCount => Interlocked.Read(ref _copyHandleCount);
    public static long UnregisterCount => Interlocked.Read(ref _unregisterCount);
    public static long GetCount => Interlocked.Read(ref _getCount);

    /// <summary>
    /// Classe interne représentant une entry avec reference counting.
    /// </summary>
    internal class HandleEntry
    {
        private object? _object;
        private int _bindingHandle;
        public object? Object => _object;
        public int BindingHandle => _bindingHandle;
        
        private int _referenceCount;
        private readonly HashSet<int> _handles;
        private readonly object _lock = new object();

        public HandleEntry(object obj, int bindingHandle)
        {
            _handles = new HashSet<int>(4); // Capacité initiale de 4
            Initialize(obj, bindingHandle);
        }

        public void Initialize(object obj, int bindingHandle)
        {
            _object = obj ?? throw new ArgumentNullException(nameof(obj));
            _bindingHandle = bindingHandle;
            _referenceCount = 1;
            lock (_lock)
            {
                _handles.Clear();
            }
        }

        public void Reset()
        {
            _object = null;
            _bindingHandle = 0;
            _referenceCount = 0;
            lock (_lock)
            {
                _handles.Clear();
            }
        }

        /// <summary>
        /// Propriété thread-safe pour lire le compteur de références.
        /// </summary>
        public int ReferenceCount => Volatile.Read(ref _referenceCount);

        /// <summary>
        /// Incrémente le compteur de références de manière atomique.
        /// </summary>
        public int IncrementRef()
        {
            return Interlocked.Increment(ref _referenceCount);
        }

        /// <summary>
        /// Décrémente le compteur de références de manière atomique.
        /// </summary>
        public int DecrementRef()
        {
            return Interlocked.Decrement(ref _referenceCount);
        }

        /// <summary>
        /// Ajoute un handle à la liste (protégé par lock).
        /// </summary>
        public void AddHandle(int handle)
        {
            lock (_lock)
            {
                _handles.Add(handle);
            }
        }

        /// <summary>
        /// Retire un handle de la liste (protégé par lock).
        /// Retourne true si le handle a été retiré, false s'il n'existait pas.
        /// </summary>
        public bool RemoveHandle(int handle)
        {
            lock (_lock)
            {
                return _handles.Remove(handle);
            }
        }

        /// <summary>
        /// Retourne un snapshot thread-safe de tous les handles via ArrayPool.
        /// Retourne aussi le nombre de handles dans le paramètre out.
        /// </summary>
        public int[] GetAllHandles(out int count)
        {
            lock (_lock)
            {
                count = _handles.Count;
                int length = count == 0 ? 1 : count;
                int[] snapshot = ArrayPool<int>.Shared.Rent(length);
                _handles.CopyTo(snapshot);
                return snapshot;
            }
        }

        /// <summary>
        /// Exécute une action avec un lock et timeout.
        /// Retourne true si le lock a été acquis et l'action exécutée, false en cas de timeout.
        /// </summary>
        public bool TryLock(Action action, int timeoutMs = LOCK_TIMEOUT_MS)
        {
            if (Monitor.TryEnter(_lock, timeoutMs))
            {
                try
                {
                    action();
                    return true;
                }
                finally
                {
                    Monitor.Exit(_lock);
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Enregistre un objet et retourne un handle unique.
    /// </summary>
    public static int Register(object obj)
    {
        if (obj == null)
            return 0;

        // Générer BindingHandle unique (pair)
        if (!TryGenerateBindingHandle(out int bindingHandle))
            return 0;

        // Générer handle principal unique (impair)
        if (!TryGenerateHandle(out int handle))
            return 0;

        // Créer ou réutiliser HandleEntry
        var entry = GetOrCreateEntry(obj, bindingHandle);
        entry.AddHandle(handle);

        // Enregistrer
        _entries[handle] = entry;
        _bindingToEntry[bindingHandle] = handle;
        Interlocked.Increment(ref _registerCount);
        Interlocked.Increment(ref _cacheVersion); // Invalider le cache

        return handle;
    }

    /// <summary>
    /// Récupère un objet par son handle.
    /// </summary>
    public static T? Get<T>(int handle) where T : class
    {
        if (!IsValidHandle(handle))
            return null;

        if (_entries.TryGetValue(handle, out var entry))
        {
            Interlocked.Increment(ref _getCount);
            return entry.Object as T;
        }
        return null;
    }

    /// <summary>
    /// Libère un handle et son objet associé.
    /// </summary>
    public static void Unregister(int handle)
    {
        if (!IsValidHandle(handle))
            return;

        if (!_entries.TryGetValue(handle, out var entry))
            return;

        int[]? allHandles = null;
        int handleCount = 0;
        bool shouldRemove = false;
        
        bool lockAcquired = entry.TryLock(() =>
        {
            // Obtenir tous les handles AVANT de retirer le handle actuel
            allHandles = entry.GetAllHandles(out handleCount);
            
            if (!entry.RemoveHandle(handle))
            {
                // Handle déjà retiré, libérer le pool et retourner
                    if (allHandles != null && handleCount > 0)
                        ArrayPool<int>.Shared.Return(allHandles, clearArray: false);
                return;
            }

            int newRefCount = entry.DecrementRef();
            if (newRefCount <= 0)
            {
                shouldRemove = true;
            }
            else
            {
                // Pas besoin de retirer, libérer le pool
                if (allHandles != null && handleCount > 0)
                    ArrayPool<int>.Shared.Return(allHandles, clearArray: false);
                allHandles = null;
                handleCount = 0;
            }
        });

        if (!lockAcquired)
            return;

        if (shouldRemove)
        {
            if (allHandles != null)
            {
                // Batch removal : retirer tous les handles
                for (int i = 0; i < handleCount; i++)
                {
                    int h = allHandles[i];
                    if (h != 0)
                        _entries.TryRemove(h, out _);
                }

                ArrayPool<int>.Shared.Return(allHandles, clearArray: false);
            }

            // Libérer le BindingHandle
            _bindingToEntry.TryRemove(entry.BindingHandle, out _);

            // Nettoyer les index secondaires
            if (entry.Object != null)
            {
                UnindexObject(entry.Object, entry.BindingHandle);
            }

            ReturnToPool(entry);
            Interlocked.Increment(ref _cacheVersion); // Invalider le cache
        }

        Interlocked.Increment(ref _unregisterCount);
    }

    /// <summary>
    /// Vérifie si un handle existe.
    /// </summary>
    public static bool Exists(int handle)
    {
        if (!IsValidHandle(handle))
            return false;
        return _entries.ContainsKey(handle);
    }

    /// <summary>
    /// Copie un handle (incrémente le compteur de références).
    /// </summary>
    public static int CopyHandle(int handle)
    {
        if (!IsValidHandle(handle))
            return 0;

        if (!_entries.TryGetValue(handle, out var entry))
            return 0;

        int newHandle = 0;
        bool success = entry.TryLock(() =>
        {
            entry.IncrementRef();
            newHandle = SafeIncrementOdd(ref _nextHandle, MAX_HANDLE, MIN_HANDLE);
            if (IsValidHandle(newHandle))
            {
                entry.AddHandle(newHandle);
                _entries[newHandle] = entry;
            }
            else
            {
                // Rollback si échec
                entry.DecrementRef();
                newHandle = 0;
            }
        });

        if (success && newHandle != 0)
        {
            Interlocked.Increment(ref _copyHandleCount);
        }

        return success ? newHandle : 0;
    }

    private static HandleEntry GetOrCreateEntry(object obj, int bindingHandle)
    {
        if (_entryPool.TryDequeue(out var entry))
        {
            entry.Initialize(obj, bindingHandle);
            return entry;
        }
        return new HandleEntry(obj, bindingHandle);
    }

    private static void ReturnToPool(HandleEntry entry)
    {
        entry.Reset();
        _entryPool.Enqueue(entry);
    }

    /// <summary>
    /// Retourne le BindingHandle associé à un handle.
    /// </summary>
    public static int GetBindingHandle(int handle)
    {
        if (!IsValidHandle(handle))
            return 0;

        if (_entries.TryGetValue(handle, out var entry))
        {
            return entry.BindingHandle;
        }
        return 0;
    }

    /// <summary>
    /// Retourne le compteur de références pour un handle.
    /// </summary>
    public static int GetReferenceCount(int handle)
    {
        if (!IsValidHandle(handle))
            return 0;

        if (_entries.TryGetValue(handle, out var entry))
        {
            return entry.ReferenceCount;
        }
        return 0;
    }

    /// <summary>
    /// Retourne tous les handles associés à un objet (via un handle).
    /// </summary>
    public static int[] GetAllHandles(int handle)
    {
        if (!IsValidHandle(handle))
            return Array.Empty<int>();

        if (_entries.TryGetValue(handle, out var entry))
        {
            var snapshot = entry.GetAllHandles(out int count);
            
            // Créer un tableau de la bonne taille
            var result = new int[count];
            Array.Copy(snapshot, result, count);
            
            // Retourner au pool
                ArrayPool<int>.Shared.Return(snapshot, clearArray: false);
            
            return result;
        }
        return Array.Empty<int>();
    }

    /// <summary>
    /// Exécute une action avec un lock sur l'entry associée au handle.
    /// </summary>
    /// <param name="handle">Le handle sur lequel verrouiller</param>
    /// <param name="action">L'action à exécuter</param>
    /// <param name="timeoutMs">Timeout en millisecondes (défaut: 1000ms)</param>
    /// <returns>True si le lock a été acquis et l'action exécutée, false sinon</returns>
    public static bool TryLock(int handle, Action action, int timeoutMs = LOCK_TIMEOUT_MS)
    {
        if (!IsValidHandle(handle))
            return false;

        if (!_entries.TryGetValue(handle, out var entry))
            return false;

        return entry.TryLock(action, timeoutMs);
    }

    /// <summary>
    /// Vérifie si un handle est valide (impair, supérieur ou égal à MIN_HANDLE, inférieur à MAX_HANDLE).
    /// </summary>
    private static bool IsValidHandle(int handle)
    {
        return handle >= MIN_HANDLE && handle < MAX_HANDLE && (handle % 2) == 1;
    }

    /// <summary>
    /// Vérifie si un BindingHandle est valide (pair, supérieur ou égal à MIN_BINDING_HANDLE, inférieur à MAX_HANDLE).
    /// </summary>
    private static bool TryGenerateBindingHandle(out int bindingHandle)
    {
        bindingHandle = SafeIncrementEven(ref _nextBindingHandle, MAX_HANDLE, MIN_BINDING_HANDLE);
        return bindingHandle != 0;
    }

    private static bool TryGenerateHandle(out int handle)
    {
        handle = SafeIncrementOdd(ref _nextHandle, MAX_HANDLE, MIN_HANDLE);
        return handle != 0;
    }

    /// <summary>
    /// Incrémente une valeur impaire avec protection contre overflow et réinitialisation si nécessaire.
    /// </summary>
    private static int SafeIncrementOdd(ref int value, int maxValue, int resetValue)
    {
        int current;
        do
        {
            current = Interlocked.Increment(ref value);
            
            if (current >= maxValue)
            {
                // Réinitialiser si on approche de la limite
                Interlocked.CompareExchange(ref value, resetValue, current);
                current = resetValue;
            }
            
            // S'assurer que c'est impair
                if ((current % 2) == 0)
            {
                current = Interlocked.Increment(ref value);
            }
        } while ((current % 2) == 0 && current < maxValue);
        
        return current < maxValue ? current : 0;
    }

    /// <summary>
    /// Incrémente une valeur paire avec protection contre overflow et réinitialisation si nécessaire.
    /// </summary>
    private static int SafeIncrementEven(ref int value, int maxValue, int resetValue)
    {
        int current;
        do
        {
            current = Interlocked.Increment(ref value);
            
            if (current >= maxValue)
            {
                // Réinitialiser si on approche de la limite
                Interlocked.CompareExchange(ref value, resetValue, current);
                current = resetValue;
            }
            
            // S'assurer que c'est pair
            if ((current % 2) == 1)
            {
                current = Interlocked.Increment(ref value);
            }
        } while ((current % 2) == 1 && current < maxValue);
        
        return current < maxValue ? current : 0;
    }

    // ========== Index secondaires (Cycle TDD 1.5) ==========

    /// <summary>
    /// Obtient l'index OpenGLHandle avec lazy initialization.
    /// </summary>
    private static ConcurrentDictionary<uint, int> GetOpenGLHandleIndex()
    {
        if (_openGLHandleIndex == null)
        {
            Interlocked.CompareExchange(ref _openGLHandleIndex, new ConcurrentDictionary<uint, int>(), null);
        }
        return _openGLHandleIndex!;
    }

    /// <summary>
    /// Obtient l'index Name avec lazy initialization.
    /// </summary>
    private static ConcurrentDictionary<string, int> GetNameIndex()
    {
        if (_nameIndex == null)
        {
            Interlocked.CompareExchange(ref _nameIndex, new ConcurrentDictionary<string, int>(), null);
        }
        return _nameIndex!;
    }

    /// <summary>
    /// Enregistre un index OpenGLHandle.
    /// </summary>
    public static void RegisterOpenGLHandleIndex(uint openGLHandle, int bindingHandle)
    {
        if (openGLHandle == 0 || !IsValidBindingHandle(bindingHandle))
            return;

        GetOpenGLHandleIndex()[openGLHandle] = bindingHandle;
    }

    /// <summary>
    /// Enregistre un index Name (lowercase, max 256 chars).
    /// </summary>
    public static void RegisterNameIndex(string name, int bindingHandle)
    {
        if (string.IsNullOrEmpty(name) || !IsValidBindingHandle(bindingHandle))
            return;

        if (name.Length > MAX_NAME_LENGTH)
            return; // Ignorer les noms trop longs pour économiser la mémoire

        string normalizedName = name.ToLowerInvariant();
        GetNameIndex()[normalizedName] = bindingHandle;
    }

    /// <summary>
    /// Nettoie les index secondaires lors de Unregister.
    /// </summary>
    private static void UnindexObject(object obj, int bindingHandle)
    {
        // Nettoyer l'index OpenGLHandle si l'objet a une propriété OpenGLHandle
        // Note: Cette méthode nécessite une réflexion ou une interface, pour l'instant on nettoie manuellement
        // Les systèmes spécifiques appelleront UnindexOpenGLHandle/UnindexName explicitement

        // Nettoyer l'index Name si nécessaire
        // Même note : nettoyage manuel par les systèmes
        
        // Paramètres non utilisés pour l'instant (nettoyage manuel par les systèmes)
        _ = obj;
        _ = bindingHandle;
    }

    /// <summary>
    /// Retire un index OpenGLHandle.
    /// </summary>
    public static void UnindexOpenGLHandle(uint openGLHandle)
    {
        if (_openGLHandleIndex != null)
        {
            _openGLHandleIndex.TryRemove(openGLHandle, out _);
        }
    }

    /// <summary>
    /// Retire un index Name.
    /// </summary>
    public static void UnindexName(string name)
    {
        if (string.IsNullOrEmpty(name) || _nameIndex == null)
            return;

        string normalizedName = name.ToLowerInvariant();
        _nameIndex.TryRemove(normalizedName, out _);
    }

    /// <summary>
    /// Recherche un objet par OpenGLHandle (O(1)).
    /// </summary>
    public static T? FindByOpenGLHandle<T>(uint openGLHandle) where T : class
    {
        if (openGLHandle == 0 || _openGLHandleIndex == null)
            return null;

        if (!_openGLHandleIndex.TryGetValue(openGLHandle, out int bindingHandle))
            return null;

        return GetByBindingHandle<T>(bindingHandle);
    }

    /// <summary>
    /// Recherche un objet par nom (O(1)).
    /// </summary>
    public static T? FindByName<T>(string name) where T : class
    {
        if (string.IsNullOrEmpty(name) || _nameIndex == null)
            return null;

        string normalizedName = name.ToLowerInvariant();
        if (!_nameIndex.TryGetValue(normalizedName, out int bindingHandle))
            return null;

        return GetByBindingHandle<T>(bindingHandle);
    }

    /// <summary>
    /// Récupère un objet par BindingHandle (O(1)).
    /// </summary>
    public static T? GetByBindingHandle<T>(int bindingHandle) where T : class
    {
        if (!IsValidBindingHandle(bindingHandle))
            return null;

        if (!_bindingToEntry.TryGetValue(bindingHandle, out int handle))
            return null;

        return Get<T>(handle);
    }

    /// <summary>
    /// Obtient un handle à partir d'un BindingHandle (O(1)).
    /// </summary>
    public static int GetHandleByBindingHandle(int bindingHandle)
    {
        if (!IsValidBindingHandle(bindingHandle))
            return 0;

        if (!_bindingToEntry.TryGetValue(bindingHandle, out int handle))
            return 0;

        return handle;
    }

    /// <summary>
    /// Vérifie si un BindingHandle est valide (pair, supérieur ou égal à MIN_BINDING_HANDLE, inférieur à MAX_HANDLE).
    /// </summary>
    private static bool IsValidBindingHandle(int bindingHandle)
    {
        return bindingHandle >= MIN_BINDING_HANDLE && bindingHandle < MAX_HANDLE && (bindingHandle % 2) == 0;
    }

    // ========== Métriques et HealthCheck (Cycle TDD 1.6) ==========

    /// <summary>
    /// Retourne tous les objets uniques (avec cache versioning).
    /// </summary>
    public static object[] GetAllObjects()
    {
        int currentVersion = Volatile.Read(ref _cacheVersion);
        if (_cachedObjects != null && _lastCacheVersion == currentVersion)
        {
            return _cachedObjects; // Cache valide, retour O(1)
        }

        // Reconstruire le cache
        var objects = new List<object>();
        foreach (var kvp in _bindingToEntry)
        {
            if (_entries.TryGetValue(kvp.Value, out var entry))
            {
                if (entry.Object != null)
                {
                    objects.Add(entry.Object);
                }
            }
        }

        _cachedObjects = objects.ToArray();
        _lastCacheVersion = currentVersion;
        return _cachedObjects;
    }

    /// <summary>
    /// Retourne le nombre d'objets uniques (O(1)).
    /// </summary>
    public static int GetUniqueObjectCount()
    {
        return _bindingToEntry.Count;
    }

    /// <summary>
    /// Vérifie la santé du système et détecte les incohérences.
    /// </summary>
    public static bool HealthCheck(out string errorMessage)
    {
        errorMessage = string.Empty;

        // Vérifier que tous les handles dans _entries pointent vers des entries valides
        foreach (var kvp in _entries)
        {
            if (kvp.Value == null)
            {
                errorMessage = $"Handle {kvp.Key} pointe vers une entry null";
                return false;
            }

            if (kvp.Value.Object == null)
            {
                errorMessage = $"Handle {kvp.Key} a une entry avec Object null";
                return false;
            }
        }

        // Vérifier que tous les BindingHandles dans _bindingToEntry ont des entries correspondantes
        foreach (var kvp in _bindingToEntry)
        {
            if (!_entries.ContainsKey(kvp.Value))
            {
                errorMessage = $"BindingHandle {kvp.Key} pointe vers un handle {kvp.Value} qui n'existe pas dans _entries";
                return false;
            }
        }

        // Vérifier que les index sont cohérents
        if (_openGLHandleIndex != null)
        {
            foreach (var kvp in _openGLHandleIndex)
            {
                if (!_bindingToEntry.ContainsKey(kvp.Value))
                {
                    errorMessage = $"OpenGLHandle {kvp.Key} pointe vers un BindingHandle {kvp.Value} qui n'existe pas";
                    return false;
                }
            }
        }

        if (_nameIndex != null)
        {
            foreach (var kvp in _nameIndex)
            {
                if (!_bindingToEntry.ContainsKey(kvp.Value))
                {
                    errorMessage = $"Name '{kvp.Key}' pointe vers un BindingHandle {kvp.Value} qui n'existe pas";
                    return false;
                }
            }
        }

        return true;
    }
}

