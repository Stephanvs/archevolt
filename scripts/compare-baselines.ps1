#Requires -Version 7.2
<#
.SYNOPSIS
Compares two scripts/baseline.ps1 result directories case by case.

.DESCRIPTION
Joins the summary.csv of a before run and an after run on harness and case. For each case it
reports both medians and ranges, the after/before ratio, and a verdict. "faster" and "slower"
mean the after median lies outside the before range. "within" means it lies inside it, which
the measurement rules treat as no change. Cases present on one side only are listed as such.

Writes comparison.md next to the after run's summary and prints it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Before,
    [Parameter(Mandatory)] [string] $After
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::InvariantCulture

function Read-Summary([string] $Directory) {
    $rows = @{}
    foreach ($row in Import-Csv -LiteralPath (Join-Path $Directory 'summary.csv')) {
        $rows["$($row.Harness)|$($row.Case)"] = $row
    }
    $rows
}

$old = Read-Summary $Before
$new = Read-Summary $After
$lines = @("Before: $Before", '', "After: $After", '',
    '| Harness | Case | Before µs [range] | After µs [range] | After/before | Verdict |',
    '| --- | --- | ---: | ---: | ---: | --- |')
foreach ($key in @($old.Keys + $new.Keys | Sort-Object -Unique)) {
    $b = $old[$key]
    $a = $new[$key]
    $harness, $case = $key -split '\|', 2
    if (-not $b -or -not $a) {
        $only = if ($b) { 'before only' } else { 'after only' }
        $lines += "| $harness | ``$case`` | | | | $only |"
        continue
    }
    $ratio = [double]$a.MedianUs / [double]$b.MedianUs
    $verdict = if ([double]$a.MedianUs -lt [double]$b.MinUs) { 'faster' }
        elseif ([double]$a.MedianUs -gt [double]$b.MaxUs) { 'slower' }
        else { 'within' }
    $lines += [string]::Format('| {0} | `{1}` | {2:G4} [{3:G4}–{4:G4}] | {5:G4} [{6:G4}–{7:G4}] | {8:F2} | {9} |',
        $harness, $case, [double]$b.MedianUs, [double]$b.MinUs, [double]$b.MaxUs,
        [double]$a.MedianUs, [double]$a.MinUs, [double]$a.MaxUs, $ratio, $verdict)
}
$lines | Set-Content -LiteralPath (Join-Path $After 'comparison.md')
$lines
