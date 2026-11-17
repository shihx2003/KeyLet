<#
.SYNOPSIS
    KeyLet 构建、测试与单文件发布脚本。
.EXAMPLE
    .\build.ps1 -Task Test
    .\build.ps1 -Task Publish -Runtime win-x64 -SelfContained $true
#>
[CmdletBinding()]
param(
    [ValidateSet("Build", "Test", "Publish", "Clean")]
    [string]$Task = "Build",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("win-x64", "win-x86")]
    [string]$Runtime = "win-x64",

    [bool]$SelfContained = $true,

    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$SlnPath = Join-Path $RepoRoot "KeyLet.sln"
$AppProjPath = Join-Path $RepoRoot "src\KeyLet.App\KeyLet.App.csproj"
$DistDir = Join-Path $RepoRoot "dist"

function Resolve-DotNet10Sdk {
    $candidates = @(
        "dotnet",
        "C:\Program Files\dotnet\dotnet.exe",
        "C:\Program Files (x86)\dotnet\dotnet.exe"
    )

    foreach ($cmd in $candidates) {
        try {
            $sdks = & $cmd --list-sdks 2>$null
            if ($LASTEXITCODE -eq 0 -and ($sdks | Where-Object { $_ -match "^10\." })) {
                return $cmd
            }
        }
        catch {
            continue
        }
    }

    throw "未找到 .NET 10 SDK，请先安装 .NET 10 SDK（x64 或 x86）。"
}

$DotNet = Resolve-DotNet10Sdk
Write-Host "[KeyLet] 使用 dotnet: $DotNet" -ForegroundColor Cyan

switch ($Task) {
    "Clean" {
        if (Test-Path $DistDir) {
            Remove-Item $DistDir -Recurse -Force
        }
        if (Test-Path $SlnPath) {
            & $DotNet clean $SlnPath -c $Configuration
        }
        Write-Host "[KeyLet] 清理完成。" -ForegroundColor Green
    }

    "Build" {
        if (-not (Test-Path $SlnPath)) {
            throw "尚未找到 $SlnPath，请先初始化 src/ 下的项目。"
        }
        $extraArgs = if ($NoRestore) { @("--no-restore", "/p:UseSharedCompilation=false", "-m:1") } else { @() }
        & $DotNet build $SlnPath -c $Configuration @extraArgs
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    "Test" {
        if (-not (Test-Path $SlnPath)) {
            throw "尚未找到 $SlnPath，请先初始化 src/ 下的项目。"
        }
        $extraArgs = if ($NoRestore) { @("--no-restore", "/p:UseSharedCompilation=false", "-m:1") } else { @() }
        & $DotNet test $SlnPath -c $Configuration @extraArgs
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    "Publish" {
        if (-not (Test-Path $AppProjPath)) {
            throw "尚未找到 $AppProjPath，请先初始化 KeyLet.App 项目。"
        }
        New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

        $selfContainedFlag = if ($SelfContained) { "true" } else { "false" }
        $extraArgs = if ($NoRestore) { @("--no-restore", "/p:UseSharedCompilation=false", "-m:1") } else { @() }
        & $DotNet publish $AppProjPath `
            -c $Configuration `
            -r $Runtime `
            --self-contained $selfContainedFlag `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true `
            -p:DebugType=none `
            -p:DebugSymbols=false `
            @extraArgs `
            -o $DistDir

        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Get-ChildItem -Path $DistDir -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item -Force
        Write-Host "[KeyLet] 单文件发布完成，输出目录: $DistDir" -ForegroundColor Green
    }
}
