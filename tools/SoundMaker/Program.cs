namespace GeoQuest.Tools.SoundMaker;

/// <summary>
/// Dev-time tool. Synthesises the game's answer sounds as small 16-bit PCM WAVs, so the
/// app ships audio that is ours to ship and needs no audio-authoring dependency at build
/// time or decoder at runtime. Re-run after changing a voice below.
///
///     dotnet run --project tools/SoundMaker
/// </summary>
internal static class Program
{
    private const int SampleRate = 44100;
    private const double TwoPi = Math.PI * 2d;

    /// <summary>Peak the finished waveform is normalised to; leaves headroom, never clips.</summary>
    private const double Peak = 0.72d;

    // Equal-tempered pitches, named as they are written so the voices below read as music.
    private const double C6 = 1046.50d;
    private const double E6 = 1318.51d;
    private const double G6 = 1567.98d;
    private const double C7 = 2093.00d;

    private static int Main(string[] args)
    {
        string? outputDir = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output" when i + 1 < args.Length:
                    outputDir = args[++i];
                    break;
                default:
                    Console.Error.WriteLine($"Unrecognised argument: {args[i]}");
                    return 1;
            }
        }

        outputDir ??= LocateSoundsDirectory();
        if (outputDir is null)
        {
            Console.Error.WriteLine("Could not locate the Assets directory. Pass --output explicitly.");
            return 1;
        }

        Directory.CreateDirectory(outputDir);

        Write(Path.Combine(outputDir, "correct.wav"), Correct());
        Write(Path.Combine(outputDir, "bonus.wav"), Bonus());
        Write(Path.Combine(outputDir, "wrong.wav"), Wrong());

        return 0;
    }

    /// <summary>A rising fifth on a bell voice: short, bright, and cheap to hear repeatedly.</summary>
    private static double[] Correct()
    {
        var buffer = new double[Length(0.50d)];

        AddBell(buffer, at: 0.000d, duration: 0.30d, frequency: C6, gain: 0.60d, decay: 10d);
        AddBell(buffer, at: 0.070d, duration: 0.43d, frequency: G6, gain: 0.75d, decay: 7d);

        return buffer;
    }

    /// <summary>The same voice climbing an arpeggio, so an earned life reads as "more" than correct.</summary>
    private static double[] Bonus()
    {
        var buffer = new double[Length(0.85d)];

        AddBell(buffer, at: 0.000d, duration: 0.30d, frequency: C6, gain: 0.45d, decay: 11d);
        AddBell(buffer, at: 0.065d, duration: 0.30d, frequency: E6, gain: 0.50d, decay: 11d);
        AddBell(buffer, at: 0.130d, duration: 0.30d, frequency: G6, gain: 0.55d, decay: 11d);
        AddBell(buffer, at: 0.195d, duration: 0.65d, frequency: C7, gain: 0.70d, decay: 5d);

        return buffer;
    }

    /// <summary>
    /// A low, quick downward slide. Deliberately soft — a harsh buzzer grates within a
    /// few rounds, and the red border already says "wrong" loudly enough.
    /// </summary>
    private static double[] Wrong()
    {
        var buffer = new double[Length(0.42d)];

        AddSlide(buffer, at: 0.000d, duration: 0.34d, from: 320d, to: 150d, gain: 0.80d, decay: 5d);

        // A second voice a whisker flat thickens the tone into a buzz rather than a beep.
        AddSlide(buffer, at: 0.000d, duration: 0.34d, from: 316d, to: 148d, gain: 0.45d, decay: 5d);

        return buffer;
    }

    private static int Length(double seconds) => (int)(seconds * SampleRate);

    /// <summary>A struck-bell voice: a decaying sine with two quiet harmonics above it.</summary>
    private static void AddBell(double[] buffer, double at, double duration, double frequency, double gain, double decay)
    {
        Render(buffer, at, duration, gain, decay, (t, _) =>
            Math.Sin(TwoPi * frequency * t)
            + 0.32d * Math.Sin(TwoPi * 2d * frequency * t)
            + 0.10d * Math.Sin(TwoPi * 3d * frequency * t));
    }

    /// <summary>A voice that glides between two pitches. Phase is accumulated, not computed
    /// from the instantaneous frequency, which is what keeps the slide free of clicks.</summary>
    private static void AddSlide(double[] buffer, double at, double duration, double from, double to, double gain, double decay)
    {
        var phase = 0d;

        Render(buffer, at, duration, gain, decay, (t, _) =>
        {
            var frequency = from + (to - from) * (t / duration);
            phase += TwoPi * frequency / SampleRate;

            return Math.Sin(phase) + 0.28d * Math.Sin(2d * phase);
        });
    }

    /// <summary>
    /// Mixes one voice into the buffer under a shared envelope: a few milliseconds of
    /// attack and release so no voice starts or ends on a step, and exponential decay
    /// in between so everything rings rather than switches off.
    /// </summary>
    private static void Render(double[] buffer, double at, double duration, double gain, double decay, Func<double, int, double> voice)
    {
        const double EdgeSeconds = 0.005d;

        var start = Length(at);
        var length = Length(duration);
        var edge = Length(EdgeSeconds);

        for (var i = 0; i < length; i++)
        {
            var index = start + i;

            if (index >= buffer.Length)
            {
                break;
            }

            var t = i / (double)SampleRate;
            var envelope = Math.Exp(-decay * t)
                * Math.Min(1d, i / (double)edge)
                * Math.Min(1d, (length - i) / (double)edge);

            buffer[index] += gain * envelope * voice(t, i);
        }
    }

    private static void Write(string path, double[] samples)
    {
        Normalise(samples);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        var dataBytes = samples.Length * sizeof(short);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);                              // PCM header size
        writer.Write((short)1);                        // PCM, uncompressed
        writer.Write((short)1);                        // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * sizeof(short));      // byte rate
        writer.Write((short)sizeof(short));            // block align
        writer.Write((short)16);                       // bits per sample

        writer.Write("data"u8);
        writer.Write(dataBytes);

        foreach (var sample in samples)
        {
            writer.Write((short)(Math.Clamp(sample, -1d, 1d) * short.MaxValue));
        }

        Console.WriteLine($"{Path.GetFileName(path)}: {samples.Length / (double)SampleRate:0.00}s, {dataBytes / 1024} KB");
    }

    private static void Normalise(double[] samples)
    {
        var loudest = samples.Max(Math.Abs);

        if (loudest <= 0d)
        {
            return;
        }

        var scale = Peak / loudest;

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= scale;
        }
    }

    private static string? LocateSoundsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Assets");

            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, "Sounds");
            }

            dir = dir.Parent;
        }

        return null;
    }
}
