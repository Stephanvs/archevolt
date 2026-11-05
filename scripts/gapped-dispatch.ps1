#Requires -Version 7.2
<#
.SYNOPSIS
Times movement schedule runs separated by gaps, alternating two preserved benchmark builds.

.DESCRIPTION
Runs scripts/gapped-dispatch.cs once per process and gap, in A-B-B-A order, with a busy or sleeping gap before each
timed run. Each row reports the median, 90th percentile, and mean run time, and cores_busy: process CPU time divided
by wall time over the timed loop, gaps included. With -Interleave, that method runs before each timed run and its own
median and the pair's median are reported.

Writes samples.csv and summary.md to -Output.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $Candidate,
    [string] $Method = 'GeneratedEach',
    [int] $Count = 1000,
    [int] $Concurrency = 4,
    [int] $Runs = 2000,
    [ValidateRange(1, 20)] [int] $Launches = 3,
    # none, busy:<microseconds>, or sleep:<milliseconds>
    [string[]] $Gaps = @('none', 'busy:20', 'busy:200', 'sleep:2'),
    [string] $Interleave = '',
    [Parameter(Mandatory)] [string] $Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
$Root = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).ProviderPath
$Output = [IO.Path]::GetFullPath($Output, $Root)
$Runner = Join-Path $PSScriptRoot 'gapped-dispatch.cs'
$null = New-Item -ItemType Directory -Force -Path $Output
$Gaps = @($Gaps | ForEach-Object { $_ -split ',' } | ForEach-Object Trim | Where-Object { $_ })
$sides = [ordered]@{
    baseline = (Resolve-Path -LiteralPath $Baseline).ProviderPath
    candidate = (Resolve-Path -LiteralPath $Candidate).ProviderPath
}
$columns = 'method', 'count', 'concurrency', 'gap', 'median_us', 'p90_us', 'mean_us', 'cores_busy', 'interleaved_median_us', 'pair_median_us'
$samples = foreach ($gap in $Gaps) {
    for ($launch = 0; $launch -lt $Launches; $launch++) {
        $order = @($sides.Keys)
        if ($launch % 2 -eq 1) { [Array]::Reverse($order) }
        foreach ($side in $order) {
            $arguments = @('run', '--file', $Runner, '--', $sides[$side], $Method, "$Count", "$Concurrency", $gap, "$Runs")
            if ($Interleave) { $arguments += $Interleave }
            $line = @(& dotnet @arguments)[-1]
            if ($LASTEXITCODE -ne 0) { throw "gapped-dispatch.cs exited $LASTEXITCODE for $side $gap" }
            $row = $line | ConvertFrom-Csv -Header $columns
            $row | Add-Member -NotePropertyName side -NotePropertyValue $side
            $row | Add-Member -NotePropertyName launch -NotePropertyValue $launch
            Write-Host "$side $gap launch $launch`: $($row.median_us) us"
            $row
        }
    }
}
$samples | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Output 'samples.csv')
$lines = @("Baseline: $($sides.baseline)", '', "Candidate: $($sides.candidate)", '',
    "$Method, $Count rows, concurrency $Concurrency, $Runs runs per process$(if ($Interleave) { ", $Interleave before each run" })", '',
    '| Gap | Side | Median µs [range] | Cores busy [range] | Interleaved median µs | Pair median µs |', '| --- | --- | ---: | ---: | ---: | ---: |')
foreach ($group in $samples | Group-Object gap, side) {
    $median = @($group.Group | ForEach-Object { [double]$_.median_us } | Sort-Object)
    $cores = @($group.Group | ForEach-Object { [double]$_.cores_busy } | Sort-Object)
    $other = @($group.Group | ForEach-Object { [double]$_.interleaved_median_us } | Sort-Object)
    $pair = @($group.Group | ForEach-Object { [double]$_.pair_median_us } | Sort-Object)
    $mid = [int][Math]::Floor($median.Count / 2)
    $lines += [string]::Format('| {0} | {1} | {2:F2} [{3:F2}, {4:F2}] | {5:F3} [{6:F3}, {7:F3}] | {8:F2} | {9:F2} |',
        $group.Group[0].gap, $group.Group[0].side, $median[$mid], $median[0], $median[-1], $cores[$mid], $cores[0], $cores[-1], $other[$mid], $pair[$mid])
}
$lines | Set-Content -LiteralPath (Join-Path $Output 'summary.md')
$lines
