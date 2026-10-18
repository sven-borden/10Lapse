namespace TenLapse.Presentation.Services;

/// <summary>A yes-or-no question with buttons that name the action (DESIGN.md, the Sentence Case Rule).</summary>
/// <param name="Title">The question, e.g. "Start a new project?".</param>
/// <param name="Message">What happens if the user goes ahead.</param>
/// <param name="ConfirmText">The button that goes ahead, e.g. "Start new project".</param>
/// <param name="CancelText">The button that keeps everything as it is.</param>
public sealed record Confirmation(string Title, string Message, string ConfirmText, string CancelText);

/// <summary>Shows modal questions. The App implements it with a <c>ContentDialog</c>; tests use a fake.</summary>
public interface IDialogService
{
    /// <summary>Asks the question; true when the user chose <see cref="Confirmation.ConfirmText"/>.</summary>
    /// <remarks>Cancel is the default button, and closing the dialog any other way (Esc) also answers false.</remarks>
    Task<bool> ConfirmAsync(Confirmation confirmation);
}
