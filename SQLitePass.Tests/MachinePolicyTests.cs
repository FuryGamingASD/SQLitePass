using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.Desktop;

namespace SQLitePass.Tests;

public sealed class MachinePolicyTests
{
    internal sealed class Source(bool present, string? json, string? error = null) : IMachinePolicySource
    {
        public MachinePolicySourceData Read() => new(present, json, error);
    }

    [Fact]
    public void MissingPolicyLeavesUserSettingsUnchanged()
    {
        var policy = MachinePolicyManager.Load(new Source(false, null));

        Assert.True(policy.IsValid);
        Assert.False(policy.IsPresent);
        Assert.True(policy.CanOpenVault);
        Assert.Equal(0, policy.EffectiveAutoLockMinutes(0));
        Assert.False(policy.DisableExport);
        Assert.False(policy.DisableAutoType);
        Assert.False(policy.LockOnMinimize(false));
        Assert.False(policy.LockOnSessionLock(false));
    }

    [Fact]
    public void ValidPolicyParsesAllSupportedRestrictions()
    {
        var policy = MachinePolicyManager.Load(new Source(true,
            """{"maxAutoLockMinutes":12,"forceLockOnMinimize":true,"forceLockOnSessionLock":true,"disableExport":true,"disableAutoType":true}"""));

        Assert.True(policy.IsValid);
        Assert.True(policy.IsPresent);
        Assert.Equal(12, policy.Options!.MaxAutoLockMinutes);
        Assert.True(policy.Options.ForceLockOnMinimize);
        Assert.True(policy.Options.ForceLockOnSessionLock);
        Assert.True(policy.DisableExport);
        Assert.True(policy.DisableAutoType);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"unknown\":true}")]
    [InlineData("{\"disableExport\":1}")]
    [InlineData("{\"maxAutoLockMinutes\":0}")]
    [InlineData("{\"maxAutoLockMinutes\":-1}")]
    [InlineData("{\"maxAutoLockMinutes\":61}")]
    [InlineData("{\"disableExport\":true,\"disableExport\":false}")]
    public void PresentMalformedOrUnsupportedPolicyFailsClosed(string json)
    {
        var policy = MachinePolicyManager.Load(new Source(true, json));

        Assert.False(policy.IsValid);
        Assert.False(policy.CanOpenVault);
        Assert.True(policy.DisableExport);
        Assert.True(policy.DisableAutoType);
        Assert.True(policy.LockOnMinimize(false));
        Assert.True(policy.LockOnSessionLock(false));
        Assert.Equal(1, policy.EffectiveAutoLockMinutes(0));
    }

    [Fact]
    public void PolicyReadErrorFailsClosed()
    {
        var policy = MachinePolicyManager.Load(new Source(true, null, "Permission denied."));

        Assert.False(policy.IsValid);
        Assert.False(policy.CanOpenVault);
        Assert.True(policy.DisableExport);
        Assert.True(policy.DisableAutoType);
        Assert.Equal(1, policy.EffectiveAutoLockMinutes(0));
    }

    [Fact]
    public void MaximumTimeoutClampsLongAndDisabledUserPreferencesButAllowsShorterOnes()
    {
        var policy = MachinePolicyManager.Load(new Source(true, "{\"maxAutoLockMinutes\":10}"));

        Assert.Equal(10, policy.EffectiveAutoLockMinutes(0));
        Assert.Equal(10, policy.EffectiveAutoLockMinutes(-1));
        Assert.Equal(10, policy.EffectiveAutoLockMinutes(60));
        Assert.Equal(4, policy.EffectiveAutoLockMinutes(4));
        Assert.False(policy.LockOnMinimize(false));
        Assert.False(policy.LockOnSessionLock(false));
    }
}

public sealed partial class AvaloniaVaultTests
{
    [Fact]
    public async Task InvalidMachinePolicyPreventsOpeningAVault()
    {
        const string password = "Synthetic-policy-invalid-passphrase!";
        var directory = Directory.CreateTempSubdirectory("SQLitePass-policy-invalid-");
        string path = Path.Combine(directory.FullName, "policy-invalid.kpd");
        var settings = new AppSettings { Language = "en", LastOpenedVaultPath = path, EnableDualFileBackup = false };
        try
        {
            using var master = new MasterKey(password);
            await using (var seed = await VaultSession.CreateAsync(path, master, settings, "General", "Recycle bin")) { }

            var policy = MachinePolicyManager.Load(new MachinePolicyTests.Source(true, "{"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var desktop = HeadlessUnitTestSession.StartNew(typeof(HeadlessStartup));
            await desktop.Dispatch(async () =>
            {
                var window = new VaultWindow(settings, useNativeSession: false, clipboard: null, machinePolicy: policy);
                window.Show();
                try
                {
                    Assert.False(window.FindControl<Button>("Unlock")!.IsEnabled);
                    window.FindControl<TextBox>("MasterPassword")!.Text = password;
                    await window.OpenVaultAsync();
                    Assert.False(window.FindControl<Grid>("VaultPanel")!.IsVisible);
                    Assert.Contains("policy is invalid", window.FindControl<TextBlock>("StatusText")!.Text!, StringComparison.OrdinalIgnoreCase);
                }
                finally
                {
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Close();
                    await closed.Task.WaitAsync(timeout.Token);
                }
                return true;
            }, timeout.Token);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task MachinePolicyEnforcesTimeoutSessionLocksAndExportBoundaries()
    {
        const string password = "Synthetic-policy-enforcement-passphrase!";
        var directory = Directory.CreateTempSubdirectory("SQLitePass-policy-apply-");
        string path = Path.Combine(directory.FullName, "policy-apply.kpd");
        string exportPath = Path.Combine(directory.FullName, "must-not-export.csv");
        var settings = new AppSettings
        {
            Language = "en", LastOpenedVaultPath = path, EnableDualFileBackup = false,
            AutoLockMinutes = 0, AutoLockOnMinimize = false, AutoLockOnWindowsLock = false, MinimizeToTray = false
        };
        try
        {
            using var master = new MasterKey(password);
            await using (var seed = await VaultSession.CreateAsync(path, master, settings, "General", "Recycle bin"))
            {
                var entry = new PwEntry { GroupId = seed.Metadata.RootGroupId!.Value, Title = "Synthetic policy entry", Password = "Synthetic-only" };
                try { await seed.Storage.SaveEntryAsync(entry, seed.Crypto); }
                finally { entry.ClearSecrets(); }
            }

            var policy = MachinePolicyManager.Load(new MachinePolicyTests.Source(true,
                """{"maxAutoLockMinutes":1,"forceLockOnMinimize":true,"forceLockOnSessionLock":true,"disableExport":true,"disableAutoType":true}"""));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await using var desktop = HeadlessUnitTestSession.StartNew(typeof(HeadlessStartup));
            await desktop.Dispatch(async () =>
            {
                var window = new VaultWindow(settings, useNativeSession: false, clipboard: null, machinePolicy: policy);
                window.Show();
                try
                {
                    window.FindControl<TextBox>("MasterPassword")!.Text = password;
                    await window.OpenVaultAsync();
                    Assert.True(window.FindControl<Grid>("VaultPanel")!.IsVisible);

                    bool pickerCalled = false;
                    await window.ExportVaultFileAsync(() =>
                    {
                        pickerCalled = true;
                        return Task.FromResult<string?>(exportPath);
                    });
                    Assert.False(pickerCalled);
                    Assert.False(File.Exists(exportPath));

                    var row = Assert.IsType<EntryRow>(Assert.Single(window.FindControl<ListBox>("Entries")!.Items));
                    await window.AutoTypeEntryAsync(row, timeout.Token);
                    Assert.Contains("Auto-Type is disabled", window.FindControl<TextBlock>("StatusText")!.Text!, StringComparison.OrdinalIgnoreCase);

                    await window.CheckDesktopIdleAsync(TimeSpan.FromSeconds(59));
                    Assert.True(window.FindControl<Grid>("VaultPanel")!.IsVisible);
                    await window.CheckDesktopIdleAsync(TimeSpan.FromMinutes(1));
                    Assert.False(window.FindControl<Grid>("VaultPanel")!.IsVisible);

                    window.FindControl<TextBox>("MasterPassword")!.Text = password;
                    await window.OpenVaultAsync();
                    await window.ApplySystemLockStateAsync(true);
                    Assert.False(window.FindControl<Grid>("VaultPanel")!.IsVisible);
                    await window.ApplySystemLockStateAsync(false);

                    window.FindControl<TextBox>("MasterPassword")!.Text = password;
                    await window.OpenVaultAsync();
                    window.WindowState = WindowState.Minimized;
                    await window.CheckDesktopIdleAsync(TimeSpan.Zero);
                    Assert.False(window.FindControl<Grid>("VaultPanel")!.IsVisible);
                }
                finally
                {
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.WindowState = WindowState.Normal;
                    window.Close();
                    await closed.Task.WaitAsync(timeout.Token);
                }
                return true;
            }, timeout.Token);
        }
        finally { directory.Delete(recursive: true); }
    }
}
