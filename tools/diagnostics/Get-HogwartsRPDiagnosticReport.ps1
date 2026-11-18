[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$LogPath,

    [string]$GameExecutablePath,

    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
    throw "UE4SS log file was not found. Supply the path to UE4SS.log with -LogPath."
}

$resolvedLogPath = (Resolve-Path -LiteralPath $LogPath).Path
$logLines = @(Get-Content -LiteralPath $resolvedLogPath -Tail 5000)

if ([string]::IsNullOrWhiteSpace($GameExecutablePath)) {
    $logDirectory = Split-Path -Parent $resolvedLogPath
    $GameExecutablePath = Join-Path $logDirectory "HogwartsLegacy.exe"
}

$gameVersion = "Not detected"
if (Test-Path -LiteralPath $GameExecutablePath -PathType Leaf) {
    $versionInfo = (Get-Item -LiteralPath $GameExecutablePath).VersionInfo
    if (-not [string]::IsNullOrWhiteSpace($versionInfo.ProductVersion)) {
        $gameVersion = $versionInfo.ProductVersion
    }
    elseif (-not [string]::IsNullOrWhiteSpace($versionInfo.FileVersion)) {
        $gameVersion = $versionInfo.FileVersion
    }
}

$diagnosticLines = @(
    $logLines |
        Where-Object { $_ -match '\[HogwartsRPDiagnostics\]' } |
        Select-Object -Last 25
)

$loaded = [bool]($diagnosticLines | Where-Object {
    $_ -match '\[HogwartsRPDiagnostics\]\s+Loaded successfully\.'
})
$completed = [bool]($diagnosticLines | Where-Object {
    $_ -match '\[HogwartsRPDiagnostics\]\s+Smoke test complete\.'
})

$ue4ssVersion = "Not reported by diagnostic mod"
$versionLine = $diagnosticLines | Where-Object {
    $_ -match '\[HogwartsRPDiagnostics\]\s+UE4SS version:'
} | Select-Object -Last 1

if ($versionLine -match 'UE4SS version:\s*(.+)$') {
    $ue4ssVersion = $Matches[1].Trim()
}
elseif ($diagnosticLines -match 'UE4SS.GetVersion\(\) is unavailable') {
    $ue4ssVersion = "GetVersion API unavailable in this loader build"
}

$result = "NOT VERIFIED — diagnostic markers were not both found"
if ($loaded -and $completed) {
    $result = "PASS — diagnostic mod loaded and completed its smoke test"
}
elseif ($loaded) {
    $result = "PARTIAL — load marker found, completion marker missing"
}

$report = @(
    "HogwartsRP UE4SS diagnostic report"
    "Result: $result"
    "Hogwarts Legacy executable version: $gameVersion"
    "UE4SS version: $ue4ssVersion"
    ""
    "HogwartsRP diagnostic log lines:"
)

if ($diagnosticLines.Count -gt 0) {
    $report += $diagnosticLines
}
else {
    $report += "No [HogwartsRPDiagnostics] lines were found in the last 5000 log lines."
}

$report += @(
    ""
    "Privacy: this report omits the log file path and unrelated log content."
    "This smoke test does not verify player discovery, networking, or multiplayer."
)

$report | ForEach-Object { Write-Output $_ }

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $report | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    Write-Output ""
    Write-Output "Sanitized report saved to the requested output file."
}
