#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [string] $PackageVersion,
    [Parameter(Mandatory)] [string] $SourceRevisionId,
    [string] $CompareWith,
    [string] $ExpectedDigest,
    [switch] $Fingerprint
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.ZipFile

function Read-Package([string] $Path) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path).ProviderPath)
    try {
        $contents = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -eq '.signature.p7s') { continue }
            $stream = $entry.Open()
            try {
                $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream))
                $contents.Add($entry.FullName, $hash)
            }
            finally { $stream.Dispose() }
        }
        $specs = @($zip.Entries | Where-Object FullName -Like '*.nuspec')
        if ($specs.Count -ne 1) { throw "expected one nuspec in $Path" }
        $reader = [System.IO.StreamReader]::new($specs[0].Open())
        try { [xml] $nuspec = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
        $metadata = $nuspec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
        if ($metadata.id -cne 'Archevolt' -or $metadata.version -cne $PackageVersion -or
            $metadata.repository.commit -cne $SourceRevisionId) {
            throw "package identity in $Path does not match Archevolt $PackageVersion at $SourceRevisionId"
        }
        return ,$contents
    }
    finally { $zip.Dispose() }
}

$expected = Read-Package $Package
$names = [System.Collections.Generic.List[string]]::new()
foreach ($name in $expected.Keys) { $names.Add($name) }
$names.Sort([StringComparer]::Ordinal)
$canonical = ($names | ForEach-Object { "$_`0$($expected[$_])`n" }) -join ''
$digest = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
if ($ExpectedDigest -and $digest -cne $ExpectedDigest) {
    throw 'Package content digest does not match the reserved release tag. Do not publish this asset.'
}
if ($CompareWith) {
    $actual = Read-Package $CompareWith
    if ($expected.Count -ne $actual.Count) { throw 'NuGet package entry count differs from the staged package' }
    foreach ($entry in $expected.GetEnumerator()) {
        if (-not $actual.ContainsKey($entry.Key) -or $actual[$entry.Key] -cne $entry.Value) {
            throw "NuGet package differs from the staged package at $($entry.Key)"
        }
    }
    if (-not $Fingerprint) { Write-Host 'NuGet package matches the staged package, excluding the repository signature.' }
}
elseif (-not $Fingerprint) {
    Write-Host "Package identity verified: Archevolt $PackageVersion at $SourceRevisionId"
}
if ($Fingerprint) { Write-Output $digest }
