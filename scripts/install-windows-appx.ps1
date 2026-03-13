[CmdletBinding()]
param(
    [string]$SearchRoot = "."
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path $SearchRoot
$allAppx = Get-ChildItem -Path $root -Recurse -File -Filter *.appx
$main = $allAppx |
    Where-Object { $_.FullName -notlike "*\Dependencies\*" -and $_.FullName -notlike "*/Dependencies/*" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $main) {
    throw "No primary .appx package found under $root."
}

$deps = $allAppx |
    Where-Object { $_.FullName -like "*\Dependencies\*" -or $_.FullName -like "*/Dependencies/*" } |
    Select-Object -ExpandProperty FullName

Write-Host "Installing package: $($main.FullName)"
if ($deps -and $deps.Count -gt 0) {
    Add-AppxPackage -Path $main.FullName -DependencyPath $deps -ForceApplicationShutdown -ForceUpdateFromAnyVersion
}
else {
    Add-AppxPackage -Path $main.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion
}

Write-Host "Install completed."
Write-Host "If launch fails, verify Windows 'Developer Mode' is enabled for unsigned app packages."
