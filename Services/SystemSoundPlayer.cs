using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Platform;

namespace GeoQuest.Services;

/// <summary>The short effects a round can trigger.</summary>
public enum GameSound
{
    Correct,
    BonusLife,
    Wrong,
}

/// <summary>
/// Plays the bundled WAVs through whatever the host platform already provides: winmm on
/// Windows, <c>afplay</c> on macOS, PulseAudio or ALSA on Linux. That keeps the app free
/// of an audio stack and its native binaries, which matters for a self-contained drop
/// that already carries Skia.
///
/// The sounds ship as Avalonia resources, which the platform players cannot read, so each
/// one is unpacked to a file the first time it is asked for and reused after that.
/// </summary>
public sealed class SystemSoundPlayer
{
    private const string ResourceRoot = "avares://GeoQuest/Assets/Sounds/";

    /// <summary>Paths of the WAVs unpacked so far. Only successful unpacks are recorded.</summary>
    private readonly ConcurrentDictionary<GameSound, string> _files = new();

    /// <summary>
    /// Beside the player's save data rather than in the system temp directory: on Linux
    /// /tmp is shared by every account, and writing predictable filenames there invites
    /// another user to leave a symlink where our copy is about to land.
    /// </summary>
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GeoQuest",
        "sounds");

    /// <summary>The Linux player that answered first; the other is not tried again. Written
    /// from pool threads, so volatile — the worst a stale read costs is one extra probe.</summary>
    private volatile string? _linuxPlayer;

    public void Play(GameSound sound)
    {
        // Everything is handed to the pool: neither unpacking a WAV nor a stalled audio
        // device may hold up the round that triggered the sound.
        ThreadPool.QueueUserWorkItem(static state => state.Item1.Playback(state.Item2), (this, sound), preferLocal: false);
    }

    private void Playback(GameSound sound)
    {
        if (!_files.TryGetValue(sound, out var path))
        {
            path = Unpack(sound);

            // Only a success is remembered. Caching the failure would mute that sound for
            // the rest of the session over something as passing as a locked file.
            if (path is null)
            {
                return;
            }

            _files[sound] = path;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Asynchronous, so a sound already ringing is simply replaced by this one.
                PlaySound(path, IntPtr.Zero, SndFilename | SndAsync | SndNoDefault);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Run("afplay", path);
            }
            else
            {
                PlayOnLinux(path);
            }
        }
        catch (Exception)
        {
            // Sound is a garnish. Anything that goes wrong here is silently dropped.
        }
    }

    private void PlayOnLinux(string path)
    {
        if (_linuxPlayer is not null)
        {
            Run(_linuxPlayer, path);
            return;
        }

        // PulseAudio first, since a desktop that has it usually routes ALSA through it.
        foreach (var player in (ReadOnlySpan<string>)["paplay", "aplay"])
        {
            if (Run(player, path))
            {
                _linuxPlayer = player;
                return;
            }
        }
    }

    /// <summary>Runs a command-line player to completion. False means it is missing or could not play.</summary>
    private static bool Run(string command, string path)
    {
        var startInfo = new ProcessStartInfo(command, [path])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return false;
            }

            // Waited on rather than abandoned, so the child is reaped instead of lingering.
            process.WaitForExit();

            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Copies a resource out to a file, returning its path, or null if that fails.</summary>
    private string? Unpack(GameSound sound)
    {
        var name = sound switch
        {
            GameSound.Correct => "correct.wav",
            GameSound.BonusLife => "bonus.wav",
            _ => "wrong.wav",
        };

        try
        {
            var path = Path.Combine(_directory, name);

            using var resource = AssetLoader.Open(new Uri(ResourceRoot + name));

            // Rewritten whenever the size differs, so an upgraded sound replaces the copy
            // an older build left behind.
            if (!File.Exists(path) || new FileInfo(path).Length != resource.Length)
            {
                Directory.CreateDirectory(_directory);

                using var file = File.Create(path);
                resource.CopyTo(file);
            }

            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndFilename = 0x00020000;

    // DllImport rather than LibraryImport: the generated marshalling code needs the whole
    // project compiled unsafe, which is a lot to concede for one call into winmm.
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(string? path, IntPtr module, uint flags);
}
