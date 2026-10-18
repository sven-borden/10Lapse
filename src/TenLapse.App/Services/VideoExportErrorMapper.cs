using TenLapse.Media.Export;
using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// The sentence for each <see cref="VideoExportFailure"/> (#8's categories), so the error path can explain an
/// export failure without Presentation referencing Media. The App registers it with the <see cref="ErrorPresenter"/>;
/// the tests compile this file directly (they cannot reference the App). #22 may reword the sentences in
/// <see cref="ProjectText"/>.
/// </summary>
public sealed class VideoExportErrorMapper : IErrorMessageMapper
{
    /// <inheritdoc />
    public ErrorMessage? Map(Exception exception) =>
        exception is VideoExportException export ? For(export.Failure) : null;

    /// <summary>
    /// The message for a category. The ones the user cannot fix alone (an encoder or driver failure) offer
    /// Copy diagnostics.
    /// </summary>
    public static ErrorMessage For(VideoExportFailure failure) => failure switch
    {
        VideoExportFailure.EncoderUnavailable => Critical(ProjectText.ExportFailedEncoderUnavailable, diagnostics: true),
        VideoExportFailure.SettingsRejected => Critical(ProjectText.ExportFailedSettingsRejected),
        VideoExportFailure.EncoderStalled => Critical(ProjectText.ExportFailedEncoderStalled, diagnostics: true),
        VideoExportFailure.OutputFolderNotWritable => Critical(ProjectText.ExportFailedFolderNotWritable),
        VideoExportFailure.OutputFileInUse => Critical(ProjectText.ExportFailedFileInUse),
        VideoExportFailure.OutputFolderMissing => Critical(ProjectText.ExportFailedFolderMissing),
        VideoExportFailure.DiskFull => Critical(ProjectText.ExportFailedDiskFull),
        VideoExportFailure.NoGraphicsDevice => Critical(ProjectText.ExportFailedNoGraphicsDevice, diagnostics: true),
        VideoExportFailure.GraphicsDeviceLost => Critical(ProjectText.ExportFailedGraphicsDeviceLost, diagnostics: true),
        VideoExportFailure.OutOfMemory => Critical(ProjectText.ExportFailedOutOfMemory),
        _ => Critical(ProjectText.ExportFailedEncoder, diagnostics: true),
    };

    private static ErrorMessage Critical(string text, bool diagnostics = false) => new(InfoBarSeverity.Critical, text, diagnostics);
}
