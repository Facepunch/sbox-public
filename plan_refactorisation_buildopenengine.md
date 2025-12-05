# Plan de Refactorisation : Intégration du Moteur Open Source dans le Build System

## Objectif

Faciliter la collaboration en permettant aux développeurs de choisir facilement entre le moteur Source 2 natif (via artefacts téléchargés) et le moteur open source (`Sbox.Engine.Emulation`). Le système doit être automatique, documenté et ne pas perturber le workflow existant.

## Contexte Actuel

### Système de Build Actuel

1. **Détection Public Source** : `IsPublicSourceDistribution()` vérifie l'existence de `public/` et `steamworks/`
   - Si public source → télécharge les artefacts depuis `artifacts.sbox.game`
   - Si non public → build natif complet

2. **Chargement du Moteur** : `NativeInterop.Initialize()` charge `libengine2.so` via `NativeLibrary.TryLoad()`
   - Chemin : `NetCore.NativeDllPath` + `definitions.NativeDll` (généré par InteropGen)
   - Pour engine : `bin/linuxsteamrt64/libengine2.so` (Linux) ou `bin/win64/engine2.dll` (Windows)

3. **Solution Temporaire Actuelle** :
   - Build manuel : `dotnet publish src/Sbox.Engine.Emulation/...`
   - Export avec nom `libopenengine2.so` (configuré dans le projet)
   - Copie manuelle : `cp libopenengine2.so game/bin/linuxsteamrt64/libopenengine2.so`
   - Problème : Pas automatique, pas documenté, chargement manuel nécessaire

### Problèmes Identifiés

1. **Pas de mécanisme automatique** : Choix entre moteur natif et open source
2. **Copie manuelle nécessaire** : À chaque build, étape manuelle fastidieuse
3. **Pas de documentation** : Collaborateurs ne savent pas comment utiliser le moteur open source
4. **Pas de chargement automatique** : Le système ne charge pas automatiquement `libopenengine2.so`
5. **Pas de détection** : Le système ne sait pas quel moteur utiliser

## Solution Proposée

### Phase 1 : Variable d'Environnement et Détection (Court Terme)

#### 1.1 Variable d'Environnement

**Ajouter** : Variable `SBOX_USE_OPENSOURCE_ENGINE=1` pour activer le moteur open source

**Comportement** :
- Si définie à `1` → Utiliser le moteur open source
- Si non définie ou `0` → Utiliser le moteur natif (comportement par défaut)
- Compatible avec le workflow existant (pas de breaking change)

**Fichiers à modifier** :
- `engine/Tools/SboxBuild/Pipelines/Build.cs` : Détecter la variable
- `engine/Sandbox.Engine/Core/Interop/NetCore.cs` : Charger `libopenengine2.so` au lieu de `libengine2.so` si activé
- Documentation : `docs/OPENSOURCE_ENGINE.md`

#### 1.2 Étape de Build Automatique

**Créer** : `engine/Tools/SboxBuild/Steps/BuildOpenSourceEngine.cs`

**Responsabilités** :
- Vérifier si `SBOX_USE_OPENSOURCE_ENGINE=1`
- Si oui :
  - Build `Sbox.Engine.Emulation` avec NativeAOT
  - Publier en mode Release avec `linux-x64` et option `-o game/bin/linuxsteamrt64`
  - Le projet exporte déjà `libopenengine2.so` (configuré dans `.csproj` avec `NativeLibraryName=openengine2`)
  - Le fichier est exporté directement dans `game/bin/linuxsteamrt64/libopenengine2.so` (pas de copie nécessaire)
  - Copier aussi `libyogacore.so` depuis le build vers `game/bin/linuxsteamrt64/` si nécessaire
  - Logger les actions pour debugging
- Si non : Skip silencieusement

**Commande de Build** :
- Utiliser : `dotnet publish src/Sbox.Engine.Emulation/Sbox.Engine.Emulation.csproj -c Release -r linux-x64 /p:NativeLib=Shared /p:SelfContained=true /p:NativeLibraryName=openengine2 -o game/bin/linuxsteamrt64`
- Pattern identique à `LinuxPlatform.cs` ligne 15, avec ajout de `NativeLibraryName=openengine2`

**Intégration dans le Pipeline** :
- Ajouter dans `Build.cs` après `DownloadPublicArtifacts` (si public source)
- Ou avant `BuildNative` (si non public source)
- Ordre important : Doit être avant que le moteur soit utilisé

**Dépendances** :
- `.NET 10 SDK` (déjà requis)
- `NativeAOT` (déjà configuré dans le projet)
- `gcc` pour libyogacore (déjà requis)

**Configuration du Projet** :
- Le projet `Sbox.Engine.Emulation.csproj` doit être configuré pour exporter `libopenengine2.so`
- Ajouter dans le `.csproj` : `<PropertyGroup><NativeLibraryName>openengine2</NativeLibraryName></PropertyGroup>`
- Ou utiliser `dotnet publish` avec l'option `-p:NativeLibraryName=openengine2`
- Sur Linux, cela génère automatiquement `libopenengine2.so` (préfixe `lib` ajouté automatiquement)

**Chemin d'Export** :
- Le build doit exporter directement dans `game/bin/linuxsteamrt64/` (comme dans `LinuxPlatform.cs`)
- Utiliser l'option `-o game/bin/linuxsteamrt64` dans la commande `dotnet publish`
- Le fichier final sera : `game/bin/linuxsteamrt64/libopenengine2.so`
- Pattern identique aux autres projets : les bibliothèques natives sont exportées directement dans `game/bin/linuxsteamrt64/`
- Pas besoin de copie supplémentaire : le fichier est déjà au bon endroit après le publish
**Fichier à Modifier à la fin**:
- Documentation : `docs/OPENSOURCE_ENGINE.md`

#### 1.3 Modification du Chargement du Moteur

**Modifier** : `engine/Sandbox.Engine/Core/Interop/NetCore.cs` et `engine/Tools/InteropGen/Writer/ManagerWriter.cs`

**Changements** :
- Ajouter logique pour détecter `SBOX_USE_OPENSOURCE_ENGINE=1`
- Si activé, charger `libopenengine2.so` au lieu de `libengine2.so`
- Vérifier que `libopenengine2.so` existe avant de le charger
- Fallback vers `libengine2.so` si le moteur open source n'est pas disponible
- Documentation : `docs/OPENSOURCE_ENGINE.md`

**Avantages** :
- Les deux bibliothèques coexistent sans conflit
- Pas de risque d'écrasement
- Choix dynamique au runtime

### Phase 2 : Détection Intelligente (Moyen Terme)

#### 2.1 Détection Automatique du Moteur Disponible

**Modifier** : `engine/Sandbox.Engine/Core/Interop/NetCore.cs`

**Logique** :
1. Vérifier si `SBOX_USE_OPENSOURCE_ENGINE=1` (priorité explicite)
2. Sinon, vérifier si `libopenengine2.so` existe localement et est récent (< 1 jour)
3. Sinon, utiliser le moteur natif (`libengine2.so`)

**Avantages** :
- Détection automatique sans configuration
- Préfère le moteur open source si disponible
- Fallback gracieux vers le moteur natif
- Les deux bibliothèques coexistent sans conflit

**Implémentation** :
```csharp
internal static string GetEngineLibraryPath(string gameFolder)
{
    var nativeDllPath = NativeDllPath;
    var openSourceLib = OperatingSystem.IsLinux() ? "libopenengine2.so" : "openengine2.dll";
    var nativeLib = OperatingSystem.IsLinux() ? "libengine2.so" : "engine2.dll";
    var openSourcePath = Path.Combine(gameFolder, nativeDllPath, openSourceLib);
    var nativePath = Path.Combine(gameFolder, nativeDllPath, nativeLib);
    
    // Priorité 1 : Variable d'environnement explicite
    if (Environment.GetEnvironmentVariable("SBOX_USE_OPENSOURCE_ENGINE") == "1")
    {
        if (File.Exists(openSourcePath))
        {
            Log.Info("Using open source engine (SBOX_USE_OPENSOURCE_ENGINE=1)");
            return openSourcePath;
        }
        Log.Warning("SBOX_USE_OPENSOURCE_ENGINE=1 but libopenengine2.so not found. Falling back to native.");
    }
    
    // Priorité 2 : Détection automatique (fichier local récent)
    if (File.Exists(openSourcePath))
    {
        var fileInfo = new FileInfo(openSourcePath);
        if (fileInfo.LastWriteTime > DateTime.Now.AddDays(-1))
        {
            Log.Info("Using locally built open source engine (auto-detected)");
            return openSourcePath;
        }
    }
    
    // Priorité 3 : Moteur natif (comportement par défaut)
    Log.Info("Using native engine from artifacts");
    return nativePath;
}
```

#### 2.2 Amélioration du Logging

**Ajouter** : Messages de log clairs pour indiquer quel moteur est utilisé

**Messages** :
- `"[Build] Using open source engine: libopenengine2.so (SBOX_USE_OPENSOURCE_ENGINE=1)"`
- `"[Build] Using locally built open source engine: libopenengine2.so (auto-detected)"`
- `"[Build] Using native engine: libengine2.so (from artifacts)"`

### Phase 3 : Système de Configuration Centralisé (Long Terme)

#### 3.1 Fichier de Configuration

**Créer** : `sbox.config.json` (optionnel, à la racine du repo)

**Structure** :
```json
{
  "engine": {
    "useOpenSource": false,
    "openSourcePath": "src/Sbox.Engine.Emulation",
    "autoDetect": true
  },
  "build": {
    "skipNativeBuild": false,
    "skipArtifactDownload": false
  }
}
```

**Avantages** :
- Configuration persistante (pas besoin de variable d'environnement)
- Versionnée dans Git (collaborateurs voient la config)
- Plus flexible pour options futures

**Priorité** :
- Variable d'environnement > Fichier de config > Détection automatique

#### 3.2 Support Multi-Plateforme

**Étendre** : Support Windows et macOS (actuellement Linux uniquement)

**Changements** :
- `BuildOpenSourceEngine.cs` : Détecter la plateforme
- Le projet exporte déjà `openengine2.dll` (Windows) ou `libopenengine2.dylib` (macOS)
- Copier les fichiers selon la plateforme
- Adapter les chemins selon la plateforme

## Plan d'Implémentation

### Étape 1 : Créer BuildOpenSourceEngine.cs

**Fichier** : `engine/Tools/SboxBuild/Steps/BuildOpenSourceEngine.cs`

**Fonctionnalités** :
- Détecter `SBOX_USE_OPENSOURCE_ENGINE`
- Build `Sbox.Engine.Emulation` avec `dotnet publish` et option `-o game/bin/linuxsteamrt64`
- Le projet exporte déjà `libopenengine2.so` directement dans `game/bin/linuxsteamrt64/` (pas de copie nécessaire)
- Vérifier que `libopenengine2.so` existe dans `game/bin/linuxsteamrt64/` après le build
- Copier aussi `libyogacore.so` depuis le répertoire de build vers `game/bin/linuxsteamrt64/` si nécessaire
- Gérer les erreurs gracieusement

**Tests** :
- Test avec variable définie → Build et copie de `libopenengine2.so` réussis
- Test sans variable → Skip silencieux
- Test avec erreur de build → Log erreur, continue le pipeline

### Étape 2 : Intégrer dans Build.cs

**Fichier** : `engine/Tools/SboxBuild/Pipelines/Build.cs`

**Changements** :
- Ajouter `BuildOpenSourceEngine` après `DownloadPublicArtifacts` (si public source)
- Ou avant `BuildNative` (si non public source)
- Condition : Seulement si `SBOX_USE_OPENSOURCE_ENGINE=1`

**Ordre du Pipeline** :
1. `DownloadPublicArtifacts` (si public source, télécharge `libengine2.so` normalement)
2. `BuildOpenSourceEngine` (si `SBOX_USE_OPENSOURCE_ENGINE=1`, génère `libopenengine2.so`)
3. `InteropGen` (génère les stubs)
4. `BuildNative` (si non public source et non open source)
5. `BuildManaged` (toujours)

### Étape 3 : Modifier le Chargement du Moteur

**Fichier** : `engine/Sandbox.Engine/Core/Interop/NetCore.cs` et `engine/Tools/InteropGen/Writer/ManagerWriter.cs`

**Changements** :
- Modifier `NativeInterop.Initialize()` pour utiliser `GetEngineLibraryPath()`
- Charger `libopenengine2.so` si `SBOX_USE_OPENSOURCE_ENGINE=1` ou si détecté automatiquement
- Fallback vers `libengine2.so` si le moteur open source n'est pas disponible
- Logger quel moteur est utilisé

### Étape 4 : Améliorer NetCore.cs (Optionnel, Phase 2)

**Fichier** : `engine/Sandbox.Engine/Core/Interop/NetCore.cs`

**Changements** :
- Ajouter méthode `GetEngineLibraryPath()` pour détection intelligente
- Modifier `NativeInterop.Initialize()` pour utiliser cette méthode au lieu de `definitions.NativeDll` directement
- Ajouter logging pour indiquer quel moteur est utilisé (`libopenengine2.so` ou `libengine2.so`)

### Étape 5 : Documentation

**Créer** : `docs/OPENSOURCE_ENGINE.md`

**Contenu** :
- Introduction : Qu'est-ce que le moteur open source ?
- Prérequis : .NET 10 SDK, NativeAOT, gcc
- Activation : Comment activer avec `SBOX_USE_OPENSOURCE_ENGINE=1`
- Build : Comment builder le moteur open source
- Troubleshooting : Problèmes courants et solutions
- Contribution : Comment contribuer au moteur open source

**Mettre à jour** : `README.md`
- Ajouter section "Using Open Source Engine"
- Lien vers `docs/OPENSOURCE_ENGINE.md`

## Points d'Attention

### Compatibilité

- **Pas de breaking change** : Le comportement par défaut reste inchangé
- **Migration progressive** : Les développeurs peuvent adopter progressivement
- **Fallback gracieux** : Si le build open source échoue, utiliser le moteur natif

### Performance

- **Build time** : Le build open source ajoute ~30-60 secondes (NativeAOT)
- **Cache** : Ne rebuild que si nécessaire (vérifier timestamps)
- **Parallélisation** : Peut être fait en parallèle avec d'autres étapes

### Maintenance

- **Synchronisation** : Le moteur open source doit rester synchronisé avec les changements Source 2
- **Tests** : Vérifier que les deux moteurs fonctionnent de manière équivalente
- **Documentation** : Maintenir la documentation à jour

### Sécurité

- **Validation** : Vérifier que le fichier copié est valide (hash, taille)
- **Permissions** : S'assurer que les permissions sont correctes
- **Isolation** : Le moteur open source (`libopenengine2.so`) et le moteur natif (`libengine2.so`) coexistent sans conflit
- **Nommage** : Les deux bibliothèques ont des noms différents pour éviter toute confusion

## Tests et Validation

### Tests Unitaires

- `BuildOpenSourceEngine` : Test avec/sans variable d'environnement
- `NetCore.GetEngineLibraryPath` : Test de détection intelligente (libopenengine2.so vs libengine2.so)
- `NativeInterop.Initialize` : Test de chargement avec les deux bibliothèques

### Tests d'Intégration

- **Workflow complet** : Build avec `SBOX_USE_OPENSOURCE_ENGINE=1` → Vérifier que `libopenengine2.so` est généré et chargé
- **Workflow par défaut** : Build sans variable → Vérifier que `libengine2.so` est chargé (comportement inchangé)
- **Workflow mixte** : Build avec artefacts + open source → Vérifier que les deux bibliothèques coexistent
- **Détection automatique** : Build open source sans variable → Vérifier que `libopenengine2.so` est détecté et chargé

### Tests de Régression

- Vérifier que le build normal fonctionne toujours
- Vérifier que les artefacts téléchargés fonctionnent toujours
- Vérifier que le jeu démarre correctement avec les deux moteurs

## Risques et Mitigation

### Risque 1 : Build Open Source Échoue

**Mitigation** :
- Logger l'erreur clairement
- Continuer le pipeline (ne pas bloquer)
- Fallback vers le moteur natif si disponible
- Message d'aide pour debugging

### Risque 2 : Bibliothèque Open Source Non Trouvée

**Mitigation** :
- Vérifier que `libopenengine2.so` existe avant de le charger
- Fallback gracieux vers `libengine2.so` si non disponible
- Message d'erreur clair si le build open source a échoué
- Vérifier que le build open source s'est bien terminé avant de tenter le chargement

### Risque 3 : Incompatibilité entre Moteurs

**Mitigation** :
- Tests de compatibilité réguliers
- Documentation des différences connues
- Versioning du moteur open source

### Risque 4 : Performance de Build Dégradée

**Mitigation** :
- Cache intelligent (ne rebuild que si nécessaire)
- Build conditionnel (seulement si variable définie)
- Option pour désactiver le build automatique

## Métriques de Succès

1. **Facilité d'utilisation** : Un collaborateur peut activer le moteur open source en < 2 minutes
2. **Automatisation** : Plus besoin de copie manuelle
3. **Documentation** : Documentation complète et à jour
4. **Compatibilité** : Pas de régression sur le workflow existant
5. **Adoption** : Au moins 50% des collaborateurs utilisent le moteur open source après 1 mois

## Prochaines Étapes

1. **Immédiat** : Implémenter Phase 1 (Variable d'environnement + Build automatique)
2. **Court terme** : Documentation et tests
3. **Moyen terme** : Phase 2 (Détection intelligente)
4. **Long terme** : Phase 3 (Configuration centralisée)

## Références

- `engine/Tools/SboxBuild/Pipelines/Build.cs` : Pipeline de build principal
- `engine/Tools/SboxBuild/Steps/DownloadPublicArtifacts.cs` : Téléchargement des artefacts (pas de modification nécessaire)
- `engine/Sandbox.Engine/Core/Interop/NetCore.cs` : Initialisation du moteur
- `engine/Tools/InteropGen/Writer/ManagerWriter.cs` : Génération du code de chargement
- `src/Sbox.Engine.Emulation/Sbox.Engine.Emulation.csproj` : Projet du moteur open source (exporte `libopenengine2.so`)
- `plan.md` : Plan général du projet (ligne 3997 pour workflow actuel)

