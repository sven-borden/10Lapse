using TenLapse.Core.Projects;

namespace TenLapse.Presentation.Music;

/// <summary>What the Music section (#50) does with files, behind Presentation's back: the App implements it with the file picker and Media's decoder.</summary>
public interface IMusicFiles
{
    /// <summary>Asks for an audio file, starting in <paramref name="startFolder"/> (the Music folder when null or gone). Null when cancelled.</summary>
    Task<string?> PickAsync(string? startFolder);

    /// <summary>
    /// Reads what the section needs of <paramref name="path"/> (its length), off the UI thread. Throws
    /// <see cref="InvalidDataException"/> when the file does not play: no audio Windows can decode, or it cannot be read.
    /// </summary>
    Task<MusicFileInfo> ProbeAsync(string path);

    /// <summary>True when there is a file at <paramref name="path"/>. Never throws.</summary>
    bool Exists(string path);
}

/// <summary>
/// The preview's speaker (#50): plays a song from a point, at a volume, for <see cref="MusicPreview"/>. The App implements
/// it with a media player. Called on the UI thread.
/// </summary>
public interface IMusicOutput : IDisposable
{
    /// <summary>True once the song <see cref="Load"/> asked for can play.</summary>
    bool IsReady { get; }

    /// <summary>True while it plays.</summary>
    bool IsPlaying { get; }

    /// <summary>Where it is in the song.</summary>
    TimeSpan Position { get; }

    /// <summary>The loudness, 0 to 1.</summary>
    double Volume { get; set; }

    /// <summary>Loads the song at <paramref name="path"/>, or nothing for null; stops what plays. It may take a moment (<see cref="IsReady"/>).</summary>
    void Load(string? path);

    /// <summary>Plays from <paramref name="position"/> in the song.</summary>
    void Play(TimeSpan position);

    /// <summary>Moves to <paramref name="position"/>, playing on if it plays.</summary>
    void Seek(TimeSpan position);

    /// <summary>Stops where it is.</summary>
    void Pause();
}
