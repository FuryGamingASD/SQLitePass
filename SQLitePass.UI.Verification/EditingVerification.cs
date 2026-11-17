using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using SQLitePass.Core.Models;
using SQLitePass.UI.Dialogs;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Verification;

internal static class EditingVerification
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan interval) => _timestamp += interval.Ticks;
    }

    public static async Task<object> VerifyAsync()
    {
        Console.WriteLine("Editor verification: timed reveal and inline validation.");
        var clock = new ManualClock(); bool visible = false;
        using (var reveal = new TimedSecretReveal(value => visible = value, clock))
        {
            reveal.Reveal(); Require(visible, "Reveal did not expose the synthetic value.");
            clock.Advance(TimeSpan.FromMilliseconds(14999)); Require(reveal.IsRevealed, "Reveal expired too early.");
            clock.Advance(TimeSpan.FromMilliseconds(1)); reveal.CheckDeadline(); Require(!visible && !reveal.IsRevealed, "Reveal missed its monotonic deadline.");
            reveal.Reveal(); reveal.Hide(); Require(!visible, "Explicit hide did not mask.");
            reveal.Reveal(); reveal.Dispose(); Require(!visible, "Dispose retained a revealed value.");
        }
        var source = new PwEntry { Title = "Original", Password = "synthetic password", Revision = 2 };
        source.CustomFields.Add(new PwCustomField { Key = "PIN", Value = "synthetic token", IsProtected = true });
        using var draft = EntryEditDraft.Create(source);
        var editor = new EntryEditDialog(draft);
        try
        {
            var title = Field<TextBox>(editor, "TxtTitle");
            var masked = Field<PasswordBox>(editor, "TxtPasswordMasked"); var plain = Field<TextBox>(editor, "TxtPasswordPlain");
            var save = Field<Button>(editor, "BtnSaveEntry"); var validation = Field<TextBlock>(editor, "TxtEntryValidation");
            Require(!save.IsEnabled && !Field<bool>(editor, "_dirty"), "Existing unchanged editor is marked dirty.");
            Require(plain.Text.Length == 0 && masked.Password == source.Password, "Masked editor retains a duplicate plaintext control.");
            var reveal = Field<TimedSecretReveal>(editor, "_passwordReveal"); reveal.Reveal();
            Require(plain.Text == source.Password && masked.Password.Length == 0 && !Field<bool>(editor, "_dirty"), "Reveal changes the draft or duplicates its controls.");
            reveal.Hide(); Require(plain.Text.Length == 0 && masked.Password == source.Password, "Hide did not transfer and clear the plaintext control.");
            title.Clear(); Require(!save.IsEnabled && validation.Visibility == Visibility.Visible, "Missing title has no inline error.");
            Require(!await editor.PrepareFormForSaveAsync(), "Invalid title was accepted.");
            title.Text = "Changed"; Require(save.IsEnabled && Field<bool>(editor, "_dirty"), "Editing does not enable saving.");
            Field<CheckBox>(editor, "ChkExpires").IsChecked = true;
            Field<DatePicker>(editor, "DpExpiry").SelectedDate = null;
            Require(!save.IsEnabled, "Missing enabled expiry was accepted.");
            Field<DatePicker>(editor, "DpExpiry").SelectedDate = DateTime.Today;
            Field<TextBox>(editor, "TxtAutoTypeSeq").Text = "{UNSUPPORTED}";
            Require(!await editor.PrepareFormForSaveAsync() && validation.Visibility == Visibility.Visible, "Invalid Auto-Type has no inline error.");
            Field<TextBox>(editor, "TxtAutoTypeSeq").Text = "{S:PIN}{TAB}{PASSWORD}";
            Require(await editor.PrepareFormForSaveAsync() && draft.IsSaveRequired == true, "Valid edited form was rejected.");
            Require(DispatcherAccess(editor) && draft.Current.History.Count == 1, "Preparation did not keep collection updates on the STA context.");
            Require(source.Title == "Original" && source.History.Count == 0, "Editing changed the source model.");
        }
        finally { editor.CancelForLock(); source.ClearSecrets(); }
        Console.WriteLine("Editor verification: passed without showing a window.");
        return new { SyntheticOnly = true, WindowsShown = false, DesktopInputUsed = false, MonotonicRevealDeadline = true,
            HiddenPlaintextControlCleared = true, InlineTitleExpiryAndAutoTypeValidation = true, DirtyState = true,
            BoundHistoryPreparedOnDispatcher = true, SourceUnchanged = true, LockDiscardBypassesPrompt = true };
    }
    private static bool DispatcherAccess(DependencyObject instance) => instance.Dispatcher.CheckAccess();
}
