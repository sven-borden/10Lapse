namespace TenLapse.Core.Projects;

/// <summary>One photo of a blended frame (#47): its index in the project's photos and its share of the frame's light.</summary>
/// <param name="Photo">The photo's index in <see cref="ProjectSnapshot.Frames"/>.</param>
/// <param name="Weight">Its share, above 0; the shares of one frame add up to 1.</param>
public readonly record struct BlendLayer(int Photo, double Weight);

/// <summary>
/// The frame blending settings (#47): on or off, how many neighbouring photos each frame blends, and how strongly. Immutable;
/// a <c>with</c> expression validates too. Project only, like the look: a new project starts without blending.
/// </summary>
/// <remarks>
/// <para>
/// <b>The blend.</b> Each photo is shown as the average, in linear light, of the <see cref="Frames"/> photos around it in
/// the project's order (<see cref="Window"/>): a light that moves leaves a trail and clouds move smoothly, as in one long
/// exposure. <see cref="Strength"/> mixes that average with the photo itself: at 100 % the frame is the average, at 50 %
/// half the photo and half the average, at 0 % the photo alone. Each photo keeps its own deflicker correction in the
/// blend.
/// </para>
/// <para>
/// <b>Photos, not video frames.</b> The window runs over the photos, so the blend belongs to the photo: a hold holds the
/// blended photo, a boomerang plays the blended photos back, and the crop and pan of the video frame frames the blend.
/// At the ends of the project the window keeps to the photos there are, so the first and last photos blend fewer.
/// </para>
/// <para>Turning it off keeps the count and the strength, so turning it on again brings them back.</para>
/// </remarks>
public sealed record BlendSettings
{
    /// <summary>The fewest photos a frame blends.</summary>
    public const int MinFrames = 2;

    /// <summary>The most photos a frame blends.</summary>
    public const int MaxFrames = 16;

    /// <summary>The weakest strength, 0 %: on, but each frame is its photo alone.</summary>
    public const int MinStrength = 0;

    /// <summary>The strongest, 100 %: each frame is the average of its photos.</summary>
    public const int MaxStrength = 100;

    /// <summary>Off, 4 photos at full strength once turned on: a project from before #47 has no blend group and reads as this.</summary>
    public static BlendSettings Default { get; } = new(false, 4, MaxStrength);

    /// <summary>Creates frame blending settings.</summary>
    /// <param name="enabled">Whether the frames are blends of their neighbouring photos.</param>
    /// <param name="frames">How many photos a frame blends, <see cref="MinFrames"/> to <see cref="MaxFrames"/>.</param>
    /// <param name="strength">How much of the blend the frame shows, in percent, <see cref="MinStrength"/> to <see cref="MaxStrength"/>.</param>
    public BlendSettings(bool enabled, int frames, int strength)
    {
        Enabled = enabled;
        Frames = frames;
        Strength = strength;
    }

    /// <summary>Whether the frames are blends of their neighbouring photos.</summary>
    public bool Enabled { get; init; }

    /// <summary>How many photos a frame blends, the photo itself included.</summary>
    public int Frames
    {
        get;
        init => field = value is >= MinFrames and <= MaxFrames
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A frame blends 2 to 16 photos.");
    }

    /// <summary>How much of the blend the frame shows, in percent; the rest is the photo itself.</summary>
    public int Strength
    {
        get;
        init => field = value is >= MinStrength and <= MaxStrength
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The strength must be from 0 to 100 %.");
    }

    /// <summary>True when a frame can differ from its photo: on, at a strength above 0.</summary>
    public bool Blends => Enabled && Strength > MinStrength;

    /// <summary>
    /// The photos photo <paramref name="photo"/> of <paramref name="photos"/> blends, first and last included:
    /// <see cref="Frames"/> photos around it, one more before than after when the count is even (2 is the photo and the one
    /// before it), kept to the photos there are. Only the photo itself when blending is off.
    /// </summary>
    public (int First, int Last) Window(int photo, int photos)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(photo);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(photo, photos);
        if (!Blends)
        {
            return (photo, photo);
        }

        var first = photo - (Frames / 2);
        return (Math.Max(0, first), Math.Min(photos - 1, first + Frames - 1));
    }

    /// <summary>
    /// What photo <paramref name="photo"/> of <paramref name="photos"/> is drawn from, in photo order: each photo of its
    /// <see cref="Window"/> with its share. The n photos of the window share the strength evenly and the photo itself also
    /// takes the rest, so at 100 % every photo has 1/n and at 0 % (or off, or a window of one) the photo has it all.
    /// </summary>
    public BlendLayer[] Layers(int photo, int photos)
    {
        var (first, last) = Window(photo, photos);
        var count = last - first + 1;
        if (count == 1)
        {
            return [new BlendLayer(photo, 1)];
        }

        var share = Strength / (double)MaxStrength / count;
        var layers = new BlendLayer[count];
        for (var i = 0; i < count; i++)
        {
            var index = first + i;
            layers[i] = new BlendLayer(index, index == photo ? 1 - (share * (count - 1)) : share);
        }

        return layers;
    }
}
