#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('Matrix', 'Bdn', 'Disassembly', 'All')]
    [string] $Mode = 'Matrix',
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).ProviderPath
$Output = Join-Path $Root 'artifacts/performance'
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
    & git status --short | Set-Content -LiteralPath (Join-Path $Output "working-tree-$Mode.txt")
    $sourceFiles = @(& rg --files src shared benchmarks -g '*.cs' -g '*.csproj') +
        @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'scripts/measure.ps1')
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
