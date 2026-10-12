<#
.SYNOPSIS
  Instala el plugin Excavaciones en Revit 2027 (usuario actual).
.DESCRIPTION
  Copia Excavaciones.dll y Excavaciones.addin a %APPDATA%\Autodesk\Revit\Addins\2027.
  Si se ejecuta desde el repositorio y hay .NET SDK 10, compila antes de copiar (usa -SkipBuild para omitir).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build\install.ps1
#>
param(
    [string]$RevitVersion = "2027",
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $root "Excavaciones.addin"))) { $root = $PSScriptRoot }  # paquete ya compilado

$dll = $null
if (-not $SkipBuild -and (Test-Path (Join-Path $root "src\Excavaciones\Excavaciones.csproj"))) {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        Write-Host "Compilando..." -ForegroundColor Cyan
        dotnet build (Join-Path $root "src\Excavaciones\Excavaciones.csproj") -c Release
        if ($LASTEXITCODE -ne 0) { throw "La compilación falló." }
        $dll = Join-Path $root "build\bin\Release\Excavaciones.dll"
    } else {
        Write-Warning "No se encontró 'dotnet'. Instala el SDK de .NET 10 (https://dotnet.microsoft.com/download/dotnet/10.0) o usa el paquete compilado."
    }
}
if (-not $dll) {
    foreach ($candidate in @("build\bin\Release\Excavaciones.dll", "Excavaciones\Excavaciones.dll", "Excavaciones.dll")) {
        $p = Join-Path $root $candidate
        if (Test-Path $p) { $dll = $p; break }
    }
}
if (-not $dll) { throw "No se encontró Excavaciones.dll. Compila el proyecto o descarga el paquete del workflow de GitHub." }

$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$target = Join-Path $addins "Excavaciones"
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item $dll $target -Force
Copy-Item (Join-Path $root "Excavaciones.addin") $addins -Force
Write-Host "Instalado en $addins" -ForegroundColor Green
Write-Host "Abre Revit $RevitVersion: aparecerá la pestaña 'Excavaciones'."
