using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Media.Export;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// The sentence for each <see cref="VideoExportFailure"/> (#8's categories), so the error path can explain an
/// export failure without Presentation referencing Media. The App registers it with the <see cref="ErrorPresenter"/>;
/// the tests compile this file directly (they cannot reference the App). The sentences are in
/// <see cref="ProjectText"/>.
/// </summary>
public sealed class VideoExportErrorMapper : IErrorMessageMapper
{
    /// <inheritdoc />
    /// <remarks>A photo that could not be read is named, with its frame number and the reason when the decoder gave one.</remarks>
    public ErrorMessage? Map(Exception exception) => exception switch
    {
        VideoExportException { Failure: VideoExportFailure.FrameUnreadable, FrameIndex: int index, SourcePath: { } path } export =>
            Critical(ProjectText.ExportFailedPhoto(index, path, (export.InnerException as PhotoDecodeException)?.Failure.Reason)),
        VideoExportException export => For(export.Failure, export.Format),
        _ => null,
    };

    /// <summary>
    /// The message for a category. The ones the user cannot fix alone (an encoder or driver failure) offer
    /// Copy diagnostics.
    /// </summary>
    public static ErrorMessage For(VideoExportFailure failure) => For(failure, format: null);

    /// <summary>
    /// The message for a category while writing <paramref name="format"/>. A GIF (#51) gets its own advice where the
    /// video's would mislead: it has no resolution to lower (its Size and Frame rate lists stand in for it), and WIC
    /// encodes it on the CPU, so the graphics driver is not to blame.
    /// </summary>
    public static ErrorMessage For(VideoExportFailure failure, VideoFormat? format) =>
        (format == VideoFormat.Gif ? ForGif(failure) : null) ?? ForVideo(failure);

    private static ErrorMessage ForVideo(VideoExportFailure failure) => failure switch
    {
        VideoExportFailure.EncoderUnavailable => Critical(ProjectText.ExportFailedEncoderUnavailable, diagnostics: true),
        VideoExportFailure.SettingsRejected => Critical(ProjectText.ExportFailedSettingsRejected),
        VideoExportFailure.EncoderStalled => Critical(ProjectText.ExportFailedEncoderStalled, diagnostics: true),
        VideoExportFailure.EncoderStuck => Critical(ProjectText.ExportFailedEncoderStuck, diagnostics: true),
        VideoExportFailure.OutputFolderNotWritable => Critical(ProjectText.ExportFailedFolderNotWritable),
        VideoExportFailure.OutputFileInUse => Critical(ProjectText.ExportFailedFileInUse),
        VideoExportFailure.OutputFolderMissing => Critical(ProjectText.ExportFailedFolderMissing),
        VideoExportFailure.DiskFull => Critical(ProjectText.ExportFailedDiskFull),
        VideoExportFailure.NoGraphicsDevice => Critical(ProjectText.ExportFailedNoGraphicsDevice, diagnostics: true),
        VideoExportFailure.GraphicsDeviceLost => Critical(ProjectText.ExportFailedGraphicsDeviceLost, diagnostics: true),
        VideoExportFailure.OutOfMemory => Critical(ProjectText.ExportFailedOutOfMemory),
        VideoExportFailure.FrameUnreadable => Critical(ProjectText.ExportFailedPhotoUnreadable),
        VideoExportFailure.DrawingFailed => Critical(ProjectText.ExportFailedDrawing, diagnostics: true),
        VideoExportFailure.MusicUnreadable => Critical(Presentation.Music.MusicText.ExportFailed),
        _ => Critical(ProjectText.ExportFailedEncoder, diagnostics: true),
    };

    // The GIF's own sentences; null where the video's say the right thing (a full disk, a photo, the drawing on the GPU).
    private static ErrorMessage? ForGif(VideoExportFailure failure) => failure switch
    {
        VideoExportFailure.EncoderUnavailable => Critical(ProjectText.ExportFailedGifEncoderUnavailable, diagnostics: true),
        VideoExportFailure.SettingsRejected => Critical(ProjectText.ExportFailedGifSettingsRejected),
        VideoExportFailure.EncoderFailed => Critical(ProjectText.ExportFailedGifEncoder, diagnostics: true),
        VideoExportFailure.EncoderStalled => Critical(ProjectText.ExportFailedGifEncoderStalled, diagnostics: true),
        VideoExportFailure.OutOfMemory => Critical(ProjectText.ExportFailedGifOutOfMemory),
        _ => null,
    };

    private static ErrorMessage Critical(string text, bool diagnostics = false) => new(InfoBarSeverity.Critical, text, diagnostics);
}
