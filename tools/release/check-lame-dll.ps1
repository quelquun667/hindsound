<#
.SYNOPSIS
    Refuses a libmp3lame DLL that a player's PC could not load or that NAudio.Lame could not call.
.DESCRIPTION
    Reads the PE file directly, with no tool from Visual Studio, and fails on the first group of problems found:
    - not a 64-bit x64 DLL;
    - an import outside a short list of Windows DLLs, so nothing from the Visual C++ or Universal CRT redistributables;
    - a missing export: the names in lame-exports.txt, which are every function NAudio.Lame 2.1.0 imports;
    - with -ReferenceDll, an export of that DLL that this one lacks;
    - a version other than 3.100, asked of the DLL itself through get_lame_short_version.
.PARAMETER DllPath
    The DLL to check.
.PARAMETER ReferenceDll
    The DLL of the NAudio.Lame package, to compare the exports with.
.EXAMPLE
    ./tools/release/check-lame-dll.ps1 -DllPath out/libmp3lame.64.dll -ReferenceDll publish/libmp3lame.64.dll
#>
param(
    [Parameter(Mandatory = $true)][string]$DllPath,
    [string]$ReferenceDll
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedVersion = '3.100'

# /MT leaves KERNEL32 alone in practice. An allow-list, so that a new CRT DLL name cannot slip through.
$allowedImports = 'KERNEL32.dll', 'NTDLL.dll', 'USER32.dll', 'ADVAPI32.dll'

function Read-PeSummary([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).ProviderPath)

    function Get-U16([long]$Offset) {
        if ($Offset -lt 0 -or $Offset + 2 -gt $bytes.Length) { throw "$Path is cut short: no 2 bytes at offset $Offset." }
        [BitConverter]::ToUInt16($bytes, $Offset)
    }
    function Get-U32([long]$Offset) {
        if ($Offset -lt 0 -or $Offset + 4 -gt $bytes.Length) { throw "$Path is cut short: no 4 bytes at offset $Offset." }
        [BitConverter]::ToUInt32($bytes, $Offset)
    }

    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw "$Path is not a PE file: no MZ header." }
    $pe = [long](Get-U32 0x3C)
    if ((Get-U32 $pe) -ne 0x00004550) { throw "$Path is not a PE file: no PE signature." }

    $machine = Get-U16 ($pe + 4)
    $sectionCount = Get-U16 ($pe + 6)
    $optionalSize = Get-U16 ($pe + 20)
    $characteristics = Get-U16 ($pe + 22)
    $optional = $pe + 24
    $magic = Get-U16 $optional

    $summary = [ordered]@{
        Machine = $machine
        Magic   = $magic
        IsDll   = (($characteristics -band 0x2000) -ne 0)
        Imports = @()
        Exports = @()
    }
    # A 32-bit file has its directories elsewhere: stop here, the caller refuses it from these fields.
    if ($magic -ne 0x20B) { return $summary }

    $sections = @()
    $sectionTable = $optional + $optionalSize
    for ($i = 0; $i -lt $sectionCount; $i++) {
        $at = $sectionTable + 40 * $i
        $sections += , @((Get-U32 ($at + 12)), [Math]::Max((Get-U32 ($at + 8)), (Get-U32 ($at + 16))), (Get-U32 ($at + 20)))
    }

    function Get-Offset([long]$Rva) {
        foreach ($section in $sections) {
            if ($Rva -ge $section[0] -and $Rva -lt $section[0] + $section[1]) {
                return [long]($Rva - $section[0] + $section[2])
            }
        }
        throw "$Path has the address $Rva outside its sections."
    }
    function Get-AsciiAt([long]$Rva) {
        $start = Get-Offset $Rva
        $end = [Array]::IndexOf($bytes, [byte]0, [int]$start)
        if ($end -lt 0) { throw "$Path has an unterminated name at offset $start." }

        [Text.Encoding]::ASCII.GetString($bytes, $start, $end - $start)
    }
    function Get-DirectoryRva([int]$Index) {
        if ($Index -ge (Get-U32 ($optional + 108))) { return [long]0 }
        [long](Get-U32 ($optional + 112 + 8 * $Index))
    }

    $exportRva = Get-DirectoryRva 0
    if ($exportRva -ne 0) {
        $directory = Get-Offset $exportRva
        $nameCount = Get-U32 ($directory + 24)
        $namesAt = Get-Offset (Get-U32 ($directory + 32))
        $summary.Exports = @(for ($i = 0; $i -lt $nameCount; $i++) { Get-AsciiAt (Get-U32 ($namesAt + 4 * $i)) })
    }

    # Entries of 20 bytes, with the name of the DLL at +12, and a last entry of zeros.
    $imports = @()
    $importRva = Get-DirectoryRva 1
    if ($importRva -ne 0) {
        $at = Get-Offset $importRva
        while ($true) {
            $nameRva = Get-U32 ($at + 12)
            if ($nameRva -eq 0) { break }
            $imports += Get-AsciiAt $nameRva
            $at += 20
        }
    }
    $summary.Imports = $imports
    $summary
}

function Get-LameShortVersion([string]$Path) {
    if (-not ('HindsoundLameProbe' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HindsoundLameProbe
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetText();

    public static string ShortVersion(string path)
    {
        // LOAD_WITH_ALTERED_SEARCH_PATH: its imports resolve as for a file next to the exe, not from the folder of this shell.
        var module = LoadLibraryExW(path, IntPtr.Zero, 8);
        if (module == IntPtr.Zero) throw new InvalidOperationException("The DLL does not load: Win32 error " + Marshal.GetLastWin32Error() + ".");
        try
        {
            var function = GetProcAddress(module, "get_lame_short_version");
            if (function == IntPtr.Zero) throw new InvalidOperationException("The DLL has no get_lame_short_version.");
            var call = (GetText)Marshal.GetDelegateForFunctionPointer(function, typeof(GetText));
            return Marshal.PtrToStringAnsi(call());
        }
        finally
        {
            FreeLibrary(module);
        }
    }
}
'@
    }
    [HindsoundLameProbe]::ShortVersion((Resolve-Path -LiteralPath $Path).ProviderPath)
}

$summary = Read-PeSummary $DllPath

Write-Output ("Machine 0x{0:X4}, optional header 0x{1:X3}, DLL: {2}" -f $summary.Machine, $summary.Magic, $summary.IsDll)
if ($summary.Machine -ne 0x8664 -or $summary.Magic -ne 0x20B) {
    throw ("$DllPath is not an x64 file: machine 0x{0:X4}, optional header 0x{1:X3}." -f $summary.Machine, $summary.Magic)
}
if (-not $summary.IsDll) {
    throw "$DllPath is not a DLL."
}

Write-Output "Imports: $($summary.Imports -join ', ')"
$badImports = @($summary.Imports | Where-Object { $allowedImports -notcontains $_ })
if ($badImports.Count -gt 0) {
    throw "$DllPath needs a DLL that a player's PC may lack: $($badImports -join ', '). Allowed: $($allowedImports -join ', ')."
}

$required = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'lame-exports.txt') | Where-Object { $_ -and $_ -notmatch '^\s*#' })
if ($required.Count -eq 0) {
    throw 'lame-exports.txt lists nothing.'
}
Write-Output "Exports: $($summary.Exports.Count), of which NAudio.Lame needs $($required.Count)."
# -ccontains: an export name is case-sensitive for GetProcAddress.
$missing = @($required | Where-Object { $summary.Exports -cnotcontains $_ })
if ($missing.Count -gt 0) {
    throw "$DllPath lacks exports that NAudio.Lame calls: $($missing -join ', ')."
}

if ($ReferenceDll) {
    $reference = Read-PeSummary $ReferenceDll
    $lost = @($reference.Exports | Where-Object { $summary.Exports -cnotcontains $_ })
    $gained = @($summary.Exports | Where-Object { $reference.Exports -cnotcontains $_ })
    Write-Output "Compared with ${ReferenceDll}: $($reference.Exports.Count) exports there, $($lost.Count) lost, $($gained.Count) gained."
    if ($gained.Count -gt 0) { Write-Output "Gained: $($gained -join ', ')" }
    if ($lost.Count -gt 0) {
        throw "$DllPath lacks exports of the DLL it replaces: $($lost -join ', ')."
    }
}

$version = Get-LameShortVersion $DllPath
Write-Output "get_lame_short_version: $version"
if ($version -cne $expectedVersion) {
    throw "$DllPath says it is LAME $version, expected $expectedVersion."
}

Write-Output "$DllPath passed. SHA-256: $((Get-FileHash -Algorithm SHA256 -LiteralPath $DllPath).Hash.ToLowerInvariant())"
