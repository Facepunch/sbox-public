# Note d’architecture cross‑plateforme (libengine2 / Sandbox.Engine.Emulation)

## État et blocages actuels
- **NativeAOT + net10.0-android** : non supporté (NETSDK1207). Impossible de publier l’émulation en AOT pour Android avec le SDK actuel.
- **Packaging Android** : XA4301 si une `.so` (libengine2) n’est pas rangée dans un dossier ABI (`runtimes/android-arm64/native/` ou `libs/arm64-v8a/`, etc.). Le build standard ne génère pas ce layout automatiquement.
- **Desktop** : AOT OK (linux-x64) et harness GL fonctionne.

## Stratégie par plateforme
- **Desktop (Linux/Win/macOS)** : conserver NativeAOT (`net10.0`, RIDs desktop), produire `libengine2.so/.dll/.dylib` et la copier vers `game/bin/...` et `runtimes/<rid>/native/` pour empaquetage.
- **Android** :
  - Compiler en **non-AOT** (`PublishAot=false` pour `net10.0-android`), tout en gardant la lib native si nécessaire.
  - Placer la `.so` Android dans `runtimes/android-arm64/native/libengine2.so` (ou `AndroidNativeLibrary Include="...libengine2.so" Abi="arm64-v8a"`), sinon l’exclure si on ne charge pas encore la lib.
  - Référencer `Sandbox.Engine.Emulation` avec condition TFM (`net10.0-android`) sans AOT.
- **iOS** : similaire à Android (pas d’AOT dispo via NativeAOT ; dépend de la toolchain Xamarin/iOS/Mono).

## Propositions de layout/projets
- `Sandbox.Engine.Emulation.csproj`
  - `TargetFrameworks`: `net10.0;net10.0-android`
  - `RuntimeIdentifiers`: `linux-x64;win-x64;osx-arm64;android-arm64` (adapter selon besoin).
  - Conditionner `PublishAot=true` uniquement sur desktop, `PublishAot=false` sur Android.
  - Si lib native Android : post-target (publish) qui copie `libengine2.so` vers `runtimes/android-arm64/native/`.
- `OpenEngine2Android`
  - Si on veut charger la lib native : utiliser `AndroidNativeLibrary` pour déposer la `.so` dans `arm64-v8a`.
  - Sinon, exclure temporairement la référence à la lib native pour tester le harness GL ES (triangle).

## Actions concrètes à prévoir
1) **csproj émulation** : ajouter PropertyGroup conditionnelle `TargetFramework == net10.0-android` avec `PublishAot=false`. Garder AOT pour desktop.
2) **Packaging libengine2** :
   - Desktop : copier la sortie NativeAOT vers `runtimes/<rid>/native/`.
   - Android : si la lib est requise, fournir une build spécifique (NDK/Toolchain) et la placer dans `runtimes/android-arm64/native/` ou `AndroidNativeLibrary`.
3) **Workloads/outils** : installer `dotnet workload install android` pour tooling ; NativeAOT Android reste indisponible → rester en mode non-AOT.
4) **Build/publish pipeline** :
   - Desktop : `dotnet publish -f net10.0 -r linux-x64/win-x64/osx-arm64 /p:PublishAot=true /p:NativeLib=Shared /p:SelfContained=true`.
   - Android : `dotnet build/publish -f net10.0-android -r android-arm64` (sans PublishAot). Ajouter la `.so` si disponible.
5) **Harness tests** :
   - Desktop : triangle GL (ok).
   - Android : triangle GL ES (ok) ; si lib native ajoutée, vérifier chargement via `System.loadLibrary` / `AndroidNativeLibrary`.

## Points à surveiller
- NativeAOT Android non supporté : ne pas forcer `/p:PublishAot=true` sur `net10.0-android`.
- Emplacement des `.so` pour Android (ABI folders) pour éviter XA4301.
- Eventuelle duplication de publish (desktop vs android) à séparer via conditions MSBuild.

