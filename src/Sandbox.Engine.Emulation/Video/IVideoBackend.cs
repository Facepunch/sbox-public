using System;

namespace Sandbox.Engine.Emulation.Video;

/// <summary>
/// Backend d'implémentation vidéo dépendant plateforme.
/// </summary>
public interface IVideoBackend : IDisposable
{
    /// <summary>Ouvre une ressource vidéo et prépare le décodage.</summary>
    /// <returns>true si ouvert.</returns>
    bool Open(string url, string extension, VideoPlayer.VideoPlayerData data);

    /// <summary>Mise à jour par frame : décodage/avance et upload texture/audio.</summary>
    void Update(VideoPlayer.VideoPlayerData data);

    /// <summary>Met en pause.</summary>
    void Pause(VideoPlayer.VideoPlayerData data);

    /// <summary>Reprend.</summary>
    void Resume(VideoPlayer.VideoPlayerData data);

    /// <summary>Stoppe et libère les ressources du flux ouvert.</summary>
    void Stop(VideoPlayer.VideoPlayerData data);

    /// <summary>Seek en secondes.</summary>
    void Seek(VideoPlayer.VideoPlayerData data, double timeSeconds);

    /// <summary>Obtient le spectre (256 valeurs) dans le buffer fourni.</summary>
    void FillSpectrum(VideoPlayer.VideoPlayerData data, float[] target);

    /// <summary>Amplitude instantanée.</summary>
    float GetAmplitude(VideoPlayer.VideoPlayerData data);
}

