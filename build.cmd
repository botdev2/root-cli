@echo off
setlocal
pushd "%~dp0"
dotnet build RootCli.sln -c Release
if errorlevel 1 (
  popd
  exit /b 1
)
echo.
echo Built: src\RootCli\bin\Release\net8.0\RootCli.exe
popd
