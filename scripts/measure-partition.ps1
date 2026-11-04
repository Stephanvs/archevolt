#Requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateRange(1, 20)] [int] $Runs = 3,
    [string] $BaselineAssembly,
    [string] $Output = 'artifacts/partition-study/repeated',
    [switch] $NoBuild,
    [switch] $RuntimeDefaults
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).ProviderPath
$tiered = $env:DOTNET_TieredCompilation
$ready = $env:DOTNET_ReadyToRun
Push-Location $root
try {
    $null = New-Item -ItemType Directory -Force -Path $Output
    $outputPath = (Resolve-Path -LiteralPath $Output).ProviderPath
    if (-not $NoBuild) {
        & dotnet build benchmarks/Archevolt.Benchmarks -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Benchmark build failed.' }
    }
    $assemblies = [ordered]@{}
    if ($BaselineAssembly) {
        $assemblies['baseline'] = (Resolve-Path -LiteralPath $BaselineAssembly).ProviderPath
    }
    $assemblies['candidate'] = (Resolve-Path -LiteralPath 'artifacts/bin/Archevolt.Benchmarks/release/Archevolt.Benchmarks.dll').ProviderPath
    if ($RuntimeDefaults) {
        $env:DOTNET_TieredCompilation = $null
        $env:DOTNET_ReadyToRun = $null
    }
    else {
        $env:DOTNET_TieredCompilation = '0'
        $env:DOTNET_ReadyToRun = '0'
    }
    & dotnet --info | Set-Content -LiteralPath (Join-Path $outputPath 'dotnet-info.txt')
    & git rev-parse HEAD | Set-Content -LiteralPath (Join-Path $outputPath 'head.txt')
    & git diff | Set-Content -LiteralPath (Join-Path $outputPath 'working-tree.patch')
    foreach ($variant in $assemblies.Keys) {
        Get-FileHash -Algorithm SHA256 -LiteralPath $assemblies[$variant],
            (Join-Path (Split-Path -Parent $assemblies[$variant]) 'Archevolt.dll') |
            Export-Csv -NoTypeInformation -LiteralPath (Join-Path $outputPath "$variant-hashes.csv")
    }
    $sourceFiles = @(& rg --files src shared benchmarks -g '*.cs' -g '*.csproj') + @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'scripts/measure-partition.ps1')
    $sourceFiles | ForEach-Object { Get-FileHash -Algorithm SHA256 -LiteralPath $_ } |
        Export-Csv -NoTypeInformation -LiteralPath (Join-Path $outputPath 'source-hashes.csv')
    $results = [Collections.Generic.List[object]]::new()
    for ($run = 1; $run -le $Runs; $run++) {
        $order = @($assemblies.Keys)
        if ($run % 2 -eq 0) { [Array]::Reverse($order) }
        foreach ($variant in $order) {
            Write-Host "Measuring $variant launch $run of $Runs"
            $log = Join-Path $outputPath "$variant-$run.csv"
            & dotnet $assemblies[$variant] --partition-matrix | Set-Content -LiteralPath $log
            if ($LASTEXITCODE -ne 0) { throw "Measurement failed. See $log" }
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
    $results | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $outputPath 'samples.csv')
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
    $summary | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $outputPath 'summary.csv')
    Write-Host "Measurements saved to $outputPath"
}
finally {
    $env:DOTNET_TieredCompilation = $tiered
    $env:DOTNET_ReadyToRun = $ready
    Pop-Location
}
