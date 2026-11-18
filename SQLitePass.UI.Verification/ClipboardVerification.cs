using System.Diagnostics;
using System.IO;
using System.Windows;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Verification;

internal static class ClipboardVerification
{
    public static async Task<object> VerifyAsync()
    {
        if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
            throw new InvalidOperationException("Clipboard verification requires a disposable Windows runner.");

        const string original = "SQLitePass synthetic clipboard value";
        const string replacement = "SQLitePass synthetic replacement value";
        const int timeout = 3;
        using var clipboard = new ClipboardCoordinator(_ => { });
        try
        {
            clipboard.CopyWithAutoClear(original, "synthetic value", timeout);
            var data = Clipboard.GetDataObject();
            Require(data?.GetData(DataFormats.UnicodeText) as string == original, "The synthetic clipboard copy failed.");
            Require(data!.GetDataPresent("CanIncludeInClipboardHistory", false) &&
                data.GetDataPresent("CanUploadToCloudClipboard", false), "Clipboard exclusion formats are absent.");
            await RequireExpiredAsync();

            clipboard.CopyWithAutoClear(original, "synthetic value", timeout);
            Require(Clipboard.GetText() == original, "The second synthetic clipboard copy failed.");
            Clipboard.SetText(replacement);
            await Task.Delay(TimeSpan.FromSeconds(timeout + 2));
            Require(Clipboard.GetText() == replacement, "Expiry erased newer clipboard content.");

            return new { SyntheticOnly = true, OwnedContentExpired = true, ReplacementPreserved = true, ExclusionFormatsPresent = true };
        }
        finally
        {
            // Clear only a value created by this verification.
            string remaining = Clipboard.GetText();
            if (remaining is original or replacement) Clipboard.Clear();
        }
    }

    private static async Task RequireExpiredAsync()
    {
        var timer = Stopwatch.StartNew();
        while (Clipboard.ContainsText() && timer.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(100);
        Require(!Clipboard.ContainsText(), "SQLitePass did not clear its expired clipboard value.");
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException(reason);
    }
}
