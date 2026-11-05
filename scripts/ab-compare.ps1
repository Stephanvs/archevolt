#Requires -Version 7.2
<#
.SYNOPSIS
Alternates BenchmarkDotNet processes between two preserved benchmark builds.

.DESCRIPTION
Runs the same BenchmarkDotNet selection from a baseline output directory and a candidate output directory,
one process at a time, in A-B-B-A order. Copy artifacts/bin/Archevolt.Benchmarks/release to preserve a build.

Writes samples.csv (one mean per case and process), summary.csv, and summary.md to -Output. "faster" and
"slower" mean the candidate's range lies entirely outside the baseline's range; "overlap" means it does not.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $Candidate,
    [Parameter(Mandatory)] [string[]] $Filter,
    [ValidateRange(1, 20)] [int] $Launches = 3,
    [ValidateSet('Default', 'Short', 'Medium')] [string] $Job = 'Default',
    # Fixes the iteration plan, for example '--warmupCount 6 --iterationCount 15 --iterationTime 250'.
    [string] $Iterations = '',
    [Parameter(Mandatory)] [string] $Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
$Root = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).ProviderPath
$Output = [IO.Path]::GetFullPath($Output, $Root)
# pwsh -File passes a comma-separated list as one string.
$Filter = @($Filter | ForEach-Object { $_ -split ',' } | ForEach-Object Trim | Where-Object { $_ })
$null = New-Item -ItemType Directory -Force -Path $Output
$sides = [ordered]@{
    baseline = (Resolve-Path -LiteralPath $Baseline).ProviderPath
    candidate = (Resolve-Path -LiteralPath $Candidate).ProviderPath
}

function Get-Median([double[]] $Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

$environment = @("UTC=$([DateTime]::UtcNow.ToString('O'))", "Launches=$Launches", "Job=$Job", "Iterations=$Iterations", "Filter=$($Filter -join ' ')",
    "Sdk=$(dotnet --version)", "Head=$(git -C $Root rev-parse HEAD)")
foreach ($side in $sides.Keys) {
    $environment += "$side=$($sides[$side])"
    $environment += @(Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $sides[$side] 'Archevolt.dll'),
        (Join-Path $sides[$side] 'Archevolt.Benchmarks.dll') | ForEach-Object { "$side $(Split-Path -Leaf $_.Path)=$($_.Hash)" })
}
$environment | Set-Content -LiteralPath (Join-Path $Output 'environment.txt')

$samples = [Collections.Generic.List[object]]::new()
$sequence = 0
for ($launch = 0; $launch -lt $Launches; $launch++) {
    $order = @($sides.Keys)
    if ($launch % 2 -eq 1) { [Array]::Reverse($order) }
    foreach ($side in $order) {
        $sequence++
        $name = "$sequence-$side-$launch"
        $artifacts = Join-Path $Output $name
        $load = if ($IsWindows) { (Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average } else { $null }
        Write-Host "[$sequence] $side launch $launch (CPU load before: $load%)"
        $arguments = @((Join-Path $sides[$side] 'Archevolt.Benchmarks.dll'), '--filter') + $Filter +
            @('--inProcess', '--launchCount', '1', '--memory', '--job', $Job, '--exporters', 'json', '--artifacts', $artifacts) +
            @($Iterations -split ' ' | Where-Object { $_ })
        & dotnet @arguments *> (Join-Path $Output "$name.txt")
        if ($LASTEXITCODE -ne 0) { throw "BenchmarkDotNet exited $LASTEXITCODE; see $(Join-Path $Output "$name.txt")" }
        $reports = @(Get-ChildItem -LiteralPath (Join-Path $artifacts 'results') -Filter '*-report-full-compressed.json')
        if ($reports.Count -eq 0) { throw "No report in $artifacts" }
        foreach ($report in $reports) {
            foreach ($benchmark in (Get-Content -LiteralPath $report.FullName -Raw | ConvertFrom-Json).Benchmarks) {
                $values = @($benchmark.Measurements | Where-Object { $_.IterationMode -eq 'Workload' -and $_.IterationStage -eq 'Result' } |
                    ForEach-Object { $_.Nanoseconds / $_.Operations })
                if ($values.Count -eq 0) { throw "No measurements for $($benchmark.Method) in $($report.FullName)" }
                $samples.Add([pscustomobject]@{
                    Case = "$($benchmark.Type).$($benchmark.Method)($($benchmark.Parameters))"
                    Side = $side
                    Launch = $launch
                    Sequence = $sequence
                    CpuLoadBefore = $load
                    MeanNs = ($values | Measure-Object -Average).Average
                    Iterations = $values.Count
                    AllocatedBytes = $benchmark.Memory.BytesAllocatedPerOperation
                })
            }
        }
    }
}
$samples | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Output 'samples.csv')

$summary = foreach ($case in $samples | Group-Object Case) {
    $by = @{}
    foreach ($side in $sides.Keys) { $by[$side] = @($case.Group | Where-Object Side -eq $side) }
    $a = @($by.baseline | ForEach-Object MeanNs)
    $b = @($by.candidate | ForEach-Object MeanNs)
    $aMin = ($a | Measure-Object -Minimum).Minimum; $aMax = ($a | Measure-Object -Maximum).Maximum
    $bMin = ($b | Measure-Object -Minimum).Minimum; $bMax = ($b | Measure-Object -Maximum).Maximum
    [pscustomobject]@{
        Case = $case.Name
        BaselineUs = (Get-Median $a) / 1000; BaselineMinUs = $aMin / 1000; BaselineMaxUs = $aMax / 1000
        CandidateUs = (Get-Median $b) / 1000; CandidateMinUs = $bMin / 1000; CandidateMaxUs = $bMax / 1000
        Ratio = (Get-Median $b) / (Get-Median $a)
        BaselineSpread = ($aMax - $aMin) / (Get-Median $a)
        Verdict = if ($bMax -lt $aMin) { 'faster' } elseif ($bMin -gt $aMax) { 'slower' } else { 'overlap' }
        BaselineAllocated = ($by.baseline | ForEach-Object AllocatedBytes | Sort-Object -Unique) -join '/'
        CandidateAllocated = ($by.candidate | ForEach-Object AllocatedBytes | Sort-Object -Unique) -join '/'
    }
}
$summary | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Output 'summary.csv')
$lines = @("Baseline: $($sides.baseline)", '', "Candidate: $($sides.candidate)", '',
    '| Case | Baseline µs [range] | Candidate µs [range] | Candidate/baseline | Baseline spread | Verdict | Allocated B (base/cand) |',
    '| --- | ---: | ---: | ---: | ---: | --- | ---: |') + @($summary | ForEach-Object {
        [string]::Format('| `{0}` | {1:G5} [{2:G5}, {3:G5}] | {4:G5} [{5:G5}, {6:G5}] | {7:F3} | {8:P1} | {9} | {10} / {11} |',
            $_.Case, $_.BaselineUs, $_.BaselineMinUs, $_.BaselineMaxUs, $_.CandidateUs, $_.CandidateMinUs, $_.CandidateMaxUs,
            $_.Ratio, $_.BaselineSpread, $_.Verdict, $_.BaselineAllocated, $_.CandidateAllocated) })
$lines | Set-Content -LiteralPath (Join-Path $Output 'summary.md')
$lines
