@echo off
chcp 65001 > nul
echo =======================================
echo  ติดตั้ง CargoFit -- ยกเลิก Block ไฟล์
echo =======================================
echo.

set INSTALLER=CargoFit-win-Setup.exe

if not exist "%~dp0%INSTALLER%" (
    echo ไม่พบไฟล์ %INSTALLER%
    echo กรุณาวางไฟล์นี้ไว้ในโฟลเดอร์เดียวกับ %INSTALLER%
    pause
    exit /b 1
)

echo กำลัง Unblock ไฟล์ installer...
powershell -Command "Unblock-File -Path '%~dp0%INSTALLER%'"
echo Unblock สำเร็จ!
echo.
echo กำลังเปิด installer...
start "" "%~dp0%INSTALLER%"
