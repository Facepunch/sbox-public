# Plan de Refactorisation : HandleManager avec Reference Counting (TDD)

## Objectif

Unifier l'architecture de gestion des handles en améliorant `HandleManager` pour supporter le pattern Source 2 (plusieurs handles pointant vers le même objet avec reference counting), puis migrer `TextureSystem`, `MaterialSystem` et `ModelSystem` pour utiliser ce système unifié.

## Méthodologie TDD

**Approche** : Chaque fonctionnalité sera développée en suivant le cycle **Red → Green → Refactor** :
1. **RED** : Écrire les tests qui échouent (définit le comportement attendu)
2. **GREEN** : Implémenter le minimum pour faire passer les tests
3. **REFACTOR** : Améliorer le code tout en gardant les tests verts

**Structure des tests** :
- `tests/Sbox.Engine.Emulation.Tests/Common/HandleManagerTests.cs` : Tests unitaires HandleManager
- `tests/Sbox.Engine.Emulation.Tests/Texture/TextureSystemTests.cs` : Tests TextureSystem
- `tests/Sbox.Engine.Emulation.Tests/Material/MaterialSystemTests.cs` : Tests MaterialSystem
- `tests/Sbox.Engine.Emulation.Tests/Model/ModelSystemTests.cs` : Tests ModelSystem
- `tests/Sbox.Engine.Emulation.Tests/Integration/HandleManagerIntegrationTests.cs` : Tests d'intégration

**Framework** : xUnit (compatible .NET 10.0, NativeAOT)

**Workflow par cycle** :
1. Écrire les tests pour une fonctionnalité spécifique (RED)
2. Implémenter le minimum pour faire passer les tests (GREEN)
3. Améliorer le code tout en gardant les tests verts (REFACTOR)
4. Commiter après chaque étape (test, implémentation, refactoring)

**Directives** :
- Tester d'abord les cas simples, puis les cas limites
- Tester les validations (handles invalides, null, etc.)
- Tester le thread-safety pour les opérations concurrentes
- Tester les optimisations (pool, cache, index)
- Les tests servent de documentation du comportement attendu

## Étape 1 : Améliorer HandleManager (TDD)

**Fichier** : `src/Sbox.Engine.Emulation/Common/HandleManager.cs`
**Tests** : `tests/Sbox.Engine.Emulation.Tests/Common/HandleManagerTests.cs`

### Cycle TDD 1.1 : HandleEntry - Structure de base

**RED** : Écrire les tests pour `HandleEntry` :
- Initialisation avec objet et BindingHandle
- `IncrementRef()` / `DecrementRef()` modifient le compteur atomiquement
- `AddHandle()` / `RemoveHandle()` gèrent le HashSet thread-safe
- `GetAllHandles()` retourne un snapshot thread-safe
- `TryLock()` exécute l'action si lock acquis, timeout sinon

**GREEN** : Implémenter `HandleEntry` avec les fonctionnalités de base

**REFACTOR** : Optimiser avec `ArrayPool<int>` pour `GetAllHandles()`, capacité initiale HashSet = 4

### Cycle TDD 1.2 : Register() avec pool d'objets

**RED** : Écrire les tests pour `Register()` :
- Retourne un handle valide (impair, >= MIN_HANDLE)
- Crée entry avec ReferenceCount = 1
- Génère BindingHandle unique (pair, >= MIN_BINDING_HANDLE)
- Réutilise HandleEntry depuis le pool si disponible
- Rejette null object (retourne 0)
- Invalide le cache `GetAllObjects()`

**GREEN** : Implémenter `Register()` avec validation, génération handles, pool

**REFACTOR** : Optimiser génération handles, améliorer gestion pool, ajouter métriques

### Cycle TDD 1.3 : CopyHandle()

**RED** : Écrire les tests pour `CopyHandle()` :
- Retourne un nouveau handle valide
- Augmente ReferenceCount (même valeur pour les deux handles)
- Retourne le même BindingHandle pour les handles copiés
- Rejette handles invalides/non-existants (retourne 0)
- Gère timeout de lock gracieusement

**GREEN** : Implémenter `CopyHandle()` avec validation, lock timeout, incrément atomique

**REFACTOR** : Optimiser lock, améliorer gestion timeout

### Cycle TDD 1.4 : Unregister() avec batch removal

**RED** : Écrire les tests pour `Unregister()` :
- Décrémente ReferenceCount
- Libère objet si ReferenceCount = 0
- Retire tous les handles de l'objet en batch
- Retourne entry au pool
- Nettoie les index secondaires
- Double Unregister ne crashe pas
- Gère timeout de lock gracieusement

**GREEN** : Implémenter `Unregister()` avec validation, batch removal, nettoyage index, pool

**REFACTOR** : Optimiser batch removal, améliorer nettoyage index

### Cycle TDD 1.5 : Index secondaires (OpenGLHandle, Name)

**RED** : Écrire les tests pour les index :
- `RegisterOpenGLHandleIndex()` crée l'index (lazy initialization)
- `RegisterNameIndex()` crée l'index (lazy initialization)
- `FindByOpenGLHandle()` trouve l'objet en O(1)
- `FindByName()` trouve l'objet en O(1) (lowercase, max 256 chars)
- `Unregister()` nettoie les index automatiquement
- Index non créés si non utilisés (lazy)

**GREEN** : Implémenter lazy initialization, méthodes d'indexation, nettoyage

**REFACTOR** : Optimiser recherches, limiter longueur noms

### Cycle TDD 1.6 : HealthCheck() et métriques

**RED** : Écrire les tests pour monitoring :
- `HealthCheck()` retourne true si état valide
- `HealthCheck()` détecte incohérences (entries vs bindingToEntry, index)
- `PerformanceMetrics` compte les opérations (Register, CopyHandle, Unregister, Get)
- Métriques thread-safe

**GREEN** : Implémenter `HealthCheck()` et `PerformanceMetrics`

**REFACTOR** : Améliorer vérifications HealthCheck, optimiser métriques

### Modifications détaillées

1. **Ajouter une classe interne `HandleEntry`** :

- Stocke l'objet (`object Object`)
- Compteur de références (`int _referenceCount`) : champ privé avec propriété en lecture seule
- BindingHandle unique (`int BindingHandle`) pour `GetBindingPtr()`
- Liste des handles associés (`HashSet<int> _handles`) : capacité initiale de 4, protégée par lock pour thread-safety
- Lock interne (`object _lock`) : pour synchroniser les accès à `_handles` et `_referenceCount`
- Constante de timeout : `LOCK_TIMEOUT_MS = 1000` (1 seconde) pour protection deadlock
- Méthodes thread-safe :
  - `IncrementRef()` : utilise `Interlocked.Increment()` pour atomicité
  - `DecrementRef()` : utilise `Interlocked.Decrement()` pour atomicité
  - `AddHandle(int handle)` : protégée par lock
  - `RemoveHandle(int handle)` : protégée par lock, retourne bool pour vérification
  - `GetAllHandles()` : retourne un snapshot (copie) thread-safe via `ArrayPool<int>` pour éviter allocations
  - `TryLock(Action action)` : wrapper avec timeout pour éviter deadlocks
- **Pool d'objets** : HandleEntry sera réutilisé via un pool pour éviter allocations GC

2. **Ajouter les dictionnaires de gestion** :

- `ConcurrentDictionary<int, HandleEntry> _entries` : handle → entry (thread-safe)
- `ConcurrentDictionary<int, int> _bindingToEntry` : BindingHandle → handle principal (pour recherche rapide O(1))
- Compteurs atomiques :
  - `int _nextHandle` : handles normaux (impairs, commence à 1001)
  - `int _nextBindingHandle` : BindingHandles (pairs, commence à 2000)
  - Utiliser `SafeIncrement()` avec protection contre integer overflow
  - Constantes de validation : `MIN_HANDLE = 1001`, `MIN_BINDING_HANDLE = 2000`, `MAX_HANDLE = int.MaxValue - 1000`
- **Pool d'objets HandleEntry** :
  - `ConcurrentQueue<HandleEntry> _entryPool` : pool pour réutiliser les HandleEntry
  - `GetOrCreateEntry(object obj, int bindingHandle)` : obtenir depuis pool ou créer nouveau
  - `ReturnToPool(HandleEntry entry)` : retourner au pool après nettoyage
- **ArrayPool pour allocations** :
  - `ArrayPool<int>.Shared` : pour `GetAllHandles()` afin d'éviter allocations répétées

3. **Ajouter les index secondaires pour recherches O(1)** :

- `ConcurrentDictionary<uint, int>? _openGLHandleIndex` : OpenGLHandle → BindingHandle (lazy initialization)
- `ConcurrentDictionary<string, int>? _nameIndex` : nom (lowercase) → BindingHandle (lazy initialization)
- Méthodes d'indexation :
  - `GetOpenGLHandleIndex()` : lazy initialization du dictionnaire OpenGL
  - `GetNameIndex()` : lazy initialization du dictionnaire nom
  - `RegisterOpenGLHandleIndex(uint openGLHandle, int bindingHandle)` : enregistrer un index OpenGL
  - `RegisterNameIndex(string name, int bindingHandle)` : enregistrer un index nom (lowercase, max 256 chars)
  - `UnindexObject(object obj, int bindingHandle)` : nettoyer les index lors de Unregister
  - `FindByOpenGLHandle<T>(uint openGLHandle)` : recherche O(1) par OpenGLHandle
  - `FindByName<T>(string name)` : recherche O(1) par nom
  - `GetByBindingHandle<T>(int bindingHandle)` : récupération O(1) par BindingHandle
- **Optimisations** :
  - Lazy initialization : index créés seulement si utilisés
  - Limite de longueur : noms > 256 chars ne sont pas indexés (économie mémoire)

4. **Modifier `Register(object obj)`** :

- Valider que `obj != null` (protection contre erreurs)
- Générer un BindingHandle unique avec `SafeIncrement(ref _nextBindingHandle)` (pair, commence à 2000, protection overflow)
- Générer un handle principal avec `SafeIncrement(ref _nextHandle)` (impair, commence à 1001, protection overflow)
- Obtenir un `HandleEntry` depuis le pool avec `GetOrCreateEntry(obj, bindingHandle)` (évite allocation GC)
- Ajouter le handle principal à `_handles` dans l'entry
- Enregistrer dans `_entries[handle] = entry`
- Enregistrer dans `_bindingToEntry[bindingHandle] = handle`
- Invalider le cache `GetAllObjects()` avec `Interlocked.Increment(ref _cacheVersion)`
- Incrémenter compteur de performance `Interlocked.Increment(ref _registerCount)`
- **Note** : L'indexation secondaire (OpenGLHandle, nom) sera faite par les systèmes spécifiques après Register()
- Retourner le handle principal

5. **Ajouter `CopyHandle(int handle)`** :

- Valider le handle avec `IsValidHandle(handle)` (vérification précoce)
- Vérifier que le handle existe avec `_entries.TryGetValue(handle, out var entry)`
- Si non trouvé, retourner 0
- **Lock sur l'entry avec timeout** pour atomicité complète :
  - Utiliser `entry.TryLock()` avec timeout (protection deadlock)
  - Si timeout, retourner 0 et logger un warning
  - Dans le lock :
    - Appeler `entry.IncrementRef()` (atomique via Interlocked)
    - Générer un nouveau handle avec `SafeIncrement(ref _nextHandle)` (protection overflow)
    - Appeler `entry.AddHandle(newHandle)` (protégé par lock)
    - Enregistrer dans `_entries[newHandle] = entry` (même entry, nouveau handle)
- Incrémenter compteur de performance `Interlocked.Increment(ref _copyHandleCount)`
- Retourner le nouveau handle
- **Thread-safety** : Le lock sur l'entry avec timeout garantit atomicité et détecte deadlocks

6. **Modifier `Unregister(int handle)`** :

- Valider le handle avec `IsValidHandle(handle)` (vérification précoce)
- Vérifier que le handle existe avec `_entries.TryGetValue(handle, out var entry)`
- Si non trouvé, retourner immédiatement (protection contre double Unregister)
- **Lock sur l'entry avec timeout** pour atomicité complète :
  - Utiliser `entry.TryLock()` avec timeout (protection deadlock)
  - Si timeout, logger un warning et retourner
  - Dans le lock :
    - Vérifier que le handle existe toujours dans l'entry avec `entry.RemoveHandle(handle)`
    - Si `RemoveHandle()` retourne false, le handle a déjà été retiré, retourner (protection double Unregister)
    - Appeler `entry.DecrementRef()` (atomique via Interlocked)
    - Si `ReferenceCount <= 0` :
      - Obtenir tous les handles avec `entry.GetAllHandles()` (snapshot thread-safe via ArrayPool)
      - **Batch removal** : retirer tous les handles de `_entries` en une fois
      - Retirer le BindingHandle de `_bindingToEntry`
      - Appeler `UnindexObject(entry.Object, entry.BindingHandle)` pour nettoyer les index secondaires
      - Retourner l'entry au pool avec `ReturnToPool(entry)` (réutilisation)
      - Invalider le cache `GetAllObjects()` avec `Interlocked.Increment(ref _cacheVersion)`
    - Sinon, l'entry reste dans `_entries` pour les autres handles
- Incrémenter compteur de performance `Interlocked.Increment(ref _unregisterCount)`
- **Performance** : Batch removal évite les parcours multiples, lock sur entry évite les race conditions, pool évite allocations

7. **Ajouter `GetBindingHandle(int handle)`** :

- Valider le handle avec `IsValidHandle(handle)` (vérification précoce)
- Utiliser `_entries.TryGetValue(handle, out var entry)`
- Si trouvé, retourner `entry.BindingHandle`
- Sinon, retourner 0

8. **Conserver et améliorer les méthodes existantes** :

- `Get<T>(int handle)` :
  - Valider le handle avec `IsValidHandle(handle)` (vérification précoce)
  - Récupérer l'objet depuis l'entry (thread-safe via ConcurrentDictionary)
  - Valider le type avec `is T` et logger un warning en DEBUG si type mismatch
  - Incrémenter compteur `Interlocked.Increment(ref _getCount)`
- `Exists(int handle)` :
  - Valider le handle avec `IsValidHandle(handle)` (vérification précoce)
  - Vérifier si le handle existe (thread-safe via ConcurrentDictionary.ContainsKey)
- `GetReferenceCount(int handle)` :
  - Valider le handle avec `IsValidHandle(handle)`
  - Retourner `entry.ReferenceCount` (propriété thread-safe)
- `GetByBindingHandle<T>(int bindingHandle)` :
  - Valider le BindingHandle avec `IsValidBindingHandle(bindingHandle)`
  - Recherche O(1) via `_bindingToEntry` puis `Get<T>()`

9. **Ajouter méthodes de recherche optimisées O(1)** :

- `FindByOpenGLHandle<T>(uint openGLHandle)` :
  - Vérifier que `openGLHandle != 0`
  - Recherche via `GetOpenGLHandleIndex()` (lazy) puis `GetByBindingHandle<T>()`
- `FindByName<T>(string name)` :
  - Vérifier que `!string.IsNullOrEmpty(name)`
  - Normaliser en lowercase et vérifier longueur <= 256 chars
  - Recherche via `GetNameIndex()` (lazy) puis `GetByBindingHandle<T>()`
- Ces méthodes remplacent les recherches O(n) dans TextureSystem, MaterialSystem, ModelSystem

10. **Ajouter méthodes de debugging et monitoring** :

- `GetAllObjects()` :
  - Utiliser cache avec versioning pour éviter reconstructions répétées
  - Si cache valide (`_lastCacheVersion == _cacheVersion`), retourner cache directement (O(1))
  - Sinon, reconstruire cache depuis `_bindingToEntry` (O(n) mais mis en cache)
- `GetUniqueObjectCount()` : retourner `_bindingToEntry.Count` (O(1))
- `GetLeakedObjectCount()` : comparer objets morts (WeakReference) vs entries actives (optionnel, pour diagnostic)
- `PerformanceMetrics` (classe statique) :
  - `RegisterCount` : nombre total de Register() appelés
  - `CopyHandleCount` : nombre total de CopyHandle() appelés
  - `UnregisterCount` : nombre total de Unregister() appelés
  - `GetCount` : nombre total de Get() appelés
  - `ActiveHandles` : nombre de handles actifs
  - `ActiveObjects` : nombre d'objets uniques actifs
  - `PoolSize` : taille du pool HandleEntry
- `HealthCheck(out string errorMessage)` :
  - Vérifier cohérence entre `_entries` et `_bindingToEntry`
  - Vérifier que tous les handles pointent vers des entries valides
  - Vérifier que tous les BindingHandles ont des entries correspondantes
  - Vérifier que les index sont cohérents
  - Retourner false avec message d'erreur si incohérence détectée

11. **Ajouter méthodes utilitaires de validation** :

- `IsValidHandle(int handle)` : vérifier que handle >= MIN_HANDLE, impair, et < MAX_HANDLE
- `IsValidBindingHandle(int bindingHandle)` : vérifier que bindingHandle >= MIN_BINDING_HANDLE, pair, et < MAX_HANDLE
- `SafeIncrement(ref int value, int maxValue)` : Increment avec protection overflow et réinitialisation si nécessaire

### Points d'attention - Performance

- **Thread-safety** :
  - `ConcurrentDictionary` pour tous les dictionnaires (thread-safe par design)
  - `Interlocked.Increment/Decrement` pour `_referenceCount` (opérations atomiques)
  - Lock sur `HandleEntry` avec timeout pour opérations complexes (CopyHandle, Unregister)
  - Snapshot avec `ArrayPool<int>` pour `GetAllHandles()` : évite allocations répétées
  - Timeout sur locks : détection de deadlocks potentiels
- **Performance** :
  - Dictionnaire inverse `_bindingToEntry` : recherche O(1) au lieu de O(n)
  - Index secondaires `_openGLHandleIndex` et `_nameIndex` : recherches O(1) au lieu de O(n)
  - Lazy initialization des index : économie mémoire si non utilisés
  - Batch removal dans `Unregister()` : évite les parcours multiples
  - Lock sur entry (pas global) : minimise la contention
  - Pool d'objets HandleEntry : évite allocations GC répétées
  - Cache pour `GetAllObjects()` : O(1) si cache valide, O(n) seulement si invalidation
  - Validation précoce des handles : évite recherches inutiles dans dictionnaires
  - ArrayPool pour snapshots : réduction allocations temporaires
- **Espace de noms** :
  - BindingHandles pairs (2000+), handles normaux impairs (1001+)
  - Évite les collisions entre handles et BindingHandles
  - Protection contre integer overflow avec `SafeIncrement()` et réinitialisation
- **Compatibilité** :
  - Garder la signature de `Register()` identique pour migration progressive
  - Les index secondaires sont optionnels (appelés après Register par les systèmes)
- **Monitoring** :
  - Métriques de performance intégrées pour suivi des opérations
  - Health check automatique pour détection d'incohérences

### Points d'attention - Sécurité

- **Race conditions** :
  - Lock sur entry avec timeout pour `CopyHandle()` et `Unregister()` : garantit atomicité et détecte deadlocks
  - `TryRemove()` atomique dans `Unregister()` : évite double libération
  - Vérification avec `RemoveHandle()` : protection contre double Unregister
  - Snapshot pattern avec ArrayPool : évite modifications pendant itération
- **Thread-safety de HandleEntry** :
  - `HashSet<int> _handles` avec capacité initiale 4, protégé par lock (pas thread-safe par défaut)
  - `int _referenceCount` avec `Interlocked` pour opérations atomiques
  - Propriété `ReferenceCount` en lecture seule (snapshot thread-safe)
  - `TryLock()` avec timeout : protection contre deadlocks
- **Protection contre erreurs** :
  - Validation précoce des handles avec `IsValidHandle()` et `IsValidBindingHandle()`
  - Vérifications `TryGetValue()` avant chaque opération
  - Retour de valeurs par défaut (0, null) si handle invalide
  - Pas d'exceptions pour handles invalides (comportement gracieux)
  - Protection contre integer overflow avec `SafeIncrement()` et réinitialisation
  - Validation des types dans `Get<T>()` avec logging optionnel en DEBUG
- **Protection contre corruption** :
  - Health check automatique pour détecter incohérences
  - Validation de cohérence entre dictionnaires
  - Métriques de performance pour monitoring

## Étape 2 : Migrer TextureSystem (TDD)

**Fichier** : `src/Sbox.Engine.Emulation/Texture/TextureSystem.cs`
**Tests** : `tests/Sbox.Engine.Emulation.Tests/Texture/TextureSystemTests.cs`

### Cycle TDD 2.1 : Migration CreateTextureWithOpenGLHandle()

**RED** : Écrire les tests :
- `CreateTextureWithOpenGLHandle()` utilise `HandleManager.Register()`
- Retourne handle valide, TextureData accessible via HandleManager
- Enregistre OpenGLHandle dans l'index secondaire
- `CTextureBase_DestroyStrongHandle()` appelle `HandleManager.Unregister()`
- `CTextureBase_CopyStrongHandle()` appelle `HandleManager.CopyHandle()`
- `CTextureBase_IsStrongHandleValid()` utilise `HandleManager.Exists()`
- `CTextureBase_GetBindingPtr()` utilise `HandleManager.GetBindingHandle()`

**GREEN** : Migrer vers HandleManager, supprimer `_nextTextureId` et `_textureHandles`

**REFACTOR** : Nettoyer code obsolète, optimiser indexation

### Cycle TDD 2.2 : Migration CreateTextureFromResourceHelper()

**RED** : Écrire les tests :
- `CreateTextureFromResourceHelper()` utilise HandleManager
- Enregistre nom dans l'index secondaire
- Recherche existante via `FindByName()` fonctionne

**GREEN** : Migrer vers HandleManager avec indexation par nom

**REFACTOR** : Optimiser recherche existante

### Modifications détaillées

1. **Supprimer** :

- `private static int _nextTextureId = 1000000;`
- `internal static readonly Dictionary<IntPtr, TextureData> _textureHandles = new();`
- `ReferenceCount` dans `TextureData` (géré par HandleManager)

2. **Modifier `CreateTextureWithOpenGLHandle()`** :

- Utiliser `HandleManager.Register(textureData)` pour obtenir le handle
- Stocker le handle dans `TextureData.BindingPtr` via `HandleManager.GetBindingHandle()`
- Retourner `(IntPtr)handle` au lieu de `(IntPtr)_nextTextureId++`
- Supprimer la recherche dans `_textureHandles`, utiliser `HandleManager.Get<TextureData>()` à la place

3. **Modifier `CreateTextureFromResourceHelper()`** :

- Même pattern que `CreateTextureWithOpenGLHandle()`

4. **Modifier `CTextureBase_DestroyStrongHandle()`** :

- Appeler `HandleManager.Unregister((int)self)` directement
- Supprimer la gestion manuelle de `ReferenceCount`

5. **Modifier `CTextureBase_CopyStrongHandle()`** :

- Appeler `HandleManager.CopyHandle((int)self)`
- Retourner `(IntPtr)newHandle`
- Supprimer la gestion manuelle de `ReferenceCount`

6. **Modifier `CTextureBase_IsStrongHandleValid()`** :

- Utiliser `HandleManager.Exists((int)self)`

7. **Modifier `CTextureBase_GetBindingPtr()`** :

- Utiliser `HandleManager.GetBindingHandle((int)self)`

8. **Modifier `GetTextureData()`** :

- Utiliser `HandleManager.Get<TextureData>((int)textureHandle)`

### Modifications supplémentaires

9. **Utiliser les index secondaires pour recherches O(1)** :

- Dans `CreateTextureWithOpenGLHandle()` :
  - Après `HandleManager.Register(textureData)`, appeler `HandleManager.RegisterOpenGLHandleIndex(openGLHandle, bindingHandle)`
  - Utiliser `HandleManager.FindByOpenGLHandle<TextureData>(openGLHandle)` pour recherche existante
- Dans `CreateTextureFromResourceHelper()` :
  - Après `HandleManager.Register(textureData)`, appeler `HandleManager.RegisterNameIndex(resourceName, bindingHandle)`
  - Utiliser `HandleManager.FindByName<TextureData>(resourceName)` pour recherche existante

### Points d'attention

- **Recherche par OpenGLHandle** : Utiliser `HandleManager.FindByOpenGLHandle<TextureData>()` (O(1) au lieu de O(n))
- **Recherche par nom** : Utiliser `HandleManager.FindByName<TextureData>()` (O(1) au lieu de O(n))
- **Thread-safety** : HandleManager est thread-safe, les index aussi (ConcurrentDictionary)
- **Performance** : Recherches O(1) grâce aux index secondaires, nettoyage automatique lors de Unregister
- **Indexation** : Doit être faite après Register() pour que le BindingHandle soit disponible

## Étape 3 : Migrer MaterialSystem (TDD)

**Fichier** : `src/Sbox.Engine.Emulation/Material/MaterialSystem.cs`
**Tests** : `tests/Sbox.Engine.Emulation.Tests/Material/MaterialSystemTests.cs`

### Cycle TDD 3.1 : Migration fonctions de création/destruction

**RED** : Écrire les tests :
- Toutes les fonctions de création utilisent `HandleManager.Register()`
- `IMaterial2_DestroyStrongHandle()` appelle `HandleManager.Unregister()`
- `IMaterial2_CopyStrongHandle()` appelle `HandleManager.CopyHandle()`
- `IMaterial2_IsStrongHandleValid()` utilise `HandleManager.Exists()`
- `IMaterial2_GetBindingPtr()` utilise `HandleManager.GetBindingHandle()`
- Indexation par nom fonctionne

**GREEN** : Migrer toutes les fonctions vers HandleManager, supprimer dictionnaires locaux

**REFACTOR** : Nettoyer code obsolète, optimiser indexation

### Modifications détaillées

1. **Supprimer** :

- `private static int _nextMaterialId = 1;`
- `private static readonly Dictionary<IntPtr, MaterialData> _materials = new();`
- `private static Dictionary<IntPtr, MaterialData> _materialHandles = new Dictionary<IntPtr, MaterialData>();`
- `ReferenceCount` dans `MaterialData` (géré par HandleManager)

2. **Modifier toutes les fonctions de création** :

- `g_pMtrlSystm2_CreateRawMaterial()`
- `g_pMtrlSystm2_FindOrCreateMaterialFromResource()`
- `FindOrCreateMaterialFromResourceHelper()`
- `g_pMtrlSystm2_CreateProceduralMaterialCopy()`
- Utiliser `HandleManager.Register(materialData)` au lieu de `_nextMaterialId++`
- Utiliser `HandleManager.GetBindingHandle()` pour `BindingPtr`

3. **Modifier `IMaterial2_DestroyStrongHandle()`** :

- Appeler `HandleManager.Unregister((int)self)` directement
- Libérer aussi `MaterialMode` si nécessaire (via HandleManager)

4. **Modifier `IMaterial2_CopyStrongHandle()`** :

- Appeler `HandleManager.CopyHandle((int)self)`

5. **Modifier toutes les fonctions de recherche** :

- Remplacer `_materials.TryGetValue()` par `HandleManager.Get<MaterialData>((int)self)`
- Remplacer `_materialHandles.TryGetValue()` par `HandleManager.Get<MaterialData>((int)self)`

6. **Modifier `IMaterial2_IsStrongHandleValid()`** :

- Utiliser `HandleManager.Exists((int)self)`

7. **Modifier `IMaterial2_GetBindingPtr()`** :

- Utiliser `HandleManager.GetBindingHandle((int)self)`

### Modifications supplémentaires

8. **Utiliser les index secondaires pour recherches O(1)** :

- Dans toutes les fonctions de création :
  - Après `HandleManager.Register(materialData)`, appeler `HandleManager.RegisterNameIndex(materialName, bindingHandle)`
  - Utiliser `HandleManager.FindByName<MaterialData>(name)` pour recherche existante

### Points d'attention

- **Recherche par nom** : Utiliser `HandleManager.FindByName<MaterialData>()` (O(1) au lieu de O(n))
- **MaterialMode** : Continuer à utiliser HandleManager pour MaterialMode (pattern identique)
- **RenderAttributes** : Pas de changement, déjà géré par HandleManager
- **Double dictionnaire** : `_materials` et `_materialHandles` étaient identiques, maintenant un seul système unifié
- **Indexation** : Doit être faite après Register() pour que le BindingHandle soit disponible

## Étape 4 : Migrer ModelSystem (TDD)

**Fichier** : `src/Sbox.Engine.Emulation/Model/ModelSystem.cs`
**Tests** : `tests/Sbox.Engine.Emulation.Tests/Model/ModelSystemTests.cs`

### Cycle TDD 4.1 : Migration ModelSystem

**RED** : Écrire les tests :
- `CreateModelFromResourceHelper()` utilise `HandleManager.Register()`
- Toutes les fonctions `CModel_*` utilisent HandleManager
- Indexation par nom fonctionne

**GREEN** : Migrer vers HandleManager, supprimer dictionnaires locaux

**REFACTOR** : Nettoyer code obsolète, optimiser indexation

### Modifications détaillées

1. **Supprimer** :

- `private static int _nextModelId = 2000000;`
- `internal static readonly Dictionary<IntPtr, ModelData> _modelHandles = new();`
- `ReferenceCount` dans `ModelData` (géré par HandleManager)

2. **Modifier `CreateModelFromResourceHelper()`** :

- Utiliser `HandleManager.Register(modelData)` au lieu de `_nextModelId++`
- Utiliser `HandleManager.GetBindingHandle()` pour `BindingPtr`

3. **Modifier toutes les fonctions CModel_*** :

- `CModel_DestroyStrongHandle()` : `HandleManager.Unregister()`
- `CModel_CopyStrongHandle()` : `HandleManager.CopyHandle()`
- `CModel_IsStrongHandleValid()` : `HandleManager.Exists()`
- `CModel_GetBindingPtr()` : `HandleManager.GetBindingHandle()`
- Toutes les autres : `HandleManager.Get<ModelData>()`

### Modifications supplémentaires

4. **Utiliser les index secondaires pour recherches O(1)** :

- Dans `CreateModelFromResourceHelper()` :
  - Après `HandleManager.Register(modelData)`, appeler `HandleManager.RegisterNameIndex(resourceName, bindingHandle)`
  - Utiliser `HandleManager.FindByName<ModelData>(resourceName)` pour recherche existante

### Points d'attention

- **Pattern identique** : Même pattern que TextureSystem, migration plus simple
- **Recherche par nom** : Utiliser `HandleManager.FindByName<ModelData>()` (O(1) au lieu de O(n))
- **Indexation** : Doit être faite après Register() pour que le BindingHandle soit disponible

## Étape 5 : Vérifier RenderDevice

**Fichier** : `src/Sbox.Engine.Emulation/Rendering/RenderDevice.cs`

### Vérifications

1. **Sampler states** : RenderDevice utilise déjà HandleManager directement, mais avec un système custom (`_samplerHandles`, `_nextSamplerId`)
2. **Décision** : 

- Option A : Migrer aussi vers le nouveau HandleManager (cohérence totale)
- Option B : Garder le système actuel si `CopyStrongHandle` n'est pas nécessaire pour sampler states

3. **Recommandation** : Option A pour cohérence, mais vérifier si `CopyStrongHandle` existe pour sampler states

### Points d'attention

- **Cache `_samplerCache`** : Peut être conservé pour éviter les créations OpenGL redondantes
- **BindlessIndex** : Spécifique aux sampler states, à conserver dans `SamplerStateData`

## Étape 6 : Tests d'intégration et Validation (TDD)

**Tests** : `tests/Sbox.Engine.Emulation.Tests/Integration/HandleManagerIntegrationTests.cs`

### Cycle TDD 6.1 : Tests d'intégration complets

**RED** : Écrire les tests d'intégration :
- Scénarios complets : créer texture → copier handle → détruire les deux
- Scénarios multi-systèmes : texture + matériau + modèle
- Vérifier que les recherches par nom/OpenGLHandle fonctionnent (O(1))
- Vérifier lazy initialization des index
- Vérifier nettoyage des index lors de Unregister()

**GREEN** : Exécuter tous les tests, corriger les bugs découverts

**REFACTOR** : Optimisations finales, améliorer code

### Tests à effectuer (complémentaires)

1. **Tests unitaires** :

- Créer un objet, vérifier handle valide
- Copier un handle, vérifier ReferenceCount = 2
- Détruire un handle, vérifier ReferenceCount = 1
- Détruire le dernier handle, vérifier objet libéré et entry retournée au pool
- Vérifier BindingPtr identique pour handles copiés
- Tester validation des handles : handles invalides (0, négatifs, pairs pour handles normaux)
- Tester protection overflow : vérifier réinitialisation des compteurs
- Tester pool d'objets : vérifier réutilisation des HandleEntry

2. **Tests d'intégration** :

- Créer une texture, copier le handle, détruire les deux
- Créer un matériau, copier le handle, vérifier GetBindingPtr()
- Vérifier que les recherches par nom/OpenGLHandle fonctionnent (O(1))
- Vérifier lazy initialization des index : index créés seulement si utilisés
- Vérifier nettoyage des index lors de Unregister()

3. **Tests de performance** :

- Mesurer le temps de `Unregister()` avec beaucoup de handles (vérifier batch removal)
- Vérifier que le dictionnaire inverse améliore les performances (comparaison avant/après)
- Mesurer les recherches par OpenGLHandle/nom : O(1) vs O(n) précédent
- Tests de charge : créer/copier/détruire 10000 handles, mesurer temps total
- Tests de contention : opérations concurrentes sur mêmes handles, vérifier thread-safety
- Mesurer impact du pool d'objets : allocations GC avant/après
- Mesurer impact du cache GetAllObjects() : temps avec cache valide vs reconstruction
- Mesurer impact ArrayPool : allocations pour GetAllHandles()

4. **Tests de sécurité** :

- Tests multi-thread : créer/copier/détruire handles depuis plusieurs threads simultanément
- Tests de race conditions : vérifier qu'aucune corruption de données
- Tests de double Unregister : vérifier qu'aucune exception ou corruption
- Tests de memory leaks : vérifier que tous les objets sont libérés quand ReferenceCount = 0
- Tests de timeout locks : simuler deadlock et vérifier détection
- Tests de validation handles : vérifier rejet des handles invalides
- Tests de health check : créer incohérence artificielle et vérifier détection
- Tests de type safety : vérifier validation des types dans Get<T>()

5. **Tests de monitoring** :

- Vérifier que les métriques de performance sont correctes
- Vérifier que HealthCheck() détecte les incohérences
- Tests de charge avec monitoring : vérifier pas de dégradation avec métriques actives

## Commandes de Test

```bash
# Exécuter tous les tests
dotnet test

# Exécuter tests spécifiques
dotnet test --filter "FullyQualifiedName~HandleManagerTests"

# Exécuter avec couverture
dotnet test /p:CollectCoverage=true

# Exécuter en mode watch (re-exécute automatiquement)
dotnet watch test
```

## Avantages du TDD pour ce projet

1. **Confiance** : Chaque fonctionnalité est testée avant d'être utilisée
2. **Documentation vivante** : Les tests documentent le comportement attendu
3. **Détection précoce** : Les bugs sont détectés immédiatement
4. **Refactoring sécurisé** : On peut refactorer en toute confiance
5. **Progression mesurable** : Le nombre de tests qui passent mesure la progression

## Points d'attention globaux

### Performance

- **Dictionnaire inverse** : Critique pour éviter O(n) dans `Unregister()`, recherche O(1)
- **Index secondaires** : Recherches O(1) pour OpenGLHandle et nom (au lieu de O(n))
- **Lazy initialization** : Index créés seulement si utilisés (économie mémoire)
- **Batch operations** : Unregister() libère tous les handles en une fois (évite parcours multiples)
- **Lock granularité** : Lock sur entry individuelle (pas global), minimise contention
- **Pool d'objets** : HandleEntry réutilisés, évite allocations GC répétées
- **Cache GetAllObjects()** : O(1) si cache valide, O(n) seulement si invalidation
- **ArrayPool** : Réduction allocations temporaires pour snapshots
- **Validation précoce** : Rejet des handles invalides avant recherche dans dictionnaires
- **Thread-safety** : Toutes les opérations thread-safe via ConcurrentDictionary + Interlocked + locks avec timeout
- **Snapshot pattern** : `GetAllHandles()` via ArrayPool pour éviter modifications pendant itération

### Compatibilité

- **Migration progressive** : Possible de migrer module par module
- **Signatures** : Garder les signatures des fonctions publiques identiques
- **BindingPtr** : Doit rester fonctionnel pour compatibilité Source 2

### Mémoire

- **Libération** : Vérifier que tous les objets sont bien libérés quand ReferenceCount = 0
- **BindingHandle** : Doit être libéré en même temps que l'objet
- **Handles multiples** : Tous les handles doivent être libérés ensemble

### Debugging

- **GetAllObjects()** : Utile pour détecter les fuites mémoire, avec cache pour performance
- **GetReferenceCount()** : Utile pour debugging
- **GetUniqueObjectCount()** : Utile pour statistiques
- **PerformanceMetrics** : Compteurs de performance pour monitoring (RegisterCount, CopyHandleCount, etc.)
- **HealthCheck()** : Vérification automatique de cohérence des données
- **Validation handles** : Détection précoce des handles invalides
- **Type validation** : Logging optionnel en DEBUG pour détecter erreurs de type

## Risques et Mitigation

1. **Risque** : Performance dégradée si beaucoup de handles

- **Mitigation** :
  - Dictionnaire inverse `_bindingToEntry` : recherche O(1)
  - Index secondaires `_openGLHandleIndex` et `_nameIndex` : recherches O(1)
  - Batch removal dans Unregister() : évite parcours multiples
  - Lock sur entry individuelle : minimise contention vs lock global
  - Snapshot pattern : évite modifications pendant itération

2. **Risque** : Fuites mémoire si Unregister() mal appelé

- **Mitigation** :
  - Tests unitaires complets pour vérifier libération
  - `GetAllObjects()` et `GetUniqueObjectCount()` pour détection
  - `GetLeakedObjectCount()` optionnel pour diagnostic avancé
  - Nettoyage automatique des index lors de Unregister()

3. **Risque** : Incompatibilité avec code existant

- **Mitigation** :
  - Migration progressive module par module
  - Signatures des fonctions publiques identiques
  - Tests d'intégration avant migration complète
  - Compatibilité rétroactive avec BindingPtr

4. **Risque** : Thread-safety compromise

- **Mitigation** :
  - `ConcurrentDictionary` pour tous les dictionnaires (thread-safe par design)
  - `Interlocked.Increment/Decrement` pour ReferenceCount (opérations atomiques)
  - Lock sur HandleEntry pour opérations complexes (atomicité garantie)
  - Vérifications `TryGetValue()` et `TryRemove()` atomiques
  - Tests multi-thread complets (création/copie/destruction concurrentes)

5. **Risque** : Race conditions dans CopyHandle/Unregister

- **Mitigation** :
  - Lock sur entry pour CopyHandle() : garantit atomicité complète
  - Lock sur entry pour Unregister() : garantit atomicité complète
  - Vérification avec `RemoveHandle()` : protection contre double Unregister
  - Snapshot avec `GetAllHandles()` : évite modifications pendant libération

6. **Risque** : Deadlocks avec locks imbriqués

- **Mitigation** :
  - Un seul niveau de lock (sur entry, pas global)
  - Ordre de lock cohérent (toujours entry puis dictionnaires)
  - Pas de locks imbriqués dans HandleManager
  - Timeout sur locks (1 seconde) : détection automatique de deadlocks
  - Tests de charge pour détecter deadlocks potentiels

7. **Risque** : Integer overflow dans les compteurs

- **Mitigation** :
  - `SafeIncrement()` avec vérification et réinitialisation si approche limite
  - Constantes MAX_HANDLE pour définir limites
  - Logging de warning si réinitialisation nécessaire
  - Tests de charge pour vérifier comportement avec beaucoup de handles

8. **Risque** : Allocations GC répétées (performance Raspberry Pi)

- **Mitigation** :
  - Pool d'objets HandleEntry : réutilisation au lieu de création
  - ArrayPool pour snapshots : réduction allocations temporaires
  - Capacité initiale HashSet : évite réallocations
  - Cache GetAllObjects() : évite reconstructions répétées
  - Tests de performance pour mesurer réduction allocations

9. **Risque** : Corruption de données non détectée

- **Mitigation** :
  - HealthCheck() automatique pour détecter incohérences
  - Validation des handles avant chaque opération
  - Validation des types dans Get<T>() avec logging DEBUG
  - Métriques de performance pour monitoring
  - Tests de santé réguliers

## Feuille de route TDD restante

Après le Cycle 1 (HandleEntry), voici la progression prévue pour terminer la refactorisation :

- **Cycle 1.2 – Register()** : Tests ciblant la génération atomique de handles, la réutilisation d'entrées depuis le pool et la cohérence des binding handles. Implémentation de la logique `Register()` décrite plus haut.
- **Cycle 1.3 – CopyHandle()** : Tests pour vérifier que la copie incrémente la référence et expose un nouveau handle imparitable. Verrou + ordre d'opérations dans `CopyHandle()`.
- **Cycle 1.4 – Unregister()** : Tests pour s'assurer qu'une suppression décrémente les références, déclenche le nettoyage batch et ne casse pas le pool. Implémentation finale de `Unregister()` avec batch removal et retour au pool.
- **Cycle 1.5 – Index secondaires** : Écrire des tests qui s'appuient sur `OpenGLHandle` et `Name` pour retrouver les objets en O(1). Ajouter les dictionnaires `ConcurrentDictionary<uint, int>` et `ConcurrentDictionary<string, int>`.
- **Cycle 1.6 – HealthCheck() & métriques** : Tests de cohérence (HealthCheck détecte une entrée manquante) et métriques (Register/Copy/Unregister comptent les opérations). Implémenter les compteurs atomiques et la méthode `HealthCheck()` (avec message d'erreur).

Chaque cycle suit le flow Red → Green → Refactor et se termine par l'exécution des tests ciblés (`HandleManagerTests`). Une fois les six cycles validés, reprendre les Étapes 2 à 4 (Texture/Material/Model) en réutilisant le nouveau `HandleManager`.

## Résumé des Solutions de Performance et Sécurité

### Solutions de Performance Implémentées

1. **Index secondaires O(1)** :
   - `_openGLHandleIndex` : recherche texture par OpenGLHandle en O(1)
   - `_nameIndex` : recherche par nom en O(1) (TextureSystem, MaterialSystem, ModelSystem)
   - Lazy initialization : index créés seulement si utilisés
   - Limite longueur nom : noms > 256 chars non indexés (économie mémoire)
   - Remplace les recherches O(n) précédentes

2. **Dictionnaire inverse O(1)** :
   - `_bindingToEntry` : recherche BindingHandle → handle en O(1)
   - `GetByBindingHandle<T>()` : récupération objet par BindingHandle en O(1)

3. **Batch operations** :
   - `Unregister()` libère tous les handles d'un objet en une fois
   - Évite les parcours multiples du dictionnaire

4. **Pool d'objets HandleEntry** :
   - `ConcurrentQueue<HandleEntry> _entryPool` : réutilisation des HandleEntry
   - Évite allocations GC répétées (critique pour Raspberry Pi)
   - Réduction significative des allocations dans les hot paths

5. **Cache GetAllObjects()** :
   - Cache avec versioning : O(1) si cache valide
   - Invalidation automatique lors de Register/Unregister
   - Évite reconstructions répétées (O(n) seulement si invalidation)

6. **ArrayPool pour snapshots** :
   - `ArrayPool<int>.Shared` pour `GetAllHandles()`
   - Réduction allocations temporaires pour snapshots
   - Réutilisation de buffers entre appels

7. **Validation précoce** :
   - `IsValidHandle()` et `IsValidBindingHandle()` avant recherches
   - Évite recherches inutiles dans dictionnaires pour handles invalides
   - Amélioration performance pour cas d'erreur fréquents

8. **Lock granularité optimisée** :
   - Lock sur HandleEntry individuelle (pas global)
   - Minimise la contention entre threads
   - Snapshot pattern avec ArrayPool pour éviter modifications pendant itération

### Solutions de Sécurité Implémentées

1. **Thread-safety complète** :
   - `ConcurrentDictionary` pour tous les dictionnaires (thread-safe par design)
   - `Interlocked.Increment/Decrement` pour ReferenceCount (opérations atomiques)
   - Lock sur HandleEntry avec timeout pour opérations complexes (CopyHandle, Unregister)
   - Snapshot avec `ArrayPool<int>` pour `GetAllHandles()` : thread-safety + performance
   - Timeout sur locks (1 seconde) : détection automatique de deadlocks

2. **Protection contre race conditions** :
   - Lock sur entry avec timeout pour `CopyHandle()` : garantit atomicité complète + détecte deadlocks
   - Lock sur entry avec timeout pour `Unregister()` : garantit atomicité complète + détecte deadlocks
   - Vérification avec `RemoveHandle()` : protection contre double Unregister
   - `TryGetValue()` et `TryRemove()` atomiques : évite états invalides
   - Snapshot pattern avec ArrayPool : évite modifications pendant itération

3. **Protection contre erreurs** :
   - Validation précoce des handles avec `IsValidHandle()` et `IsValidBindingHandle()`
   - Vérifications avant chaque opération (handles invalides, null, types)
   - Retour de valeurs par défaut (0, null) au lieu d'exceptions
   - Comportement gracieux pour handles invalides
   - Validation des types dans `Get<T>()` avec logging optionnel en DEBUG

4. **Protection contre corruption** :
   - `SafeIncrement()` avec protection integer overflow et réinitialisation
   - Constantes MAX_HANDLE pour définir limites
   - HealthCheck() automatique pour détecter incohérences entre dictionnaires
   - Validation de cohérence : handles → entries, BindingHandles → handles, index → BindingHandles

5. **Gestion mémoire robuste** :
   - Nettoyage automatique des index lors de Unregister()
   - Batch removal garantit libération complète
   - Pool d'objets pour réutilisation (évite allocations)
   - Méthodes de debugging pour détecter fuites mémoire
   - Métriques de performance pour monitoring

6. **Monitoring et diagnostic** :
   - `PerformanceMetrics` : compteurs pour toutes les opérations (RegisterCount, CopyHandleCount, etc.)
   - `HealthCheck()` : vérification automatique de cohérence
   - Logging optionnel en DEBUG pour détecter erreurs de type
   - Métriques actives/inactives : pas de dégradation si désactivées

### Gains de Performance Attendus

- **Recherches par propriété** : O(n) → O(1) (amélioration significative)
- **Unregister()** : O(n) → O(k) où k = nombre de handles pour l'objet (amélioration avec batch)
- **GetAllObjects()** : O(n) → O(1) si cache valide (amélioration majeure pour debugging)
- **Allocations GC** : Réduction significative grâce au pool d'objets HandleEntry (critique Raspberry Pi)
- **Allocations temporaires** : Réduction grâce à ArrayPool pour snapshots
- **Thread contention** : Réduite grâce aux locks granulaires avec timeout
- **Memory overhead** : Acceptable (index secondaires lazy, pool HandleEntry, cache optionnel)
- **Validation précoce** : Évite recherches inutiles pour handles invalides (amélioration cas d'erreur)

### Garanties de Sécurité

- **Thread-safety** : Toutes les opérations sont thread-safe
- **Atomicité** : CopyHandle() et Unregister() sont atomiques avec timeout
- **Pas de corruption** : Protection contre race conditions et double Unregister
- **Pas de deadlocks** : Un seul niveau de lock, ordre cohérent, timeout pour détection
- **Pas d'overflow** : Protection contre integer overflow avec réinitialisation
- **Validation complète** : Handles, types, et cohérence validés à chaque étape
- **Détection proactive** : HealthCheck() et métriques pour détecter problèmes avant corruption
- **Comportement gracieux** : Pas d'exceptions pour erreurs utilisateur, retour valeurs par défaut
