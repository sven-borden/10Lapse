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
/// 0 … P−1 … 1 per play: <c>Plays × (2P − 2)</c> frames, with no closing photo 0 (review of PR #134). A play is then the
/// loop's unit: the next play, or a player looping the video (a GIF always does, the preview does, chat apps do), goes
/// on from photo 1 to photo 0 with no photo shown twice in a row at the seam. The last photo is the turning point and
/// shows once. Two photos alternate (0, 1, 0, 1, …), as forward. With one photo there is nothing to turn on, so a
/// boomerang is <c>Plays</c> frames of it.
/// </para>
/// <para>
/// <b>The holds</b> come before and after the core, once: the video's first frame (photo 0) stays on screen for
/// <see cref="HoldStartFrames"/> more frames. After the core, <see cref="HoldEndFrames"/> frames show
/// <see cref="HoldEndPhoto"/>: the last photo forward (the last frame held), photo 0 for a boomerang, which comes home
/// there and waits. Without an end hold a boomerang ends on photo 1. With no photos there is no video, so no holds
/// either.
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
            ? checked(plays * ((2 * photoCount) - 2))
            : checked(plays * photoCount);
    }

    /// <summary>The length of the video in 100 ns ticks at <paramref name="rate"/>: <see cref="FrameTiming.Duration"/> of <see cref="Length"/>.</summary>
    public long Duration(Fraction rate) => FrameTiming.Duration(Length, rate);

    // Frames in one play of the core: P forward, 2P − 2 for a boomerang (0 … P−1 … 1; photo 0 opens the next play).
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
            return HoldEndPhoto;
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

    /// <summary>
    /// The photo the video ends on (0 without photos): the last forward; for a boomerang photo 0 when it holds at the end,
    /// else photo 1 (the way back's last, which a looping player follows with photo 0).
    /// </summary>
    public int LastPhoto => Length == 0 ? 0 : PhotoAt(Length - 1);

    /// <summary>The photo the end hold shows: the last photo forward, photo 0 for a boomerang (it comes home and waits).</summary>
    public int HoldEndPhoto => PhotoCount == 0 ? 0 : Boomerang && PhotoCount >= 2 ? 0 : PhotoCount - 1;

    /// <summary>
    /// The first frame of every showing of <paramref name="photo"/>, in order: a showing is a run of frames of it (a hold
    /// included). Photo 0's first showing starts at frame 0, its hold included; a boomerang shows the photos between the
    /// ends twice a play, and photo 0 once more in its end hold. At most <c>2 × Plays + 1</c> positions.
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

        if (turns && photo == 0 && HoldEndFrames > 0)
        {
            // A boomerang's end hold: photo 0, home again, after photo 1.
            starts.Add(HoldStartFrames + CoreLength);
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
    /// The first frame of the showing frame <paramref name="position"/> is in: a showing is a run of frames of one photo,
    /// a hold included. In constant time: runs longer than a frame are only the start hold with photo 0's first frame,
    /// the end hold (with the last photo's frame, forward), and the whole video when there is one photo.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the video.</exception>
    public int ShowingStart(int position)
    {
        _ = PhotoAt(position);
        if (PhotoCount == 1 || position <= HoldStartFrames)
        {
            return 0;
        }

        var endHold = HoldStartFrames + CoreLength;
        if (HoldEndFrames > 0 && position >= endHold - 1)
        {
            // Forward, the last photo's frame and its hold are one showing; a boomerang's hold (photo 0) follows photo 1.
            return PhotoAt(endHold - 1) == HoldEndPhoto ? endHold - 1 : position >= endHold ? endHold : position;
        }

        return position;
    }

    /// <summary>The last frame of the showing frame <paramref name="position"/> is in (see <see cref="ShowingStart"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the video.</exception>
    public int ShowingEnd(int position)
    {
        _ = PhotoAt(position);
        if (PhotoCount == 1)
        {
            return Length - 1;
        }

        if (position <= HoldStartFrames)
        {
            return HoldStartFrames;
        }

        var endHold = HoldStartFrames + CoreLength;
        return HoldEndFrames > 0 && (position >= endHold || (position == endHold - 1 && PhotoAt(position) == HoldEndPhoto))
            ? Length - 1
            : position;
    }

    /// <summary>
    /// Where the arrow keys go from frame <paramref name="position"/> (review of PR #134): <paramref name="showings"/>
    /// photos on along the video (back when negative), each to the first frame of the next showing, a hold's frames
    /// skipped. Forward is always later in the video, on a boomerang's way back too. Back from inside a showing passes its
    /// first frame and goes to the one before, so the photo on screen changes. Stops at the video's ends. In a plain
    /// sequence this is the photo index plus <paramref name="showings"/>, clamped.
    /// </summary>
    public int StepShowings(int position, int showings)
    {
        if (Length == 0)
        {
            return 0;
        }

        var at = Math.Clamp(position, 0, Length - 1);
        for (var step = 0; step < Math.Abs(showings); step++)
        {
            if (showings > 0)
            {
                var end = ShowingEnd(at);
                if (end + 1 >= Length)
                {
                    break;
                }

                at = end + 1;
            }
            else
            {
                var start = ShowingStart(at);
                if (start == 0)
                {
                    at = 0;
                    break;
                }

                at = ShowingStart(start - 1);
            }
        }

        return at;
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
