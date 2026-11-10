#Requires -Version 7.2
<#
.SYNOPSIS
Compares two builds of benchmarks/Archevolt.EachShapes, one process at a time, in alternating order.

.DESCRIPTION
Each side is a directory that holds a published Archevolt.EachShapes executable. With -Remote, the directories are
paths on that SSH host and every process runs there. -Prefix is put in front of the executable, for example
'taskset -c 2,3' or a 'docker run' command that mounts the directories.

.EXAMPLE
./scripts/each-shapes.ps1 -Baseline artifacts/a/shapes-win-jit -Candidate artifacts/b/shapes-win-jit -Output artifacts/ab/shapes

.EXAMPLE
./scripts/each-shapes.ps1 -Remote mini -Prefix 'taskset -c 2,3' -Baseline archevolt-bench/a -Candidate archevolt-bench/b -Output artifacts/ab/mini
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Baseline,
    [Parameter(Mandatory)] [string] $Candidate,
    [Parameter(Mandatory)] [string] $Output,
    [ValidateRange(1, 20)] [int] $Launches = 3,
    [ValidateSet('populated', 'empty')] [string[]] $Modes = @('populated', 'empty'),
    [int] $Rows = 100000,
    [string] $Remote,
    [string] $Prefix = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Invariant = [Globalization.CultureInfo]::InvariantCulture
$Output = [IO.Path]::GetFullPath($Output)
$null = New-Item -ItemType Directory -Force -Path $Output
$Sides = [ordered]@{ baseline = $Baseline; candidate = $Candidate }
$Ssh = if ($IsWindows -and (Test-Path "$env:WINDIR\System32\OpenSSH\ssh.exe")) { "$env:WINDIR\System32\OpenSSH\ssh.exe" } else { 'ssh' }

function Invoke-Shapes([string] $Directory, [string] $Mode) {
    if ($Remote) {
        $command = "$Prefix $Directory/Archevolt.EachShapes --mode $Mode --rows $Rows".Trim()
        return & $Ssh -o BatchMode=yes $Remote $command
    }
    $name = if ($IsWindows) { 'Archevolt.EachShapes.exe' } else { 'Archevolt.EachShapes' }
    return & (Join-Path (Resolve-Path -LiteralPath $Directory).ProviderPath $name) --mode $Mode --rows $Rows
}

function Get-Median([double[]] $Values) {
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { return $sorted[$middle] }
    return ($sorted[$middle - 1] + $sorted[$middle]) / 2
}

@("UTC=$([DateTime]::UtcNow.ToString('O'))", "Baseline=$Baseline", "Candidate=$Candidate", "Launches=$Launches",
    "Modes=$($Modes -join ',')", "Rows=$Rows", "Remote=$Remote", "Prefix=$Prefix") |
    Set-Content -LiteralPath (Join-Path $Output 'environment.txt')

$samples = [Collections.Generic.List[object]]::new()
foreach ($mode in $Modes) {
    for ($launch = 1; $launch -le $Launches; $launch++) {
        $order = @($Sides.Keys)
        if ($launch % 2 -eq 0) { [Array]::Reverse($order) }
        foreach ($side in $order) {
            Write-Host "Measuring $side, $mode, launch $launch of $Launches"
            $lines = @(Invoke-Shapes $Sides[$side] $mode)
            $log = Join-Path $Output "$side-$mode-$launch.csv"
            $lines | Set-Content -LiteralPath $log
            if ($LASTEXITCODE -ne 0 -or $lines -notcontains '# zero_allocations=true; validation=ok') {
                throw "Run failed or did not pass validation and the allocation check; see $log"
            }
            foreach ($row in $lines | Where-Object { $_ -and -not $_.StartsWith('#') } | ConvertFrom-Csv) {
                $samples.Add([pscustomobject]@{
                    side = $side; launch = $launch; case = $row.case; mode = $row.mode; runtime = $row.runtime
                    median_ns_per_row = [double]::Parse($row.median_ns_per_row, $Invariant)
                })
            }
        }
    }
}
$samples | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Output 'samples.csv')

$summary = foreach ($group in $samples | Group-Object case, mode) {
    $first = $group.Group[0]
    $a = @($group.Group | Where-Object side -EQ 'baseline' | ForEach-Object median_ns_per_row)
    $b = @($group.Group | Where-Object side -EQ 'candidate' | ForEach-Object median_ns_per_row)
    $aMin, $aMax = ($a | Measure-Object -Minimum -Maximum).Minimum, ($a | Measure-Object -Minimum -Maximum).Maximum
    $bMin, $bMax = ($b | Measure-Object -Minimum -Maximum).Minimum, ($b | Measure-Object -Minimum -Maximum).Maximum
    $aMedian = Get-Median $a
    $bMedian = Get-Median $b
    [pscustomobject]@{
        case = $first.case; mode = $first.mode; runtime = $first.runtime
        baseline = $aMedian; baseline_min = $aMin; baseline_max = $aMax
        candidate = $bMedian; candidate_min = $bMin; candidate_max = $bMax
        ratio = $bMedian / $aMedian
        ranges = if ($bMax -lt $aMin) { 'faster' } elseif ($bMin -gt $aMax) { 'slower' } else { 'overlap' }
    }
}
$summary | Export-Csv -NoTypeInformation -LiteralPath (Join-Path $Output 'summary.csv')

$format = { param($value) $value.ToString('F3', $Invariant) }
$table = @('| Case | Mode | Baseline ns/row | Candidate ns/row | Ratio | Ranges |', '| --- | --- | --- | --- | ---: | --- |')
foreach ($row in $summary) {
    $table += "| $($row.case) | $($row.mode) | $(& $format $row.baseline) [$(& $format $row.baseline_min), $(& $format $row.baseline_max)] | " +
        "$(& $format $row.candidate) [$(& $format $row.candidate_min), $(& $format $row.candidate_max)] | $($row.ratio.ToString('F2', $Invariant)) | $($row.ranges) |"
}
$table | Set-Content -LiteralPath (Join-Path $Output 'summary.md')
$table | Write-Host
