#Requires -Version 7.2
<#
.SYNOPSIS
Records Archevolt baselines, optionally including its Bevy counterparts.

.DESCRIPTION
Runs the BenchmarkDotNet cases that later roadmap steps gate on, with one launch per process run.
With -IncludeBevy, also runs criterion once per process run with -C target-cpu=native.
Each run yields one mean per case. The summary reports the median of those means and their range,
per the measurement rules. The default run does not require Rust or access Criterion artifacts.

Results go to artifacts/baseline/<UTC timestamp>/: environment.txt, the raw harness logs and
exports, summary.csv, and summary.md.

A step gate that compares only some cases passes -Classes, for example
./scripts/baseline.ps1 -Classes ReferenceComparisonBenchmarks.
An entry may also name one method, for example ReferenceComparisonBenchmarks.GeneratedQuery.
Add -IncludeBevy -Benches movement to refresh the corresponding Bevy reference.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 99)]
    [int] $Runs = 3,
    # Shortens the selected harnesses to check that the script works. Quick results are not baselines.
    [switch] $Quick,
    [switch] $IncludeBevy,
    [ValidateNotNullOrEmpty()]
    [string[]] $Classes = @('EntityPopulationBenchmarks', 'EntityStorageBenchmarks', 'TraversalMatrixBenchmarks',
        'FilterBenchmarks', 'ReferenceComparisonBenchmarks'),
    # Requires -IncludeBevy. Empty runs all Criterion bench targets in bevy-reference.
    [string[]] $Benches = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($Benches.Count -gt 0 -and -not $IncludeBevy) {
    throw '-Benches requires -IncludeBevy. Omit -Benches to run only the .NET benchmarks.'
}
$Root = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).ProviderPath
$Stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$Output = Join-Path $Root "artifacts/baseline/$Stamp"
$Project = Join-Path $Root 'benchmarks/Archevolt.Benchmarks/Archevolt.Benchmarks.csproj'
$Assembly = Join-Path $Root 'artifacts/bin/Archevolt.Benchmarks/release/Archevolt.Benchmarks.dll'
$Crate = Join-Path $Root 'benchmarks/bevy-reference'
$CargoTarget = Join-Path $Crate 'target/native'
[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
$null = New-Item -ItemType Directory -Force -Path $Output

function Invoke-Logged([string] $Log, [string] $Command, [string[]] $Arguments) {
    & $Command @Arguments *> (Join-Path $Output $Log)
    if ($LASTEXITCODE -ne 0) { throw "$Command exited $LASTEXITCODE; see $(Join-Path $Output $Log)" }
}

function Get-Median([double[]] $Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return $sorted[$middle] }
    ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

function New-Row([string] $Harness, [string] $Case, [double[]] $NanosecondsPerRun) {
    if ($NanosecondsPerRun.Count -eq 0) { throw "$Harness recorded no measurements for $Case; see the logs in $Output" }
    [pscustomobject]@{
        Harness = $Harness
        Case = $Case
        Runs = $NanosecondsPerRun.Count
        MedianUs = (Get-Median $NanosecondsPerRun) / 1000
        MinUs = ($NanosecondsPerRun | Measure-Object -Minimum).Minimum / 1000
        MaxUs = ($NanosecondsPerRun | Measure-Object -Maximum).Maximum / 1000
    }
}

Push-Location $Root
try {
    $cpu = if ($IsWindows) { (Get-CimInstance Win32_Processor).Name } else { (Get-Content /proc/cpuinfo | Select-String 'model name' | Select-Object -First 1).Line }
    $os = if ($IsWindows) { (Get-CimInstance Win32_OperatingSystem | ForEach-Object { "$($_.Caption) $($_.Version)" }) } else { (uname -sr) }
    $environment = @("UTC=$Stamp", "Runs=$Runs", "Quick=$Quick", "IncludeBevy=$IncludeBevy", "CPU=$cpu", "OS=$os",
        "Head=$(git rev-parse HEAD)", "WorkingTree=$((git status --short) -join '; ')",
        "DotnetSdk=$(dotnet --version)", "Configuration=Release")
    if ($IncludeBevy) {
        $bevyVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $Crate 'Cargo.lock') -Raw),
            '(?m)^name = "bevy_ecs"\r?\nversion = "([^"]+)"')
        if (-not $bevyVersion.Success) { throw 'Could not find the bevy_ecs version in Cargo.lock.' }
        $environment += @("BevyEcs=$($bevyVersion.Groups[1].Value)", "Rustc=$((rustc -vV) -join '; ')",
            "RUSTFLAGS=-C target-cpu=native")
    }
    else {
        Write-Host 'Bevy benchmarks skipped. Use -IncludeBevy to refresh the Bevy baseline.'
    }
    $environment | Set-Content -LiteralPath (Join-Path $Output 'environment.txt')

    Invoke-Logged 'dotnet-build.txt' 'dotnet' @('build', $Project, '-c', 'Release', '-warnaserror')
    $bdnResults = Join-Path $Output 'bdn'
    $bdnArguments = @($Assembly, '--filter') + @($Classes | ForEach-Object { if ($_.Contains('.')) { "*.$_" } else { "*.$_.*" } }) +
        @('--launchCount', "$Runs", '--exporters', 'json', '--artifacts', $bdnResults)
    if ($Quick) { $bdnArguments += @('--warmupCount', '1', '--iterationCount', '3') }
    Invoke-Logged 'bdn.txt' 'dotnet' $bdnArguments
    # BenchmarkDotNet exports one report per class, however many of its methods were selected.
    $classCount = @($Classes | ForEach-Object { $_.Split('.')[0] } | Sort-Object -Unique).Count
    $reports = @(Get-ChildItem -LiteralPath (Join-Path $bdnResults 'results') -Filter '*-report-full-compressed.json' -ErrorAction SilentlyContinue)
    if ($reports.Count -ne $classCount) { throw "BenchmarkDotNet exported $($reports.Count) of $classCount reports; see $(Join-Path $Output 'bdn.txt')" }

    if ($IncludeBevy) {
        $savedFlags = $env:RUSTFLAGS
        $savedTarget = $env:CARGO_TARGET_DIR
        $savedScheme = if ($IsWindows) { [regex]::Match((powercfg /getactivescheme), '[0-9a-f-]{36}').Value } else { $null }
        try {
            $env:RUSTFLAGS = '-C target-cpu=native'
            $env:CARGO_TARGET_DIR = $CargoTarget
            # BenchmarkDotNet selects the High performance plan for its own runs; give criterion the same.
            if ($savedScheme) { powercfg /setactive SCHEME_MIN }
            $criterionArguments = if ($Quick) { @('--warm-up-time', '0.5', '--measurement-time', '1', '--sample-size', '10') } else { @() }
            for ($run = 1; $run -le $Runs; $run++) {
                Invoke-Logged "criterion-run$run.txt" 'cargo' (@('bench', '--locked', '--manifest-path', (Join-Path $Crate 'Cargo.toml')) +
                    @($Benches | ForEach-Object { '--bench', $_ }) + @('--', '--save-baseline', "$Stamp-run$run") + $criterionArguments)
            }
        }
        finally {
            $env:RUSTFLAGS = $savedFlags
            $env:CARGO_TARGET_DIR = $savedTarget
            if ($savedScheme) { powercfg /setactive $savedScheme }
        }
    }

    $rows = [Collections.Generic.List[object]]::new()
    foreach ($report in $reports) {
        $json = Get-Content -LiteralPath $report.FullName -Raw | ConvertFrom-Json
        foreach ($benchmark in $json.Benchmarks) {
            $perLaunch = @($benchmark.Measurements |
                Where-Object { $_.IterationMode -eq 'Workload' -and $_.IterationStage -eq 'Result' } |
                Group-Object LaunchIndex |
                ForEach-Object { ($_.Group | ForEach-Object { $_.Nanoseconds / $_.Operations } | Measure-Object -Average).Average })
            $rows.Add((New-Row 'BenchmarkDotNet' "$($benchmark.Type).$($benchmark.Method)($($benchmark.Parameters))" $perLaunch))
        }
    }
    if ($IncludeBevy) {
        $criterion = Get-ChildItem -LiteralPath (Join-Path $CargoTarget 'criterion') -Recurse -Directory |
            Where-Object { $_.Name -like "$Stamp-run*" }
        $criterion | Group-Object { (Get-Content -LiteralPath (Join-Path $_.FullName 'benchmark.json') -Raw | ConvertFrom-Json).full_id } |
            ForEach-Object {
                $means = @($_.Group | ForEach-Object {
                    (Get-Content -LiteralPath (Join-Path $_.FullName 'estimates.json') -Raw | ConvertFrom-Json).mean.point_estimate })
                $rows.Add((New-Row 'criterion' $_.Name $means))
            }
    }

    $rows | Export-Csv -LiteralPath (Join-Path $Output 'summary.csv') -NoTypeInformation
    $bevyStatus = if ($IncludeBevy) { 'measured in this run' } else { 'not run; use a saved Bevy baseline for reference' }
    $table = @("Bevy benchmarks: $bevyStatus.", '',
        '| Harness | Case | Runs | Median µs | Min µs | Max µs |', '| --- | --- | ---: | ---: | ---: | ---: |') +
        @($rows | ForEach-Object {
            [string]::Format('| {0} | `{1}` | {2} | {3:F3} | {4:F3} | {5:F3} |',
                $_.Harness, $_.Case, $_.Runs, $_.MedianUs, $_.MinUs, $_.MaxUs) })
    $table | Set-Content -LiteralPath (Join-Path $Output 'summary.md')
    Write-Host "Baseline written to $Output"
}
finally { Pop-Location }
