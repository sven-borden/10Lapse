namespace TenLapse.Core.Timing;

/// <summary>
/// Which photo each frame of the video shows (#49): the photos in order, played <see cref="Plays"/> times, forward or as a
/// boomerang (forward, then backward), with the first frame held for <see cref="HoldStartFrames"/> extra frames before
/// and the last one for <see cref="HoldEndFrames"/> after. A pure function from an output frame's index (its
/// <i>position</i>) to a photo index, in constant time, so the preview, the MP4 and the GIF all ask it the same question
/// and get the same answer. Times stay <see cref="FrameTiming"/>'s: position n starts at <c>FrameStart(n, rate)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The core.</b> Forward, the photos 0 to P−1 play once per play: <c>Plays × P</c> frames. A boomerang runs
/// 0 … P−1 … 1 per play and ends on photo 0 once all plays are done: <c>Plays × (2P − 2) + 1</c> frames. No photo shows
/// twice in a row at a turn: the last photo is the turning point of the way out and back, and photo 0 is shared by
/// the end of one play and the start of the next. With one photo there is nothing to turn on, so a boomerang is
/// <c>Plays</c> frames of it.
/// </para>
/// <para>
/// <b>The holds</b> come before and after the core, once: the video's first frame stays on screen for
/// <see cref="HoldStartFrames"/> more frames, and its last for <see cref="HoldEndFrames"/> more. With no photos there is
/// no video, so no holds either.
/// </para>
/// <para>
/// In every sequence the photos shown in the first n frames are 0 to some k (the first pass goes forward), which is what
/// <see cref="PhotosCovered"/> counts for the export's progress in the filmstrip.
/// </para>
/// </remarks>
public readonly record struct FrameSequence
{
    /// <summary>Creates a sequence; every count must be zero or more, and <paramref name="plays"/> at least 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A count is negative, or <paramref name="plays"/> is below 1.</exception>
    /// <exception cref="OverflowException">The video would have more than <see cref="int.MaxValue"/> frames.</exception>
    public FrameSequence(int photoCount, int holdStartFrames, int holdEndFrames, int plays, bool boomerang)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(photoCount);
        ArgumentOutOfRangeException.ThrowIfNegative(holdStartFrames);
        ArgumentOutOfRangeException.ThrowIfNegative(holdEndFrames);
        ArgumentOutOfRangeException.ThrowIfLessThan(plays, 1);
        PhotoCount = photoCount;
        HoldStartFrames = photoCount == 0 ? 0 : holdStartFrames;
        HoldEndFrames = photoCount == 0 ? 0 : holdEndFrames;
        Plays = plays;
        Boomerang = boomerang;
        CoreLength = CoreLengthFor(photoCount, plays, boomerang);
        Length = checked(HoldStartFrames + CoreLength + HoldEndFrames);
    }

    /// <summary>The photos in the project.</summary>
    public int PhotoCount { get; }

    /// <summary>Extra frames of the first frame, before the photos play.</summary>
    public int HoldStartFrames { get; }

    /// <summary>Extra frames of the last frame, after the photos played.</summary>
    public int HoldEndFrames { get; }

    /// <summary>How many times the photos play (a boomerang counts out and back as one).</summary>
    public int Plays { get; }

    /// <summary>True when each play goes forward and then backward.</summary>
    public bool Boomerang { get; }

    /// <summary>The frames the photos themselves fill, without the holds.</summary>
    public int CoreLength { get; }

    /// <summary>Frames in the video: the holds and the core.</summary>
    public int Length { get; }

    /// <summary>True when the video is the photos once, forward, one frame each: position n shows photo n.</summary>
    public bool IsIdentity => Length == PhotoCount;

    /// <summary>The photos played once, forward, with no holds: what a project without #49's settings plays.</summary>
    public static FrameSequence Plain(int photoCount) => new(photoCount, 0, 0, 1, false);

    /// <summary>The frames <paramref name="photoCount"/> photos fill when played <paramref name="plays"/> times, forward or as a boomerang, without holds.</summary>
    public static int CoreLengthFor(int photoCount, int plays, bool boomerang)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(photoCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(plays, 1);
        if (photoCount == 0)
        {
            return 0;
        }

        return boomerang && photoCount >= 2
            ? checked((plays * ((2 * photoCount) - 2)) + 1)
            : checked(plays * photoCount);
    }

    /// <summary>The length of the video in 100 ns ticks at <paramref name="rate"/>: <see cref="FrameTiming.Duration"/> of <see cref="Length"/>.</summary>
    public long Duration(Fraction rate) => FrameTiming.Duration(Length, rate);

    // Frames in one play of the core: P forward, 2P − 2 for a boomerang (its closing photo 0 is the next play's first).
    private int Cycle => Boomerang && PhotoCount >= 2 ? (2 * PhotoCount) - 2 : PhotoCount;

    // The position of the first frame after the first forward pass: every photo has shown by then.
    private int FirstPassEnd => HoldStartFrames + PhotoCount;

    /// <summary>The photo (0-based) frame <paramref name="position"/> of the video shows.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the video (0 to <see cref="Length"/> − 1).</exception>
    public int PhotoAt(int position)
    {
        if (position < 0 || position >= Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, $"The video has {Length} frames.");
        }

        var core = position - HoldStartFrames;
        if (core <= 0)
        {
            return 0;
        }

        if (core >= CoreLength)
        {
            return LastPhoto;
        }

        var within = core % Cycle;
        return within < PhotoCount ? within : Cycle - within;
    }

    /// <summary>
    /// Which way the photos run at <paramref name="position"/>: 1 forward (and in the holds), -1 on a boomerang's way back.
    /// The preview loads ahead in this direction.
    /// </summary>
    public int DirectionAt(int position)
    {
        var core = position - HoldStartFrames;
        if (!Boomerang || PhotoCount < 2 || core < 0 || core >= CoreLength)
        {
            return 1;
        }

        // The turning photo (P − 1) faces back: what comes after it is P − 2.
        return core % Cycle >= PhotoCount - 1 ? -1 : 1;
    }

    /// <summary>The photo the video ends on: the last for a forward sequence, the first for a boomerang (none without photos).</summary>
    public int LastPhoto => PhotoCount == 0 ? 0 : Boomerang && PhotoCount >= 2 ? 0 : PhotoCount - 1;

    /// <summary>
    /// The first frame of every showing of <paramref name="photo"/>, in order: a showing is a run of frames of it (a hold
    /// included). Photo 0's first showing starts at frame 0, its hold included; a boomerang shows the photos between the
    /// ends twice a play. At most <c>2 × Plays + 1</c> positions.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The photo is not in the sequence.</exception>
    public IReadOnlyList<int> ShowingsOf(int photo)
    {
        if (photo < 0 || photo >= PhotoCount)
        {
            throw new ArgumentOutOfRangeException(nameof(photo), photo, $"The sequence has {PhotoCount} photos.");
        }

        var starts = new List<int>((2 * Plays) + 1);
        var cycle = Cycle;
        var turns = Boomerang && PhotoCount >= 2;
        for (var play = 0; play < Plays; play++)
        {
            var origin = HoldStartFrames + (play * cycle);
            if (photo == 0)
            {
                // Photo 0 opens every play; the first time with the hold before it.
                starts.Add(play == 0 ? 0 : origin);
                continue;
            }

            starts.Add(origin + photo);
            if (turns && photo < PhotoCount - 1)
            {
                starts.Add(origin + cycle - photo);
            }
        }

        if (turns && photo == 0)
        {
            // The boomerang's last frame (and the hold after it).
            starts.Add(HoldStartFrames + CoreLength - 1);
        }

        return starts;
    }

    /// <summary>The first frame of <paramref name="photo"/>'s first showing: where a click on its thumbnail goes.</summary>
    public int FirstPositionOf(int photo) => ShowingsOf(photo)[0];

    /// <summary>
    /// The first frame of the showing of <paramref name="photo"/> nearest to <paramref name="position"/> (the earlier on a
    /// tie): where the playhead goes when the photo on screen moved in the project, so it stays in the same pass.
    /// </summary>
    public int NearestPositionOf(int photo, int position)
    {
        var best = -1;
        foreach (var start in ShowingsOf(photo))
        {
            if (best < 0 || Math.Abs((long)start - position) < Math.Abs((long)best - position))
            {
                best = start;
            }
        }

        return best;
    }

    /// <summary>
    /// How many photos the first <paramref name="positions"/> frames show at least once: always the photos 0 to k − 1, since
    /// the first pass goes forward. The export's strip dims the photos from this index on; all of them are done once the
    /// first forward pass is. For a plain sequence this is <paramref name="positions"/> itself.
    /// </summary>
    public int PhotosCovered(int positions)
    {
        if (positions <= 0 || PhotoCount == 0)
        {
            return 0;
        }

        if (positions <= HoldStartFrames)
        {
            return 1;
        }

        return positions >= FirstPassEnd ? PhotoCount : positions - HoldStartFrames;
    }
}
