[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$KeePassExePath,
    [Parameter(Mandatory)][string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$assemblyPath = (Resolve-Path -LiteralPath $KeePassExePath).ProviderPath
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) {
    throw 'Refusing to overwrite an existing output file.'
}

[Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null
$omega = [char]0x03A9
$database = New-Object KeePassLib.PwDatabase
$key = New-Object KeePassLib.Keys.CompositeKey
$key.AddUserKey((New-Object KeePassLib.Keys.KcpPassword('synthetic KeePass 2.62 fixture only')))
$database.New([KeePassLib.Serialization.IOConnectionInfo]::FromPath($output), $key)
try {
    $database.RootGroup.Name = 'SQLitePass independent KDBX fixture'
    $group = New-Object KeePassLib.PwGroup($true, $true, "Accounts $omega", [KeePassLib.PwIcon]::FolderOpen)
    $database.RootGroup.AddGroup($group, $true)

    $entry = New-Object KeePassLib.PwEntry($true, $true)
    $entry.Strings.Set('Title', (New-Object KeePassLib.Security.ProtectedString($false, "KeePass 2.62 $omega fixture")))
    $entry.Strings.Set('UserName', (New-Object KeePassLib.Security.ProtectedString($false, 'synthetic-user')))
    $entry.Strings.Set('Password', (New-Object KeePassLib.Security.ProtectedString($true, 'synthetic-password-2.62')))
    $entry.Strings.Set('URL', (New-Object KeePassLib.Security.ProtectedString($false, 'https://example.invalid/keepass-2.62')))
    $entry.Strings.Set('Notes', (New-Object KeePassLib.Security.ProtectedString($false, 'Synthetic independent KDBX 4.0 producer fixture')))
    $entry.Tags.Add('synthetic')
    $group.AddEntry($entry, $true)
    $database.Save($null)
}
finally {
    $database.Close()
}

$bytes = [IO.File]::ReadAllBytes($output)
[pscustomobject]@{
    Path = $output
    Length = $bytes.Length
    Major = [BitConverter]::ToUInt32($bytes, 8) -shr 16
    Minor = [BitConverter]::ToUInt32($bytes, 8) -band 0xFFFF
    SHA256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
}
