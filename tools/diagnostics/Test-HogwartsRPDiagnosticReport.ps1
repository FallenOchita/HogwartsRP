[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$helperPath = Join-Path $PSScriptRoot "Get-HogwartsRPDiagnosticReport.ps1"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("HogwartsRP-DiagnosticTests-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw "FAIL: $Message"
    }
}

try {
    $logPath = Join-Path $tempRoot "UE4SS.log"
    $outputPath = Join-Path $tempRoot "sanitized-report.txt"

    @(
        "Unrelated engine log content must not appear."
        "[HogwartsRPDiagnostics] Loaded successfully."
        "[HogwartsRPDiagnostics] UE4SS version: 3.0.1 C:\Users\Example\Private"
        "[HogwartsRPDiagnostics] arbitrary diagnostic payload: C:\Users\Example\Secret"
        "[HogwartsRPDiagnostics] Smoke test complete."
    ) | Set-Content -LiteralPath $logPath

    $passOutput = @(& $helperPath -LogPath $logPath -OutputPath $outputPath) -join "`n"
    Assert-True ($passOutput.Contains("Result: PASS")) "Expected a PASS for both recognized markers."
    Assert-True ($passOutput.Contains("UE4SS version: 3.0.1")) "Expected the safe UE4SS version token."
    Assert-True (-not $passOutput.Contains("Private")) "The version suffix must be excluded."
    Assert-True (-not $passOutput.Contains("Secret")) "Unknown tagged log payload must be excluded."
    Assert-True (-not $passOutput.Contains("Unrelated engine log content")) "Unrelated log content must be excluded."
    Assert-True (Test-Path -LiteralPath $outputPath -PathType Leaf) "Expected the optional report file."
    $savedReport = Get-Content -LiteralPath $outputPath -Raw
    Assert-True (-not $savedReport.Contains($tempRoot)) "The saved report must not contain the temporary log path."

    @("[HogwartsRPDiagnostics] Loaded successfully.") | Set-Content -LiteralPath $logPath
    $partialOutput = @(& $helperPath -LogPath $logPath) -join "`n"
    Assert-True ($partialOutput.Contains("Result: PARTIAL")) "A load-only log must report PARTIAL."

    @("No HogwartsRP diagnostic output here.") | Set-Content -LiteralPath $logPath
    $missingOutput = @(& $helperPath -LogPath $logPath) -join "`n"
    Assert-True ($missingOutput.Contains("Result: NOT VERIFIED")) "A log without markers must report NOT VERIFIED."

    $missingPathRejected = $false
    try {
        & $helperPath -LogPath (Join-Path $tempRoot "missing.log") | Out-Null
    }
    catch {
        $missingPathRejected = $true
    }
    Assert-True $missingPathRejected "A missing log path must fail clearly."

    Write-Output "PASS: UE4SS diagnostic report tests completed."
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
