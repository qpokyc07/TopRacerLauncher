# Сборка TopRacerLauncher в один .exe
# Использование:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "src\TopRacerLauncher\TopRacerLauncher.csproj"
$dist = Join-Path $root "dist"

# dotnet SDK: системный dotnet может быть без SDK, поэтому перебираем известные места.
function Get-DotnetWithSdk {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "dotnet8\dotnet.exe"),
        "C:\Temp\opencode\dotnet\dotnet.exe",
        (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
        (Join-Path $env:USERPROFILE ".dotnet\dotnet.exe")
    )
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }

    foreach ($c in $candidates) {
        if (-not $c -or -not (Test-Path $c)) { continue }
        $sdks = & $c --list-sdks 2>$null
        if ($sdks -and (($sdks | Out-String) -match "\d+\.\d+\.\d+")) { return $c }
    }
    return $null
}

$dotnetExe = Get-DotnetWithSdk
if (-not $dotnetExe) {
    throw ("dotnet SDK не найден. Установите .NET 8 SDK:" + [Environment]::NewLine +
        "  Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1" + [Environment]::NewLine +
        "  .\dotnet-install.ps1 -Channel 8.0 -InstallDir `$env:LOCALAPPDATA\dotnet8")
}
Write-Host "dotnet: $dotnetExe" -ForegroundColor DarkGray

# 1) Иконка (нужна только если Assets\app.ico удалён)
$ico = Join-Path $root "src\TopRacerLauncher\Assets\app.ico"
if (-not (Test-Path $ico)) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "tools\gen_icon.ps1")
}

# 2) Publish: один самодостаточный exe, без консольного окна и без .pdb в поставке
Remove-Item (Join-Path $dist "TopRacerLauncher.pdb") -Force -ErrorAction SilentlyContinue
& $dotnetExe publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $dist -v minimal
if ($LASTEXITCODE -ne 0) { throw "Ошибка сборки (код $LASTEXITCODE)" }

$exe = Join-Path $dist "TopRacerLauncher.exe"
Write-Host ""
Write-Host "Готово: $exe" -ForegroundColor Green
Write-Host ("Размер: {0:N1} MB" -f ((Get-Item $exe).Length / 1MB))

# 2.5) Рабочая копия в обычной папке. Внутри папки проекта (где лежит этот
# скрипт) у Windows стоит ограничение на права, из-за которого single-file exe
# не распаковывается при запуске — поэтому готовый файл кладём ещё и сюда.
$runDir = "C:\TopRacerLauncher"
try {
    New-Item -ItemType Directory -Path $runDir -Force | Out-Null
    Copy-Item $exe (Join-Path $runDir "TopRacerLauncher.exe") -Force
    Write-Host ("Запускать отсюда: {0}" -f (Join-Path $runDir "TopRacerLauncher.exe")) -ForegroundColor DarkCyan
} catch { }

# 3) Самопроверка: стартует, прогоняет логические проверки и выходит с кодом 0
$smokeDir = Join-Path $env:TEMP "TopRacerLauncherSmoke"
Remove-Item $smokeDir -Recurse -Force -ErrorAction SilentlyContinue

# Запускаем копию из TEMP: антивирусы и политики папок иногда блокируют
# single-file .exe, лежащий в папке проекта (проверяются при этом те же байты).
$smokeExe = Join-Path $env:TEMP "TopRacerLauncher-smoke\TopRacerLauncher.exe"
New-Item -ItemType Directory -Path (Split-Path $smokeExe -Parent) -Force | Out-Null
Copy-Item $exe $smokeExe -Force

$p = Start-Process -FilePath $smokeExe -ArgumentList "--smoke-test" -PassThru
if (-not $p.WaitForExit(60000)) {
    $p.Kill()
    throw "Самопроверка: таймаут (окно не закрылось за 60 с)"
}

$okMarker = Join-Path $smokeDir "smoke-ok.txt"
$failMarker = Join-Path $smokeDir "smoke-fail.txt"

if ($p.ExitCode -eq 0 -and (Test-Path $okMarker)) {
    Write-Host "Самопроверка: OK" -ForegroundColor Green
    Get-Content $okMarker -Encoding UTF8 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
} else {
    Write-Host "Самопроверка: ПРОВАЛ (код $($p.ExitCode))" -ForegroundColor Red
    if (Test-Path $failMarker) { Get-Content $failMarker -Encoding UTF8 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red } }
    exit 1
}
