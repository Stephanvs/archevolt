#Requires -Version 7.2
<#
.SYNOPSIS
Compares the public API of a preserved build with the current build, including generated consumer APIs.

.DESCRIPTION
Runs Microsoft.DotNet.ApiCompat in strict mode, so additions fail as well as removals, over the runtime and over
consumer assemblies whose public members the generator emits. -Baseline is a copy of artifacts/bin taken before
the change; the current artifacts/bin must already be built. Install the tool once with
dotnet tool install --tool-path artifacts/tools Microsoft.DotNet.ApiCompat.Tool
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Baseline,
    [string] $Tool = 'artifacts/tools/apicompat'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).ProviderPath
$Baseline = (Resolve-Path -LiteralPath $Baseline).ProviderPath
$Tool = [IO.Path]::GetFullPath($Tool, $Root)
$assemblies = [ordered]@{
    'Archevolt' = 'Archevolt.dll'
    'Archevolt.Benchmarks' = 'Archevolt.Benchmarks.dll'
    'Archevolt.Tests' = 'Archevolt.Tests.dll'
    'Archeoids' = 'Archeoids.dll'
    'Archeoids.Browser' = 'Archeoids.Browser.dll'
    'Archevolt.Particles' = 'Archevolt.Particles.dll'
}
$runtime = @(dotnet --list-runtimes | Select-String '^Microsoft\.NETCore\.App (11\.\S+) \[(.+)\]$')[-1].Matches[0]
$frameworkReferences = Join-Path $runtime.Groups[2].Value $runtime.Groups[1].Value
$failed = @()
foreach ($project in $assemblies.Keys) {
    $left = Join-Path $Baseline "$project/release"
    $right = Join-Path $Root "artifacts/bin/$project/release"
    & $Tool --left-assembly (Join-Path $left $assemblies[$project]) --right-assembly (Join-Path $right $assemblies[$project]) `
        --left-assembly-references "$left,$frameworkReferences" --right-assembly-references "$right,$frameworkReferences" --strict-mode `
        --enable-rule-attributes-must-match --enable-rule-cannot-change-parameter-name
    if ($LASTEXITCODE -ne 0) { $failed += $project }
    Write-Host "$project`: $(if ($LASTEXITCODE -eq 0) { 'unchanged' } else { 'CHANGED' })"
}
if ($failed) { throw "Public API changed in: $($failed -join ', ')" }
