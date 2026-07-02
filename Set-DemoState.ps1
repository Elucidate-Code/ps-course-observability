<#
.SYNOPSIS
  Moves the OrderService instrumentation between the course's checkpoint states.

.DESCRIPTION
  The source has named [DEMO-BLOCK BEGIN: x] / [DEMO-BLOCK END: x] regions in
  TelemetryConfiguration.cs and OrderProcessingService.cs. Each region belongs to a
  group (tracing / metrics / logging / pii), and each course module cumulatively
  turns one more group on:

    M1  framework auto-instrumentation only (ASP.NET Core, EF Core, HttpClient)
    M2  + tracing   (ActivitySource, custom spans)                 -> after Module 2
    M3  + metrics   (Meter, counters/histogram)                    -> after Module 3
    M4  + logging   (structured logs, business fields)              -> after Module 4
    M5  + pii       (PII redaction processor on traces + logs)      -> after Module 5

  Canonical (fully "on") content for every managed block lives in
  tools/demo-block-manifest.json. Turning a group off comments out its block's
  lines; turning it on rewrites the block from that canonical source. Because
  every run regenerates from the canonical snapshot rather than editing in place,
  it's safe to run repeatedly or jump between states in any order.

  A few blocks are intentionally NOT managed by this script and are left exactly
  as committed: tracing-dbquery-statement and metrics-exemplar-filter (baseline
  framework wiring, always on), pii-status-scan (inert until the pii processor is
  registered), logging-error-baseline / logging-error-with-exception (alternate
  log statement styles kept for reference, not part of the reveal), and
  logging-info-structured (a success-path log line already left disabled in the
  source). The single-line [DEMO-BLOCK: name] markers (extra grandchild spans in
  the helper services) are optional bonus content and are also left untouched.

  To add or rename a managed block, edit tools/Update-DemoBlockManifest.py and
  re-run it against the M5 (fully-on) state to regenerate the manifest.

.PARAMETER State
  M1, M2, M3, M4, or M5.

.PARAMETER Show
  Report each managed block's current on/off status without changing anything.

.EXAMPLE
  ./Set-DemoState.ps1 -State M2

.EXAMPLE
  ./Set-DemoState.ps1 -Show
#>
[CmdletBinding(DefaultParameterSetName = 'SetState')]
param(
    [Parameter(ParameterSetName = 'SetState', Position = 0, Mandatory = $true)]
    [ValidateSet('M1', 'M2', 'M3', 'M4', 'M5')]
    [string]$State,

    [Parameter(ParameterSetName = 'Show', Mandatory = $true)]
    [switch]$Show
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$manifestPath = Join-Path $repoRoot 'tools/demo-block-manifest.json'

if (-not (Test-Path $manifestPath)) {
    throw "Manifest not found at $manifestPath"
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$groupOrder = @('tracing', 'metrics', 'logging', 'pii')

function Get-EnabledGroups([string]$state) {
    if (-not $state) { return @() }
    $count = [int]$state.Substring(1) - 1   # M1 -> 0, M2 -> 1, ... M5 -> 4
    if ($count -le 0) { return @() }
    return $groupOrder[0..($count - 1)]
}

function ConvertTo-CommentedLines($lines) {
    $lines | ForEach-Object {
        if ($_ -match '^\s*$') {
            $_
        }
        elseif ($_ -match '^(\s*)//') {
            $_
        }
        else {
            $_ -replace '^(\s*)', '$1// '
        }
    }
}

$blocksByFile = $manifest.blocks | Group-Object -Property file

if ($Show) {
    foreach ($group in $blocksByFile) {
        $filePath = Join-Path $repoRoot $group.Name
        $lines = Get-Content $filePath
        foreach ($block in $group.Group) {
            $beginIdx = ($lines | Select-String -Pattern "\[DEMO-BLOCK BEGIN:\s*$([regex]::Escape($block.name))\]").LineNumber
            $endIdx = ($lines | Select-String -Pattern "\[DEMO-BLOCK END:\s*$([regex]::Escape($block.name))\]").LineNumber
            if (-not $beginIdx -or -not $endIdx) {
                Write-Warning "$($block.name): markers not found in $($group.Name)"
                continue
            }
            $current = $lines[$beginIdx..($endIdx - 2)]
            $canonicalOn = $block.lines -join "`n"
            $canonicalOff = (ConvertTo-CommentedLines $block.lines) -join "`n"
            $currentJoined = $current -join "`n"
            $status = if ($currentJoined -eq $canonicalOn) { 'ON ' }
                      elseif ($currentJoined -eq $canonicalOff) { 'off' }
                      else { '?? ' }
            "{0}  {1,-38} ({2})" -f $status, $block.name, $block.group
        }
    }
    return
}

$enabledGroups = Get-EnabledGroups $State
Write-Host "Setting demo state to $State - enabled groups: $(if ($enabledGroups) { $enabledGroups -join ', ' } else { '(none - framework baseline only)' })"

foreach ($group in $blocksByFile) {
    $filePath = Join-Path $repoRoot $group.Name
    $lines = [System.Collections.Generic.List[string]](Get-Content $filePath)
    $blockLookup = @{}
    foreach ($block in $group.Group) { $blockLookup[$block.name] = $block }

    $output = New-Object System.Collections.Generic.List[string]
    $i = 0
    $changed = 0
    while ($i -lt $lines.Count) {
        $line = $lines[$i]
        $beginMatch = [regex]::Match($line, '\[DEMO-BLOCK BEGIN:\s*([a-zA-Z0-9_-]+)\]')
        if ($beginMatch.Success -and $blockLookup.ContainsKey($beginMatch.Groups[1].Value)) {
            $name = $beginMatch.Groups[1].Value
            $block = $blockLookup[$name]
            $output.Add($line)
            $j = $i + 1
            while ($j -lt $lines.Count -and $lines[$j] -notmatch "\[DEMO-BLOCK END:\s*$([regex]::Escape($name))\]") { $j++ }
            if ($j -ge $lines.Count) { throw "Unterminated block '$name' in $filePath" }

            $desiredOn = $enabledGroups -contains $block.group
            $newBody = if ($desiredOn) { $block.lines } else { ConvertTo-CommentedLines $block.lines }
            foreach ($bodyLine in $newBody) { $output.Add($bodyLine) }
            $output.Add($lines[$j])
            $changed++
            $i = $j + 1
        }
        else {
            $output.Add($line)
            $i++
        }
    }

    # Write with LF line endings (no BOM) to match the rest of the repo and avoid
    # a whole-file diff every time this script runs.
    $text = ($output -join "`n") + "`n"
    [System.IO.File]::WriteAllText($filePath, $text, [System.Text.UTF8Encoding]::new($false))
    Write-Host "  $($group.Name): $changed block(s) applied"
}

Write-Host "Done. Run 'dotnet build' to confirm, then './Set-DemoState.ps1 -Show' to review."
