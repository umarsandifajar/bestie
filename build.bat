@echo off
setlocal
set VER=1.1.0
echo Build bestie v%VER% ...
if exist dist rmdir /s /q dist

dotnet publish src\MySecretary\MySecretary.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:Version=%VER% -p:DebugType=None -p:DebugSymbols=false -o dist\standalone || goto :err
dotnet publish src\MySecretary\MySecretary.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:Version=%VER% -p:DebugType=None -p:DebugSymbols=false -o dist\lite || goto :err

copy /y dist\standalone\MySecretary.exe dist\bestie-v%VER%-win-x64.exe >nul
copy /y dist\lite\MySecretary.exe dist\bestie-v%VER%-win-x64-lite.exe >nul

rem --- installer (perlu Inno Setup 6/7: https://jrsoftware.org/isdl.php) ---
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 7\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 7\ISCC.exe"
if exist "%LocalAppData%\Programs\Inno Setup 7\ISCC.exe" set "ISCC=%LocalAppData%\Programs\Inno Setup 7\ISCC.exe"
if defined ISCC (
  "%ISCC%" /DMyAppVersion=%VER% installer\bestie.iss || goto :err
) else (
  echo.
  echo [!] Inno Setup 6 atau 7 belum terpasang, installer dilewati.
  echo     Install dari https://jrsoftware.org/isdl.php lalu jalankan build.bat lagi.
)

echo.
echo Selesai. Hasil di folder dist\
echo   bestie-v%VER%-setup.exe          (INSTALLER, bisa jalan otomatis saat Windows menyala)
echo   bestie-v%VER%-win-x64.exe        (portable, tanpa install)
echo   bestie-v%VER%-win-x64-lite.exe   (portable kecil, butuh .NET 8 Desktop Runtime)
exit /b 0
:err
echo Build gagal.
exit /b 1
