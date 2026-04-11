@echo off
chcp 65001 >nul
title HTTP Monitor

echo ========================================
echo         HTTP Monitor - Запуск
echo ========================================
echo.

:: Проверка наличия .NET
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo [ОШИБКА] .NET SDK не установлен!
    echo.
    echo Установите .NET 10.0 SDK с сайта:
    echo https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    pause
    exit /b 1
)

:: Отображение версии .NET
for /f "tokens=*" %%i in ('dotnet --version') do set DOTNET_VERSION=%%i
echo [OK] Обнаружен .NET SDK версии %DOTNET_VERSION%
echo.

:: Переход в папку с проектом
cd /d "%~dp0HttpMonitorApp"
if errorlevel 1 (
    echo [ОШИБКА] Папка HttpMonitorApp не найдена!
    echo Убедитесь, что run.bat находится в одной папке с проектом.
    echo.
    pause
    exit /b 1
)

echo [INFO] Восстановление зависимостей...
dotnet restore --verbosity quiet
if errorlevel 1 (
    echo [ОШИБКА] Не удалось восстановить зависимости!
    pause
    exit /b 1
)

echo [INFO] Сборка проекта...
dotnet build --verbosity quiet
if errorlevel 1 (
    echo [ОШИБКА] Не удалось собрать проект!
    pause
    exit /b 1
)

echo [INFO] Запуск приложения...
echo ========================================
echo.

:: Запуск приложения
dotnet run

:: Если приложение завершилось с ошибкой
if errorlevel 1 (
    echo.
    echo ========================================
    echo [ОШИБКА] Приложение завершилось с ошибкой!
    echo.
    echo Возможные решения:
    echo 1. Запустите run.bat от имени администратора
    echo 2. Выполните в PowerShell (админ):
    echo    netsh http add urlacl url=http://localhost:8080/ user="Все"
    echo ========================================
    pause
)