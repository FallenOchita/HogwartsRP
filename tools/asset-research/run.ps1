[CmdletBinding()]
param(
 [ValidateSet("scan","search")][string]$Action="scan",
 [string]$GameRoot="D:\SteamLibrary\steamapps\common\Hogwarts Legacy",
 [string]$Output=".\reports",
 [string]$Query,
 [switch]$NoHash,
 [switch]$IncludeHidden
)
$ErrorActionPreference="Stop"
$Here=Split-Path -Parent $MyInvocation.MyCommand.Path
$Python=Get-Command python -ErrorAction SilentlyContinue
if(-not $Python){throw "Python 3.10+ is required."}
Push-Location $Here
try {
 if($Action -eq "scan"){
  $argsList=@(".\asset_research.py","scan","--game-root",$GameRoot,"--output",$Output,"--logs",".\logs")
  if($NoHash){$argsList+="--no-hash"}
  if($IncludeHidden){$argsList+="--include-hidden"}
 } else {
  if([string]::IsNullOrWhiteSpace($Query)){throw "Search requires -Query."}
  $argsList=@(".\asset_research.py","search",$Query,"--output",$Output)
 }
 & $Python.Source @argsList
 if($LASTEXITCODE -ne 0){throw "Asset research exited with code $LASTEXITCODE"}
} finally {Pop-Location}
