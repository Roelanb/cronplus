Param(
  [string]$GoArch = $env:GOARCH,
  [string]$Version = $env:VERSION,
  [string]$CgoEnabled = $env:CGO_ENABLED
)

$ErrorActionPreference = "Stop"

# Defaults
if (-not $GoArch) { $GoArch = "amd64" }
if (-not $CgoEnabled) { $CgoEnabled = "0" }

# Resolve repo root (scripts directory's parent)
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
Set-Location $RootDir

$AppName = "cronplusd"
$Pkg = "./cmd/cronplusd"
$GoOs = "windows"

# Compute version string
if (-not $Version) {
  try {
    $gitDir = & git -C $RootDir rev-parse --git-dir 2>$null
    if ($LASTEXITCODE -eq 0) {
      $desc = (& git -C $RootDir describe --tags --always --dirty 2>$null)
      if ($LASTEXITCODE -eq 0 -and $desc) {
        $Version = $desc.Trim()
      } else {
        $sha = (& git -C $RootDir rev-parse --short HEAD 2>$null)
        if ($LASTEXITCODE -eq 0 -and $sha) {
          $Version = $sha.Trim()
        } else {
          $Version = "dev"
        }
      }
    } else {
      $Version = "dev"
    }
  } catch {
    $Version = "dev"
  }
}

# Ensure dist folder exists
$dist = Join-Path $RootDir "dist"
if (-not (Test-Path $dist)) {
  New-Item -ItemType Directory -Path $dist | Out-Null
}

# Linker flags (strip debug, trim paths)
$ldflags = "-s -w"
# If you add a version variable in code (e.g., package main: var version = "" ),
# you can pass it like:
# $ldflags = "$ldflags -X 'main.version=$Version'"

$outPath = Join-Path $dist "$AppName-windows-$GoArch.exe"

Write-Host "Building $outPath (GOOS=$GoOs GOARCH=$GoArch CGO_ENABLED=$CgoEnabled) VERSION=$Version"

$env:GOOS = $GoOs
$env:GOARCH = $GoArch
$env:CGO_ENABLED = $CgoEnabled

# Run build
& go build -o $outPath -trimpath -ldflags $ldflags $Pkg

if ($LASTEXITCODE -ne 0) {
  throw "go build failed"
}

Write-Host "Built $outPath"
