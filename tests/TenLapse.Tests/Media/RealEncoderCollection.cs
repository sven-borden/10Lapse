namespace TenLapse.Tests.Media;

/// <summary>
/// The tests that run this PC's real encoders, or count the export's process-wide state (stuck encoder calls,
/// pending clean-ups): one at a time and with nothing else running. Several hardware encodes at once can make a
/// GPU encoder stall past a test's limit, and a stall in one test changes what another one's open failure means.
/// </summary>
[CollectionDefinition(nameof(RealEncoderCollection), DisableParallelization = true)]
public sealed class RealEncoderCollection;
