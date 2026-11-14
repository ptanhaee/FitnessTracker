@echo off
setlocal
pushd "%~dp0"
set "APPDATA=%CD%\.build-profile"
set "LOCALAPPDATA=%APPDATA%"
set "DOTNET_CLI_HOME=%APPDATA%"
set "NUGET_PACKAGES=%CD%\.packages"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
dotnet build "Fitness Tracker.csproj" --nologo -p:TargetPlatformMoniker=Windows%%2CVersion=7.0 -p:TargetPlatformDisplayName=Windows "-p:RestoreConfigFile=%CD%\NuGet.Config" -p:NuGetAudit=false
if errorlevel 1 goto fail
if /I not "%~1"=="check" goto done
dotnet build Checks\Checks.csproj --nologo -p:TargetPlatformMoniker=Windows%%2CVersion=7.0 -p:TargetPlatformDisplayName=Windows "-p:RestoreConfigFile=%CD%\NuGet.Config" -p:NuGetAudit=false
if errorlevel 1 goto fail
dotnet Checks\bin\Debug\net8.0-windows\Checks.dll
if errorlevel 1 goto fail
:done
popd
exit /b 0
:fail
popd
exit /b 1
