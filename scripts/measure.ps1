#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('Matrix', 'SingleItem', 'Partition', 'Bdn', 'Disassembly', 'All')]
    [string] $Mode = 'Matrix',
    [ValidateRange(1, 20)] [int] $Runs = 3,
    [string] $BaselineAssembly,
    [string] $Output = 'artifacts/performance',
    [switch] $NoBuild,
    [switch] $RuntimeDefaults
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).ProviderPath
$Output = [IO.Path]::GetFullPath($Output, $Root)
$Project = Join-Path $Root 'benchmarks/Archevolt.Benchmarks/Archevolt.Benchmarks.csproj'
$Assembly = Join-Path $Root 'artifacts/bin/Archevolt.Benchmarks/release/Archevolt.Benchmarks.dll'
$null = New-Item -ItemType Directory -Force -Path $Output

function Invoke-Recorded([string] $Name, [string[]] $Arguments) {
    $log = Join-Path $Output $Name
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
    $timer.Stop()
    "$Name,$($timer.Elapsed.TotalSeconds.ToString('F3', [Globalization.CultureInfo]::InvariantCulture)),$code" |
        Add-Content -LiteralPath (Join-Path $Output 'durations.csv')
    if ($code -ne 0) { throw "dotnet exited $code; see $log" }
}

Push-Location $Root
try {
    & dotnet --info | Set-Content -LiteralPath (Join-Path $Output 'dotnet-info.txt')
    @("UTC=$([DateTime]::UtcNow.ToString('O'))", "Mode=$Mode", "Configuration=Release", "NoBuild=$NoBuild",
        "Runs=$Runs", "BaselineAssembly=$BaselineAssembly", "RuntimeDefaults=$RuntimeDefaults",
        "DOTNET_TieredCompilation=$env:DOTNET_TieredCompilation", "DOTNET_TieredPGO=$env:DOTNET_TieredPGO",
        "DOTNET_ReadyToRun=$env:DOTNET_ReadyToRun", "COMPlus_GCServer=$env:COMPlus_GCServer") |
        Set-Content -LiteralPath (Join-Path $Output "environment-$Mode.txt")
    if ($IsWindows) {
        Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed |
            Format-List | Out-String | Set-Content -LiteralPath (Join-Path $Output 'cpu.txt')
        Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber, TotalVisibleMemorySize |
            Format-List | Out-String | Set-Content -LiteralPath (Join-Path $Output 'os.txt')
    }
    & git rev-parse HEAD | Set-Content -LiteralPath (Join-Path $Output 'head.txt')
    & git diff | Set-Content -LiteralPath (Join-Path $Output 'working-tree.patch')
    & git status --short | Set-Content -LiteralPath (Join-Path $Output "working-tree-$Mode.txt")
    $sourceFiles = @(& rg --files src shared benchmarks -g '*.cs' -g '*.csproj') +
        @('Directory.Build.props', 'Directory.Packages.props', 'scripts/measure.ps1')
    $sourceFiles | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 } |
        Export-Csv -LiteralPath (Join-Path $Output "source-hashes-$Mode.csv") -NoTypeInformation
    if (-not $NoBuild) {
        $generated = Join-Path $Output 'generated'
        Invoke-Recorded 'build.txt' @('build', $Project, '-c', 'Release', '-warnaserror',
            '-p:EmitCompilerGeneratedFiles=true', "-p:CompilerGeneratedFilesOutputPath=$generated")
        Invoke-Recorded 'no-change-build.txt' @('build', $Project, '-c', 'Release', '--no-restore', '-warnaserror')
    }
    if ($Mode -in @('Matrix', 'All')) {
        $tiered = $env:DOTNET_TieredCompilation
        $ready = $env:DOTNET_ReadyToRun
        try {
            $env:DOTNET_TieredCompilation = '0'
            $env:DOTNET_ReadyToRun = '0'
            Invoke-Recorded 'matrix.csv' @($Assembly, '--matrix')
        }
        finally {
            $env:DOTNET_TieredCompilation = $tiered
            $env:DOTNET_ReadyToRun = $ready
        }
    }
    if ($Mode -in @('SingleItem', 'All')) {
        $runDirectory = "single-item/$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff'))"
        $singleItemOutput = Join-Path $Output $runDirectory
        $null = New-Item -ItemType Directory -Path (Join-Path $singleItemOutput 'raw')
        $metadata = @('dotnet-info.txt', 'head.txt', "environment-$Mode.txt",
            "working-tree-$Mode.txt", "source-hashes-$Mode.csv")
        if ($IsWindows) { $metadata += @('cpu.txt', 'os.txt') }
        if (-not $NoBuild) { $metadata += @('build.txt', 'no-change-build.txt') }
        foreach ($file in $metadata) {
            Copy-Item -LiteralPath (Join-Path $Output $file) -Destination $singleItemOutput
        }
        @('SingleItemRuns=3', 'SingleItemDOTNET_TieredCompilation=0', 'SingleItemDOTNET_ReadyToRun=0') |
            Add-Content -LiteralPath (Join-Path $singleItemOutput "environment-$Mode.txt")
        $tiered = $env:DOTNET_TieredCompilation
        $ready = $env:DOTNET_ReadyToRun
        try {
            $env:DOTNET_TieredCompilation = '0'
            $env:DOTNET_ReadyToRun = '0'
            $cases = @(Invoke-Recorded "$runDirectory/cases.txt" @($Assembly, '--single-item', '--list'))
            if ($cases.Count -eq 0) { throw 'The single-item runner listed no cases.' }
            $summary = foreach ($case in $cases) {
                $means = @(foreach ($run in 1..3) {
                    $name = "$case-$run"
                    $resultPath = Join-Path $singleItemOutput "raw/$name.json"
                    Invoke-Recorded "$runDirectory/raw/$name.txt" @($Assembly, '--single-item', $case, $resultPath) |
                        Out-Host
                    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
                    ($result.Samples | Measure-Object -Property NanosecondsPerRun -Average).Average / 1000
                })
                $means = @($means | Sort-Object)
                [pscustomobject]@{
                    Harness = 'single-item'
                    Case = $case
                    Runs = $means.Count
                    MedianUs = $means[1].ToString('R', [Globalization.CultureInfo]::InvariantCulture)
                    MinUs = $means[0].ToString('R', [Globalization.CultureInfo]::InvariantCulture)
                    MaxUs = $means[2].ToString('R', [Globalization.CultureInfo]::InvariantCulture)
                }
            }
            $summary | Export-Csv -LiteralPath (Join-Path $singleItemOutput 'summary.csv') -NoTypeInformation
            Copy-Item -LiteralPath (Join-Path $Output 'durations.csv') -Destination $singleItemOutput
            Write-Host "Single-item results: $singleItemOutput"
        }
        finally {
            $env:DOTNET_TieredCompilation = $tiered
            $env:DOTNET_ReadyToRun = $ready
        }
    }
    if ($Mode -in @('Partition', 'All')) {
        $tiered = $env:DOTNET_TieredCompilation
        $ready = $env:DOTNET_ReadyToRun
        try {
            $assemblies = [ordered]@{}
            if ($BaselineAssembly) {
                $assemblies['baseline'] = (Resolve-Path -LiteralPath $BaselineAssembly).ProviderPath
            }
            $assemblies['candidate'] = (Resolve-Path -LiteralPath $Assembly).ProviderPath
            if ($RuntimeDefaults) {
                $env:DOTNET_TieredCompilation = $null
                $env:DOTNET_ReadyToRun = $null
            }
            else {
                $env:DOTNET_TieredCompilation = '0'
                $env:DOTNET_ReadyToRun = '0'
            }
            foreach ($variant in $assemblies.Keys) {
                Get-FileHash -Algorithm SHA256 -LiteralPath $assemblies[$variant],
                    (Join-Path (Split-Path -Parent $assemblies[$variant]) 'Archevolt.dll') |
                    Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Output "$variant-hashes.csv")
            }
            $results = [Collections.Generic.List[object]]::new()
            for ($run = 1; $run -le $Runs; $run++) {
                $order = @($assemblies.Keys)
                if ($run % 2 -eq 0) { [Array]::Reverse($order) }
                foreach ($variant in $order) {
                    Write-Host "Measuring $variant launch $run of $Runs"
                    $name = "$variant-$run.csv"
                    $log = Join-Path $Output $name
                    Invoke-Recorded $name @($assemblies[$variant], '--partition-matrix')
                    $lines = Get-Content -LiteralPath $log
                    if ($lines -notcontains '# zero_process_allocations=true; matrix=ok') {
                        throw "Missing correctness and allocation success marker in $log"
                    }
                    $rows = @($lines | Where-Object { $_ -and -not $_.StartsWith('#') } | ConvertFrom-Csv)
                    if ($rows.Count -ne 120) { throw "Expected 120 cases in $log, found $($rows.Count)." }
                    foreach ($row in $rows) {
                        if ([long]$row.owner_bytes_total -ne 0 -or [long]$row.all_thread_bytes_total -ne 0) {
                            throw "Steady-state allocation in $log for $($row.case)."
                        }
                        $row | Add-Member -NotePropertyName variant -NotePropertyValue $variant
                        $row | Add-Member -NotePropertyName launch -NotePropertyValue $run
                        $results.Add($row)
                    }
                }
            }
            $results | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Output 'samples.csv')
            $summary = foreach ($group in $results | Group-Object variant,case,rows,concurrency) {
                $first = $group.Group[0]
                $values = @($group.Group | ForEach-Object { [double]::Parse($_.median_us, [Globalization.CultureInfo]::InvariantCulture) } | Sort-Object)
                $middle = [int][Math]::Floor($values.Count / 2)
                $median = if ($values.Count % 2) { $values[$middle] } else { ($values[$middle - 1] + $values[$middle]) / 2 }
                [pscustomobject]@{
                    variant = $first.variant
                    case = $first.case
                    rows = [int]$first.rows
                    concurrency = [int]$first.concurrency
                    launches = $values.Count
                    median_us = $median.ToString([Globalization.CultureInfo]::InvariantCulture)
                    min_us = $values[0].ToString([Globalization.CultureInfo]::InvariantCulture)
                    max_us = $values[-1].ToString([Globalization.CultureInfo]::InvariantCulture)
                }
            }
            $summary | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Output 'summary.csv')
            Write-Host "Measurements saved to $Output"
        }
        finally {
            $env:DOTNET_TieredCompilation = $tiered
            $env:DOTNET_ReadyToRun = $ready
        }
    }
    if ($Mode -in @('Bdn', 'All')) {
        Invoke-Recorded 'bdn.txt' @($Assembly, '--filter', '*ReferenceComparisonBenchmarks*', '--job', 'Short',
            '--launchCount', '1', '--warmupCount', '3', '--iterationCount', '8',
            '--artifacts', (Join-Path $Output 'bdn'))
        if (-not (Select-String -LiteralPath (Join-Path $Output 'bdn.txt') -Pattern 'executed benchmarks: 6' -Quiet)) {
            throw 'BenchmarkDotNet did not execute all six reference benchmarks.'
        }
    }
    if ($Mode -in @('Disassembly', 'All')) {
        $saved = @{}
        foreach ($key in 'DOTNET_TieredCompilation', 'DOTNET_ReadyToRun', 'DOTNET_JitDisasm', 'DOTNET_JitStdOutFile') {
            $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        }
        try {
            $env:DOTNET_TieredCompilation = '0'
            $env:DOTNET_ReadyToRun = '0'
            $env:DOTNET_JitDisasm = '*Run* *Handwritten* *ArchInline* *Query* *Block*'
            $env:DOTNET_JitStdOutFile = Join-Path $Output 'jit-disassembly.txt'
            Set-Content -LiteralPath $env:DOTNET_JitStdOutFile -Value '' -NoNewline
            Invoke-Recorded 'disassembly-run.txt' @($Assembly, '--matrix')
            $disassembly = Get-Content -LiteralPath $env:DOTNET_JitStdOutFile -Raw
            $methods = [regex]::Matches($disassembly,
                '(?ms)^; Assembly listing for method (?<name>[^\r\n]+).*?^; Total bytes of code (?<bytes>\d+)\r?\n')
            $selected = @($methods | Where-Object {
                $_.Groups['name'].Value -match 'Movement.*(Run|Query|Block|Handwritten)|ArchInline|InlineQuery'
            })
            $selected | ForEach-Object {
                [pscustomobject]@{ Method = $_.Groups['name'].Value; CodeBytes = $_.Groups['bytes'].Value }
            } | Export-Csv -LiteralPath (Join-Path $Output 'jit-method-sizes.csv') -NoTypeInformation
            $selected.Value -join "`n" | Set-Content -LiteralPath (Join-Path $Output 'jit-selected-methods.txt')
        }
        finally {
            foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
        }
    }
    Get-ChildItem -LiteralPath (Join-Path $Root 'artifacts/bin/Archevolt/release'),
        (Join-Path $Root 'artifacts/bin/Archevolt.Generators/release'),
        (Join-Path $Root 'artifacts/bin/Archevolt.Benchmarks/release') -Filter 'Archevolt*.dll' |
        Select-Object FullName, Length | Export-Csv -LiteralPath (Join-Path $Output 'assembly-sizes.csv') -NoTypeInformation
    if (Test-Path -LiteralPath (Join-Path $Output 'generated')) {
        Get-ChildItem -LiteralPath (Join-Path $Output 'generated') -Recurse -Filter '*.cs' |
            Select-Object FullName, Length | Export-Csv -LiteralPath (Join-Path $Output 'generated-source-sizes.csv') -NoTypeInformation
    }
    if (Test-Path -LiteralPath (Join-Path $Root 'artifacts/publish')) {
        Get-ChildItem -LiteralPath (Join-Path $Root 'artifacts/publish') -Recurse -File -Filter '*.exe' |
            Select-Object FullName, Length | Export-Csv -LiteralPath (Join-Path $Output 'aot-image-sizes.csv') -NoTypeInformation
    }
}
finally { Pop-Location }
