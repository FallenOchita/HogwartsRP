[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$LogPath,

    [string]$GameExecutablePath,

    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

function Get-SafeVersionToken {
    param([string]$Value)

    if ($Value -match '^\s*([A-Za-z0-9][A-Za-z0-9._+-]{0,63})(?:\s|$)') {
        return $Matches[1]
    }

    return "Not detected"
}

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
        $gameVersion = Get-SafeVersionToken $versionInfo.ProductVersion
    }
    elseif (-not [string]::IsNullOrWhiteSpace($versionInfo.FileVersion)) {
        $gameVersion = Get-SafeVersionToken $versionInfo.FileVersion
    }
}

# Parse known diagnostic messages, but never echo arbitrary log lines into the report.
$taggedLines = @(
    $logLines | Where-Object { $_ -match '\[HogwartsRPDiagnostics\]' }
)

$loaded = [bool]($taggedLines | Where-Object {
    $_ -match '\[HogwartsRPDiagnostics\]\s+Loaded successfully\.'
})
$completed = [bool]($taggedLines | Where-Object {
    $_ -match '\[HogwartsRPDiagnostics\]\s+Smoke test complete\.'
})
$unavailable = [bool]($taggedLines | Where-Object {
    $_ -match '\[HogwartsRPDiagnostics\]\s+UE4SS\.GetVersion\(\) is unavailable'
})

$ue4ssVersion = "Not reported by diagnostic mod"
$versionLine = $taggedLines | Where-Object {
    $_ -match '\[HogwartsRPDiagnostics\]\s+UE4SS version:'
} | Select-Object -Last 1

if ($versionLine -match 'UE4SS version:\s*(.+)$') {
    $ue4ssVersion = Get-SafeVersionToken $Matches[1]
}
elseif ($unavailable) {
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
    "HogwartsRP diagnostic log markers:"
)

if ($loaded) {
    $report += "[HogwartsRPDiagnostics] Loaded successfully."
}
if ($versionLine) {
    $report += "[HogwartsRPDiagnostics] UE4SS version: $ue4ssVersion"
}
elseif ($unavailable) {
    $report += "[HogwartsRPDiagnostics] UE4SS.GetVersion() is unavailable in this loader build."
}
if ($completed) {
    $report += "[HogwartsRPDiagnostics] Smoke test complete."
}
if (-not $loaded -and -not $versionLine -and -not $unavailable -and -not $completed) {
    $report += "No recognized HogwartsRP diagnostic markers were found in the last 5000 log lines."
}

$report += @(
    ""
    "Privacy: only recognized diagnostic markers and version tokens are included."
    "This smoke test does not verify player discovery, networking, or multiplayer."
)

$report | ForEach-Object { Write-Output $_ }

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $report | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    Write-Output ""
    Write-Output "Sanitized report saved to the requested output file."
}
