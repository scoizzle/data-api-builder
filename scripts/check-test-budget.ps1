<#
.SYNOPSIS
    Fails when a TRX test run skipped more tests, or executed fewer tests, than a checked-in budget allows.

.DESCRIPTION
    A live-database suite that silently skips its tests still reports green. This script reads a
    .trx file, counts results by outcome and compares them with a budget file:

      * executed = results whose outcome is Passed or Failed
      * skipped  = results whose outcome is NotExecuted (MSTest [Ignore] and Assert.Inconclusive)

    The run fails (exit 1) when skipped > maxSkipped or executed < minExecuted. The skipped test
    names are printed, and optionally written to -SkippedListOut, so the skip list can be published
    as a pipeline artifact. Budgets are ratcheted: lower maxSkipped / raise minExecuted as skips are
    fixed, never the reverse.

.PARAMETER Trx
    Path to the .trx file produced by `dotnet test --logger trx`.

.PARAMETER Budget
    Path to a JSON file with numeric `maxSkipped` and `minExecuted` properties.

.PARAMETER SkippedListOut
    Optional path to write the skipped test names (one per line).

.EXAMPLE
    ./check-test-budget.ps1 -Trx oracle.trx -Budget tests/oracle-test-budget.json -SkippedListOut skipped.txt
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Trx,
    [Parameter(Mandatory = $true)] [string] $Budget,
    [string] $SkippedListOut
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Trx)) { throw "TRX file not found: $Trx" }
if (-not (Test-Path -LiteralPath $Budget)) { throw "Budget file not found: $Budget" }

$limits = Get-Content -LiteralPath $Budget -Raw | ConvertFrom-Json
if ($null -eq $limits.maxSkipped -or $null -eq $limits.minExecuted) {
    throw "Budget file must define numeric 'maxSkipped' and 'minExecuted'."
}

[xml] $doc = Get-Content -LiteralPath $Trx -Raw
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')

$results = $doc.SelectNodes('//t:UnitTestResult', $ns)
$executed = 0
$skipped = New-Object System.Collections.Generic.List[string]
foreach ($result in $results) {
    switch ($result.GetAttribute('outcome')) {
        { $_ -in 'Passed', 'Failed' } { $executed++ }
        'NotExecuted' { $skipped.Add($result.GetAttribute('testName')) }
    }
}

Write-Host "Executed: $executed (min $($limits.minExecuted)); Skipped: $($skipped.Count) (max $($limits.maxSkipped))"

if ($SkippedListOut) {
    $skipped | Sort-Object | Set-Content -LiteralPath $SkippedListOut
}

$failures = @()
if ($skipped.Count -gt [int] $limits.maxSkipped) {
    $failures += "Skipped $($skipped.Count) tests, budget allows $($limits.maxSkipped)."
}
if ($executed -lt [int] $limits.minExecuted) {
    $failures += "Executed $executed tests, budget requires at least $($limits.minExecuted)."
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "##vso[task.logissue type=error]$_" }
    $skipped | Sort-Object | Select-Object -First 50 | ForEach-Object { Write-Host "  skipped: $_" }
    exit 1
}
