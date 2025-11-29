# Étude de Faisabilité : Remplacement de Source 2

## Objectif
Remplacer le moteur propriétaire Source 2 par une solution open source (OpenGL/Vulkan custom ou moteur existant) tout en conservant la compatibilité avec le code managé (C#) de SBox.

## Analyse Technique
### Couplage actuel
SBox utilise une couche d'interopérabilité ([InteropGen](file:///home/hermann/Repositories/sbox-public/engine/Tools/SboxBuild/Steps/InteropGen.cs#5-13)) pour communiquer avec le moteur C++ (Source 2).
Il faut identifier :
1.  **La surface de l'API** : Combien de classes/méthodes natives sont exposées ?
2.  **La complexité des systèmes** : Rendu, Physique, Audio, Input, Réseau.

## Stratégie Optimisée : Moteur C# (NativeAOT)
Au lieu d'écrire un moteur en C++, nous allons utiliser **.NET NativeAOT** pour compiler une bibliothèque C# en code natif (`libengine2.so`).
Cela permet de satisfaire les dépendances de `Sandbox.Engine` tout en écrivant l'implémentation de notre moteur en C# moderne avec **Silk.NET**.

### Architecture
1.  **Projet `Sbox.Engine.Emulation`** : Un projet C# **.NET 10** configuré pour NativeAOT (`<PublishAot>true</PublishAot>`).
2.  **Exports** : Méthodes statiques annotées avec `[UnmanagedCallersOnly(EntryPoint = "...")]` correspondant aux signatures attendues.
3.  **Implémentation** :
    -   **Graphisme** : **Silk.NET** (OpenGL/Vulkan).
    -   **Fenêtrage/Input** : **Silk.NET** (SDL2 backend).
    -   **Audio** : **Silk.NET** (OpenAL).

### Avantages
-   **Vitesse de dév** : Tout en C#, pas de contexte switching C++/C#.
-   **Écosystème** : **Silk.NET** est la librairie officielle de la .NET Foundation pour les bindings multimédia.
-   **Performance** : NativeAOT produit du code natif optimisé.

### Plan d'action Immédiat
1.  Créer le projet `Sbox.Engine.Emulation` (.NET 10).
2.  Générer automatiquement les stubs C# à partir des fichiers [Interop](file:///home/hermann/Repositories/sbox-public/engine/Tools/SboxBuild/Steps/InteropGen.cs#5-13) existants.
3.  Implémenter [igen_engine](file:///home/hermann/Repositories/sbox-public/src/engine2/interop.engine.cpp#18151-21159) et l'initialisation basique avec Silk.NET.
4.  Compiler en `libengine2.so`.
5.  Lancer [Bootstrap.sh](file:///home/hermann/Repositories/sbox-public/Bootstrap.sh).
