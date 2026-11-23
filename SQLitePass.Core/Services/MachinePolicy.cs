using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SQLitePass.Core.Services;

public sealed record MachinePolicyOptions
{
    public int? MaxAutoLockMinutes { get; init; }
    public bool ForceLockOnMinimize { get; init; }
    public bool ForceLockOnSessionLock { get; init; }
    public bool DisableExport { get; init; }
    public bool DisableAutoType { get; init; }
}

public sealed record MachinePolicyState(bool IsPresent, MachinePolicyOptions? Options, string? Error)
{
    public static MachinePolicyState Unmanaged { get; } = new(false, new MachinePolicyOptions(), null);
    public bool IsValid => Error == null;
    public bool CanOpenVault => IsValid;
    public bool DisableExport => !IsValid || Options?.DisableExport == true;
    public bool DisableAutoType => !IsValid || Options?.DisableAutoType == true;
    public bool ForceLockOnMinimize => !IsValid || Options?.ForceLockOnMinimize == true;
    public bool ForceLockOnSessionLock => !IsValid || Options?.ForceLockOnSessionLock == true;
    public int? MaxAutoLockMinutes => IsValid ? Options?.MaxAutoLockMinutes : 1;

    public int EffectiveAutoLockMinutes(int userMinutes)
    {
        int? maximum = MaxAutoLockMinutes;
        if (maximum == null) return userMinutes;
        // A zero user preference means “disabled”; a finite machine maximum makes it the maximum timeout.
        return userMinutes <= 0 ? maximum.Value : Math.Min(userMinutes, maximum.Value);
    }

    public bool LockOnMinimize(bool userSetting) => userSetting || ForceLockOnMinimize;
    public bool LockOnSessionLock(bool userSetting) => userSetting || ForceLockOnSessionLock;
}

internal sealed record MachinePolicySourceData(bool IsPresent, string? Json, string? Error = null);

internal interface IMachinePolicySource
{
    MachinePolicySourceData Read();
}

public static class MachinePolicyManager
{
    private const int MaximumPolicyBytes = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static MachinePolicyState LoadSystemPolicy() => Load(CreateSystemSource());

    internal static MachinePolicyState Load(IMachinePolicySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        MachinePolicySourceData input;
        try { input = source.Read(); }
        catch (Exception error)
        { return Invalid(present: true, error.GetType().Name); }

        if (input.Error != null) return Invalid(input.IsPresent, input.Error);
        if (!input.IsPresent) return MachinePolicyState.Unmanaged;
        if (input.Json == null) return Invalid(present: true, "Policy content is missing.");
        if (Encoding.UTF8.GetByteCount(input.Json) > MaximumPolicyBytes)
            return Invalid(present: true, "Policy exceeds the size limit.");

        try { return new(true, Parse(input.Json), null); }
        catch (Exception error) when (error is JsonException or InvalidDataException or FormatException or OverflowException)
        { return Invalid(present: true, error.Message); }
    }

    private static MachinePolicyState Invalid(bool present, string error) => new(present, null, error);

    private static MachinePolicyOptions Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The policy root must be an object.");

        int? maximum = null;
        bool forceMinimize = false, forceSessionLock = false, disableExport = false, disableAutoType = false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("The policy contains a duplicate field.");
            switch (property.Name)
            {
                case "maxAutoLockMinutes":
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out int value) || value is < 1 or > 60)
                        throw new InvalidDataException("maxAutoLockMinutes must be an integer from 1 to 60.");
                    maximum = value;
                    break;
                case "forceLockOnMinimize": forceMinimize = ReadBoolean(property); break;
                case "forceLockOnSessionLock": forceSessionLock = ReadBoolean(property); break;
                case "disableExport": disableExport = ReadBoolean(property); break;
                case "disableAutoType": disableAutoType = ReadBoolean(property); break;
                default: throw new InvalidDataException("The policy contains an unknown field.");
            }
        }

        return new MachinePolicyOptions
        {
            MaxAutoLockMinutes = maximum,
            ForceLockOnMinimize = forceMinimize,
            ForceLockOnSessionLock = forceSessionLock,
            DisableExport = disableExport,
            DisableAutoType = disableAutoType
        };
    }

    private static bool ReadBoolean(JsonProperty property)
    {
        if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"{property.Name} must be a boolean.");
        return property.Value.GetBoolean();
    }

    private static IMachinePolicySource CreateSystemSource()
    {
        if (OperatingSystem.IsWindows()) return new WindowsMachinePolicySource();
        if (OperatingSystem.IsLinux()) return new LinuxMachinePolicySource();
        return new UnsupportedMachinePolicySource();
    }

    private sealed class UnsupportedMachinePolicySource : IMachinePolicySource
    {
        public MachinePolicySourceData Read() => new(false, null);
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsMachinePolicySource : IMachinePolicySource
    {
        private const string RegistryPath = @"SOFTWARE\Policies\SQLitePass";

        public MachinePolicySourceData Read()
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(RegistryPath, writable: false);
            if (key == null) return new(false, null);
            if (key.GetSubKeyNames().Length != 0) return new(true, null, "Policy subkeys are not supported.");

            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (string name in key.GetValueNames())
            {
                RegistryValueKind kind = key.GetValueKind(name);
                object? value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (kind != RegistryValueKind.DWord || value is not int number)
                    return new(true, null, "Policy values must be REG_DWORD values.");

                if (name is "forceLockOnMinimize" or "forceLockOnSessionLock" or "disableExport" or "disableAutoType")
                    values[name] = number switch { 0 => false, 1 => true, _ => number };
                else values[name] = number;
            }
            return new(true, JsonSerializer.Serialize(values));
        }
    }

    [SupportedOSPlatform("linux")]
    private sealed class LinuxMachinePolicySource : IMachinePolicySource
    {
        private const string DirectoryPath = "/etc/sqlitepass";
        private const string FilePath = "/etc/sqlitepass/policy.json";
        private const uint FileTypeMask = 0xF000;
        private const uint RegularFile = 0x8000;
        private const uint Directory = 0x4000;
        private const uint GroupOrOtherWrite = 0x12;

        public MachinePolicySourceData Read()
        {
            try
            {
                VerifyRootManagedPath("/etc", Directory);
                if (!TryReadStat(DirectoryPath, out var directory, out int directoryError))
                {
                    if (directoryError == 2) return new(false, null);
                    return new(true, null, "The policy directory cannot be inspected.");
                }
                VerifyRootManaged(directory, Directory, "policy directory");

                if (!TryReadStat(FilePath, out var file, out int fileError))
                {
                    if (fileError == 2) return new(false, null);
                    return new(true, null, "The policy file cannot be inspected.");
                }
                VerifyRootManaged(file, RegularFile, "policy file");

                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] bytes = new byte[MaximumPolicyBytes + 1];
                int count = 0;
                while (count < bytes.Length)
                {
                    int read = stream.Read(bytes, count, bytes.Length - count);
                    if (read == 0) break;
                    count += read;
                }
                if (count > MaximumPolicyBytes) return new(true, null, "Policy exceeds the size limit.");
                try { return new(true, StrictUtf8.GetString(bytes, 0, count)); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidDataException or DecoderFallbackException)
            { return new(true, null, error.Message); }
        }

        private static void VerifyRootManagedPath(string path, uint expectedType)
        {
            if (!TryReadStat(path, out var stat, out _)) throw new InvalidDataException("The system policy path cannot be inspected.");
            VerifyRootManaged(stat, expectedType, "system policy directory");
        }

        private static void VerifyRootManaged(LinuxStat stat, uint expectedType, string description)
        {
            if (stat.UserId != 0 || (stat.Mode & FileTypeMask) != expectedType || (stat.Mode & GroupOrOtherWrite) != 0)
                throw new InvalidDataException($"The {description} must be root-owned, non-symlinked, and not group- or world-writable.");
        }

        private static bool TryReadStat(string path, out LinuxStat stat, out int error)
        {
            if (LStat(path, out stat) == 0) { error = 0; return true; }
            error = Marshal.GetLastPInvokeError();
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LinuxStat
        {
            public ulong Device;
            public ulong Inode;
            public ulong LinkCount;
            public uint Mode;
            public uint UserId;
            public uint GroupId;
            public int Padding;
            public ulong SpecialDevice;
            public long Size;
            public long BlockSize;
            public long Blocks;
            public long AccessSeconds;
            public long AccessNanoseconds;
            public long ModifySeconds;
            public long ModifyNanoseconds;
            public long ChangeSeconds;
            public long ChangeNanoseconds;
            public long Reserved0;
            public long Reserved1;
            public long Reserved2;
        }

        [DllImport("libc", EntryPoint = "lstat", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int LStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out LinuxStat stat);
    }
}
