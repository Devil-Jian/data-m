#requires -Version 5.1
<#
.SYNOPSIS
  Creates disposable Windows Explorer drag/drop fixtures and verifies their integrity.
.DESCRIPTION
  Manual mode: launch the unsigned Data-M P1 Windows build separately; drag the
  printed file and folder into the Tauri window, inspect the read-only preview,
  then press Enter. Checks file SHA-256, length, modification time and folder
  contents. Does NOT prove UI behavior automatically.
  -SelfTest validates the fixture checker in CI without a native GUI drag/drop.
#>
[CmdletBinding()]
param(
    [switch]$SelfTest
)
$ErrorActionPreference = 'Stop'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("data-m-p1-drop-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
try {
    $file = Join-Path $fixtureRoot 'source_中文 空格.tif'
    $folder = Join-Path $fixtureRoot 'folder_中文 空格'
    New-Item -ItemType Directory -Path $folder | Out-Null
    $content = New-Object byte[] 2048
    for ($i = 0; $i -lt $content.Length; $i++) {
        $content[$i] = [byte]($i % 256)
    }
    [System.IO.File]::WriteAllBytes($file, $content)

    $before = Get-Item -LiteralPath $file
    $originalHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    $originalSize = $before.Length
    $originalTimestamp = $before.LastWriteTimeUtc.Ticks
    $originalFolderCount = @(Get-ChildItem -LiteralPath $folder -Force).Count

    if (-not $SelfTest) {
        Write-Host "在另一窗口启动 Data-M P1 Windows 客户端（非正式版）。"
        Write-Host "将以下文件和目录从 Windows Explorer 拖入 Data-M：" -ForegroundColor Cyan
        Write-Host "文件：$file"
        Write-Host "目录：$folder"
        Write-Host "验证界面只显示文件预览，不提供真实导入操作。"
        [void](Read-Host '完成拖入、预览与关闭操作后按 Enter 进行原文件校验')
    } else {
        Write-Host 'CI SelfTest: only fixture integrity checker is tested; native GUI drag/drop is NOT exercised.'
    }

    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw '原文件在测试后丢失'
    }
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
        throw '原目录在测试后丢失'
    }
    $after = Get-Item -LiteralPath $file
    $afterHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    $afterFolderCount = @(Get-ChildItem -LiteralPath $folder -Force).Count

    if ($afterHash -ne $originalHash) { throw 'SHA-256 检查失败：原文件内容被修改' }
    if ($after.Length -ne $originalSize) { throw '文件大小发生变化' }
    if ($after.LastWriteTimeUtc.Ticks -ne $originalTimestamp) { throw '原文件修改时间发生变化' }
    if ($afterFolderCount -ne $originalFolderCount) { throw '原目录内容发生变化' }

    Write-Host 'PASS: 原文件 SHA-256、长度、修改时间及目录内容均未改变。' -ForegroundColor Green
    if ($SelfTest) {
        Write-Host 'NOTE: 自检不能替代 Windows WebView2 原生 GUI 拖入验收。'
    }
} finally {
    # Only our newly created disposable test directory is removed.
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
