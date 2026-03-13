[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x86", "x64", "ARM64")]
    [string]$Platform = "x64",

    [string]$OutputDir = "dist/windows-demo-build",
    [string]$ZipPath = "dist/windows-demo-build.zip",

    [switch]$SkipTests,
    [switch]$SkipUwpBuild,
    [switch]$ForceUwpBuild,
    [switch]$PackageForSideload
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$enableSideloadPackage = $PackageForSideload.IsPresent
if (-not $PSBoundParameters.ContainsKey("PackageForSideload")) {
    $enableSideloadPackage = $true
}

function Write-Section {
    param([Parameter(Mandatory = $true)][string]$Title)
    Write-Host ""
    Write-Host "== $Title ==" -ForegroundColor Cyan
}

function Require-Command {
    param([Parameter(Mandatory = $true)][string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Resolve-RepoPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $PathValue))
}

function Copy-DirectoryContentIfExists {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$TargetDir
    )

    if (-not (Test-Path $SourceDir)) {
        return
    }

    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    Copy-Item -Path (Join-Path $SourceDir "*") -Destination $TargetDir -Recurse -Force
}

function Write-AppxInstallerScript {
    param([Parameter(Mandatory = $true)][string]$Path)

    $content = @'
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
'@

    Set-Content -Path $Path -Value $content -Encoding UTF8
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")

$windowsRoot = Join-Path $repoRoot "windows"
$protocolTestsProject = Join-Path $windowsRoot "WiFiDirectDemo.Protocol.Tests/WiFiDirectDemo.Protocol.Tests.csproj"
$uwpProject = Join-Path $windowsRoot "WiFiDirectDemo.Windows/WiFiDirectDemo.Windows.csproj"
$protocolOutputRoot = Join-Path $windowsRoot "WiFiDirectDemo.Protocol/bin/$Configuration"
$protocolTestsOutputRoot = Join-Path $windowsRoot "WiFiDirectDemo.Protocol.Tests/bin/$Configuration"
$uwpOutputRoot = Join-Path $windowsRoot "WiFiDirectDemo.Windows/bin/$Platform/$Configuration"
$appxOutputRoot = Join-Path $repoRoot "artifacts/windows-appx"
$installerTemplatePath = Join-Path $scriptRoot "install-windows-appx.ps1"

$outputDirFull = Resolve-RepoPath -RepoRoot $repoRoot -PathValue $OutputDir
$zipPathFull = Resolve-RepoPath -RepoRoot $repoRoot -PathValue $ZipPath

if (-not (Test-Path $protocolTestsProject)) {
    throw "Project not found: $protocolTestsProject"
}
if (-not (Test-Path $uwpProject)) {
    throw "Project not found: $uwpProject"
}

Require-Command -Name "dotnet"

Push-Location $repoRoot
try {
    Write-Section "Restore/Test"
    if ($SkipTests) {
        Write-Host "Skipping protocol tests."
    }
    else {
        & dotnet restore $protocolTestsProject
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed."
        }

        & dotnet test $protocolTestsProject --configuration $Configuration --no-restore
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet test failed."
        }
    }

    Write-Section "UAP Detection"
    $uapRoot = Join-Path ${env:ProgramFiles(x86)} "Reference Assemblies\Microsoft\Framework\UAP"
    $windowsSdkReferencesRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\References"
    $uapAvailable = $false
    $targetFramework = $null
    $targetPlatformVersion = $null
    $uapDetectionSource = $null

    if (Test-Path $uapRoot) {
        $versions = @(Get-ChildItem $uapRoot -Directory |
            Where-Object { $_.Name -match '^v10\.0\.\d+(\.\d+)?$' } |
            Sort-Object { [version]($_.Name.TrimStart('v')) } -Descending)

        if ($versions.Count -gt 0) {
            $selected = $versions[0].Name.TrimStart('v')
            $parts = $selected.Split('.')
            if ($parts.Count -ge 3) {
                $build = $parts[2]
                $targetFramework = "uap10.0.$build"
                $targetPlatformVersion = if ($parts.Count -ge 4) { $selected } else { "$selected.0" }
                $uapAvailable = $true
                $uapDetectionSource = $uapRoot
            }
        }
    }

    if (-not $uapAvailable -and (Test-Path $windowsSdkReferencesRoot)) {
        $sdkVersions = @(Get-ChildItem $windowsSdkReferencesRoot -Directory |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Sort-Object { [version]$_.Name } -Descending)

        if ($sdkVersions.Count -gt 0) {
            $selected = $sdkVersions[0].Name
            $parts = $selected.Split('.')
            if ($parts.Count -ge 3) {
                $build = $parts[2]
                $targetFramework = "uap10.0.$build"
                $targetPlatformVersion = $selected
                $uapAvailable = $true
                $uapDetectionSource = $windowsSdkReferencesRoot
            }
        }
    }

    if ($uapAvailable) {
        Write-Host "Detected UAP SDK: $targetFramework ($targetPlatformVersion) from $uapDetectionSource"
    }
    else {
        Write-Host "UAP SDK not available on this machine. Checked: $uapRoot and $windowsSdkReferencesRoot"
    }

    $uwpBuilt = $false
    $skipReason = ""

    Write-Section "Build UWP"
    if ($SkipUwpBuild) {
        $skipReason = "skip requested by -SkipUwpBuild."
        Write-Host "Skipping UWP build: $skipReason"
    }
    elseif (-not $uapAvailable -and -not $ForceUwpBuild) {
        if ($enableSideloadPackage) {
            throw "UWP SDK references are not available. Checked '$uapRoot' and '$windowsSdkReferencesRoot'. Install Visual Studio 2022 Build Tools with UWP workload, or run with -SkipUwpBuild for protocol-only artifacts."
        }

        $skipReason = "UWP SDK references are not available."
        Write-Host "Skipping UWP build: $skipReason"
    }
    else {
        if (-not (Get-Command msbuild -ErrorAction SilentlyContinue)) {
            throw "msbuild was not found in PATH. Install Visual Studio 2022 (or Build Tools) with UWP build components."
        }

        if ($enableSideloadPackage) {
            if (Test-Path $appxOutputRoot) {
                Remove-Item -Path $appxOutputRoot -Recurse -Force
            }
            New-Item -ItemType Directory -Path $appxOutputRoot -Force | Out-Null
        }

        $msbuildArgs = @(
            $uwpProject,
            "/restore",
            "/p:Configuration=$Configuration",
            "/p:Platform=$Platform",
            "/p:AppxBundle=Never"
        )

        if ($enableSideloadPackage) {
            $msbuildArgs += "/p:GenerateAppxPackageOnBuild=true"
            $msbuildArgs += "/p:UapAppxPackageBuildMode=SideloadOnly"
            $msbuildArgs += "/p:AppxPackageSigningEnabled=false"
            $msbuildArgs += "/p:AppxPackageDir=$appxOutputRoot\\"
        }
        else {
            $msbuildArgs += "/p:UapAppxPackageBuildMode=None"
        }

        if ($uapAvailable) {
            $msbuildArgs += "/p:TargetFramework=$targetFramework"
            $msbuildArgs += "/p:TargetPlatformVersion=$targetPlatformVersion"
            $msbuildArgs += "/p:TargetPlatformMinVersion=$targetPlatformVersion"
        }
        elseif ($ForceUwpBuild) {
            Write-Host "Proceeding without detected UAP SDK because -ForceUwpBuild was specified."
        }

        & msbuild @msbuildArgs
        if ($LASTEXITCODE -ne 0) {
            throw "msbuild failed."
        }

        $uwpBuilt = $true
    }

    Write-Section "Collect Artifacts"
    if (Test-Path $outputDirFull) {
        Remove-Item -Path $outputDirFull -Recurse -Force
    }
    New-Item -ItemType Directory -Path $outputDirFull -Force | Out-Null

    if ($uwpBuilt) {
        Copy-DirectoryContentIfExists -SourceDir $uwpOutputRoot -TargetDir $outputDirFull

        if ($enableSideloadPackage) {
            Copy-DirectoryContentIfExists -SourceDir $appxOutputRoot -TargetDir (Join-Path $outputDirFull "appx")
            $installScriptPath = Join-Path $outputDirFull "install-appx.ps1"
            if (Test-Path $installerTemplatePath) {
                Copy-Item -Path $installerTemplatePath -Destination $installScriptPath -Force
            }
            else {
                Write-AppxInstallerScript -Path $installScriptPath
            }
        }
    }
    else {
        Copy-DirectoryContentIfExists -SourceDir $protocolOutputRoot -TargetDir (Join-Path $outputDirFull "protocol")
        Copy-DirectoryContentIfExists -SourceDir $protocolTestsOutputRoot -TargetDir (Join-Path $outputDirFull "protocol-tests")
    }

    $statusLines = New-Object System.Collections.Generic.List[string]
    $statusLines.Add(("status={0}" -f ($(if ($uwpBuilt) { "uwp-built" } else { "uwp-skipped" }))))
    $statusLines.Add(("uap_available={0}" -f $uapAvailable.ToString().ToLowerInvariant()))
    $statusLines.Add(("configuration={0}" -f $Configuration))
    $statusLines.Add(("platform={0}" -f $Platform))
    $statusLines.Add(("tests={0}" -f $(if ($SkipTests) { "skipped" } else { "ran" })))
    $statusLines.Add(("package_for_sideload={0}" -f $enableSideloadPackage.ToString().ToLowerInvariant()))
    if ($targetFramework) {
        $statusLines.Add(("target_framework={0}" -f $targetFramework))
    }
    if ($targetPlatformVersion) {
        $statusLines.Add(("target_platform_version={0}" -f $targetPlatformVersion))
    }
    if (-not $uwpBuilt -and $skipReason) {
        $statusLines.Add(("reason={0}" -f $skipReason))
    }

    $statusPath = Join-Path $outputDirFull "build-status.txt"
    $statusLines | Set-Content -Path $statusPath -Encoding UTF8

    Write-Section "Package Zip"
    $zipParent = Split-Path -Parent $zipPathFull
    if ($zipParent) {
        New-Item -ItemType Directory -Path $zipParent -Force | Out-Null
    }
    if (Test-Path $zipPathFull) {
        Remove-Item -Path $zipPathFull -Force
    }
    Compress-Archive -Path (Join-Path $outputDirFull "*") -DestinationPath $zipPathFull -CompressionLevel Optimal

    Write-Section "Done"
    Write-Host "Artifacts directory: $outputDirFull"
    Write-Host "Zip package: $zipPathFull"
}
finally {
    Pop-Location
}
