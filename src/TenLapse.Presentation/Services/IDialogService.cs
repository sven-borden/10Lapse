namespace TenLapse.Presentation.Services;

/// <summary>A yes-or-no question with buttons that name the action (DESIGN.md, the Sentence Case Rule).</summary>
/// <param name="Title">The question, e.g. "Start a new project?".</param>
/// <param name="Message">What happens if the user goes ahead.</param>
/// <param name="ConfirmText">The button that goes ahead, e.g. "Start new project".</param>
/// <param name="CancelText">The button that keeps everything as it is.</param>
public sealed record Confirmation(string Title, string Message, string ConfirmText, string CancelText);

/// <summary>
/// A question with two ways forward and a way back (#12: a drop onto a project with photos asks "Add to project" or
/// "Start new project"). Buttons name the action.
/// </summary>
/// <param name="Title">The question.</param>
/// <param name="Message">What each choice does.</param>
/// <param name="PrimaryText">The first way forward and the default button, so it must lose nothing.</param>
/// <param name="SecondaryText">The second way forward.</param>
/// <param name="CancelText">The button that does neither.</param>
public sealed record Choice(string Title, string Message, string PrimaryText, string SecondaryText, string CancelText);

/// <summary>What the user answered to a <see cref="Choice"/>.</summary>
public enum ChoiceResult
{
    /// <summary>Cancel, Esc, or the dialog closed any other way.</summary>
    Cancel,

    /// <summary><see cref="Choice.PrimaryText"/>.</summary>
    Primary,

    /// <summary><see cref="Choice.SecondaryText"/>.</summary>
    Secondary,
}

/// <summary>Shows modal questions. The App implements it with a <c>ContentDialog</c>; tests use a fake.</summary>
public interface IDialogService
{
    /// <summary>Asks the question; true when the user chose <see cref="Confirmation.ConfirmText"/>.</summary>
    /// <remarks>Cancel is the default button, and closing the dialog any other way (Esc) also answers false.</remarks>
    Task<bool> ConfirmAsync(Confirmation confirmation);

    /// <summary>Asks the question and says which button the user chose.</summary>
    /// <remarks>The primary button is the default; closing the dialog any other way (Esc) answers <see cref="ChoiceResult.Cancel"/>.</remarks>
    Task<ChoiceResult> ChooseAsync(Choice choice);
}
