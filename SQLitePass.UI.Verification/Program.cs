using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;
using SQLitePass.Core.Models;
using SQLitePass.UI;
using SQLitePass.UI.Controls;
using SQLitePass.UI.Dialogs;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Verification;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--verify-feed")
            return FeedVerification.RunAsync(args[1], args[2], args[3]).GetAwaiter().GetResult();
        int result = 1;
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.BeginInvoke(async () =>
        {
            try { result = await VerifyAsync(args); }
            catch (Exception exception) { Console.Error.WriteLine(exception); }
            finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
        });
        // Pump only the dispatcher. Application.Run would start the product's real login workflow.
        Dispatcher.Run();
        return result;
    }

    private static async Task<int> VerifyAsync(string[] args)
    {
        if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--session-only") { Console.Error.WriteLine("Specify an output directory for verification evidence."); return 2; }
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        string temporary = Path.Combine(Path.GetTempPath(), "SQLitePass-ui-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        AppContext.SetData("SQLitePass.SettingsDirectory", Path.Combine(temporary, "settings"));
        AppContext.SetData("SQLitePass.LogDirectory", Path.Combine(temporary, "logs"));
        var results = new List<object>(); var contrastResults = new List<object>();
        try
        {
            var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            WindowPresentationService.Register();
            File.WriteAllText(Path.Combine(output, "session-lock-results.json"), JsonSerializer.Serialize(await SessionVerification.VerifyAsync(temporary), new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "editor-results.json"), JsonSerializer.Serialize(await EditingVerification.VerifyAsync(), new JsonSerializerOptions { WriteIndented = true }));
            if (args.Length == 2) return 0;
            using var master = new MasterKey("synthetic-ui-verification-only"); byte[] salt = RandomNumberGenerator.GetBytes(16);
            using var crypto = CryptoEngine.FromMasterKey(master, salt, 1, 8192, 1);
            var metadata = new VaultMetadata { SaltBase64 = Convert.ToBase64String(salt), VerificationCanary = crypto.GenerateCanaryToken(), KdfIterations = 1, KdfMemoryKb = 8192, KdfParallelism = 1 };
            var root = new PwGroup { Name = "Synthetic vault", IsRoot = true };
            using var storage = await SqliteStorageProvider.CreateVaultAsync(Path.Combine(temporary, "synthetic.kpd"), metadata, crypto, [root], [], false);
            var seed = Enumerable.Range(0, 230).Select(index => new PwEntry
            {
                GroupId = root.Id, Title = $"{index:D3} Mail account — synthetic long title for layout verification",
                UserName = "synthetic.user@example.invalid", Password = "synthetic-only", Url = "https://example.invalid/login",
                Notes = "Synthetic notes for layout verification. " + new string('x', 1024)
            }).ToList();
            await storage.SaveEntriesBatchAsync(seed, crypto);
            foreach (var item in seed) item.ClearSecrets();
            var page = await storage.LoadEntryPageAsync(crypto, new EntryPageQuery());
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            foreach (string language in TranslationCatalog.Languages)
            foreach (string theme in new[] { "light", "dark", "high-contrast-simulated" })
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                bool dark = theme != "light";
                LocalizationManager.ApplyLanguage(language); ThemeManager.ApplyTheme(dark, false);
                if (theme == "high-contrast-simulated") ThemeManager.ApplyHighContrast(Brushes.Black, Brushes.White, Brushes.Yellow, Brushes.Black);
                if (language == "en" && scale == 1.0) VerifyContrast(theme, contrastResults);
                using var importCancellation = new CancellationTokenSource();
                using var exportCancellation = new CancellationTokenSource();
                var entry = new PwEntry { GroupId = root.Id, Title = "Synthetic entry", UserName = "synthetic user", Password = "synthetic", Notes = "Synthetic notes for layout verification" };
                var export = new OperationProgressDialog(exportCancellation, exporting: true);
                var populated = new MainWindow(storage, crypto, page.Metadata, enableDesktopIntegration: false);
                populated.ApplyVerificationPage(page with { Groups = page.Groups.Select(group => group.DeepClone()).ToList(), Entries = page.Entries.Select(item => item.CreateOverview()).ToList() });
                populated.DgEntries.SelectedIndex = 0;
                var dirty = new EntryEditDialog(entry); dirty.TxtTitle.Text = "Changed synthetic title";
                var invalid = new EntryEditDialog(entry); invalid.TxtTitle.Clear();
                var invalidAutoType = new EntryEditDialog(entry); invalidAutoType.EntryTabs.SelectedIndex = 2;
                invalidAutoType.TxtAutoTypeSeq.Text = "{UNSUPPORTED}";
                if (await invalidAutoType.PrepareFormForSaveAsync()) throw new InvalidDataException("Invalid Auto-Type render setup was accepted.");
                var protectedSource = entry.DeepClone(); protectedSource.CustomFields.Add(new PwCustomField { Key = "PIN", Value = "synthetic token", IsProtected = true });
                var fields = new EntryEditDialog(protectedSource); fields.EntryTabs.SelectedIndex = 1; protectedSource.ClearSecrets();
                var attachmentSource = entry.DeepClone(); attachmentSource.Attachments.Add(new PwAttachment { FileName = "Synthetic attachment with a longer descriptive filename.bin", Data = new byte[1024] });
                var attachments = new EntryEditDialog(attachmentSource); attachments.EntryTabs.SelectedIndex = 3; attachmentSource.ClearSecrets();
                Window[] windows = [new LoginWindow(), new EntryEditDialog(entry), dirty, invalid, invalidAutoType, fields, attachments, new SettingsDialog(), new ChangeMasterPasswordDialog(crypto, page.Metadata), new PasswordGeneratorDialog(), new GroupEditDialog(), new RestoreEntryDialog(new Dictionary<Guid, string> { [root.Id] = "Synthetic vault / A long destination folder for layout verification" }, root.Id), new CustomFieldDialog(), new AutoTypeConfirmDialog("Synthetic entry", "Synthetic destination"), new OperationProgressDialog(importCancellation), export, new MainWindow(storage, crypto, page.Metadata, enableDesktopIntegration: false), populated];
                foreach (var window in windows)
                {
                    window.ShowInTaskbar = false;
                    var content = (FrameworkElement)window.Content;
                    double width = double.IsFinite(window.Width) ? window.Width - 20 : 500;
                    content.Measure(new Size(width, double.PositiveInfinity));
                    double height = double.IsFinite(window.Height) ? window.Height - 44 : Math.Max(120, content.DesiredSize.Height);
                    content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                    if (content.ActualWidth <= 0 || content.ActualHeight <= 0) throw new InvalidDataException("No layout: " + window.GetType().Name);
                    if (ReferenceEquals(window, populated))
                    {
                        var row = populated.DgEntries.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow
                            ?? throw new InvalidDataException("No populated row was generated.");
                        var surface = row.Template.FindName("RowSurface", row) as Border;
                        if (!row.IsSelected || surface?.Background is not SolidColorBrush background ||
                            background.Color != ((SolidColorBrush)Application.Current.Resources["SelectionBg"]).Color)
                            throw new InvalidDataException("Selected row does not display the selection background: " + theme);
                    }
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    var drawing = new DrawingVisual();
                    using (var context = drawing.RenderOpen())
                    {
                        context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
                    }
                    bitmap.Render(drawing);
                    bitmap.Render(content);
                    string variant = ReferenceEquals(window, export) ? "export" : ReferenceEquals(window, populated) ? "paged" :
                        ReferenceEquals(window, dirty) ? "dirty" : ReferenceEquals(window, invalid) ? "invalid" :
                        ReferenceEquals(window, invalidAutoType) ? "invalid-autotype" : ReferenceEquals(window, fields) ? "protected-fields" : ReferenceEquals(window, attachments) ? "attachments" : "default";
                    string name = $"{window.GetType().Name}{(variant == "default" ? "" : "-" + variant)}-{language}-{theme}-{(int)(scale * 100)}";
                    if (scale is 1.0 or 2.0)
                    {
                        using var file = File.Create(Path.Combine(output, name + ".png"));
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
                    }
                    results.Add(new { Window = window.GetType().Name, Variant = variant, Language = language, Theme = theme, Dark = dark, RasterScale = scale, Rendered = true });
                    // No window is shown, activated or sent input. No desktop integration is initialized.
                    if (window is MainWindow main) main.Content = null;
                    if (window is EntryEditDialog editor) editor.CancelForLock(); else window.Close();
                }
                entry.ClearSecrets();
            }
            foreach (var item in page.Entries) item.ClearSecrets();
            File.WriteAllText(Path.Combine(output, "ui-render-results.json"), JsonSerializer.Serialize(new { SyntheticOnly = true, WindowsShown = false, SendInputUsed = false, SettingsIsolated = true, NativeDpiValidated = false, NativeAssistiveTechnologyValidated = false, NativeHighContrastValidated = false, HighContrastPaletteSimulated = true, PagedMainWindowIncluded = true, Cases = results.Count, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(output, "theme-contrast-results.json"), JsonSerializer.Serialize(contrastResults, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine($"Rendered {results.Count} in-memory layout cases; no windows shown or desktop input sent.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally
        {
            string resolved = Path.GetFullPath(temporary);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("SQLitePass-ui-verification-")) throw new InvalidOperationException("Invalid verification cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static void VerifyContrast(string theme, List<object> results)
    {
        var resources = Application.Current.Resources;
        double Luminance(Color color)
        {
            static double Linear(byte value) { double channel = value / 255.0; return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        void Check(string foreground, string background)
        {
            Color text = foreground == "White" ? Colors.White : ((SolidColorBrush)resources[foreground]).Color;
            Color surface = ((SolidColorBrush)resources[background]).Color;
            double a = Luminance(text), b = Luminance(surface), ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
            results.Add(new { Theme = theme, Foreground = foreground, Background = background, Ratio = ratio });
            if (ratio < 4.5) throw new InvalidDataException($"Text contrast below 4.5: {foreground}/{background} ({theme}) = {ratio:F2}");
        }
        foreach (string text in new[] { "TextPrimary", "TextSecondary", "TextMuted" })
            foreach (string surface in new[] { "AppBg", "CardBg", "SidebarBg", "InputBg", "HoverBg", "ActivePillBg", "HeaderBg", "RowAltBg" }) Check(text, surface);
        foreach (string text in new[] { "LinkColor", "WarningText", "DangerText" })
            foreach (string surface in new[] { "AppBg", "CardBg" }) Check(text, surface);
        Check("SelectionFg", "SelectionBg"); Check("AccentForeground", "AccentColor"); Check("AccentForeground", "AccentHover"); Check("BadgeSuccessFg", "BadgeSuccessBg"); Check("ConfirmationForeground", "ConfirmationBg");
    }
}
