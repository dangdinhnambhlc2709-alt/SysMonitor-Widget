@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul

cd /d "%~dp0"

echo ============================================================================
echo   TU DONG DAY MA NGUON SYSMONITOR WIDGET LEN GITHUB (GIU NGUYEN README.md)
echo ============================================================================
echo.

where git >nul 2>&1
if errorlevel 1 (
    echo [LOI] Chua cai dat Git tren may tinh nay.
    pause
    exit /b 1
)

REM 1. Dam bao dang o nhanh main va lay du lieu moi nhat tu GitHub
echo [1/5] Dang tai lich su moi nhat tu origin/main...
git fetch origin
if errorlevel 1 (
    echo [LOI] Khong the ket noi GitHub. Hay kiem tra mang hoac dang nhap Git.
    pause
    exit /b 1
)

REM 2. Keo nguyen ban README.md moi nhat tu origin/main (tuyet doi khong ghi de README)
echo [2/5] Dang dong bo nguyen ban README.md tu GitHub (origin/main)...
git checkout origin/main -- README.md
if errorlevel 1 (
    echo [CANH BAO] Khong tim thay README.md tren origin/main, bo qua buoc nay.
)

REM Kiem tra neu local dang cham hon origin/main thi dong bo nhe nhang (uu tien README cua GitHub)
for /f %%c in ('git rev-list --count HEAD..origin/main 2^>nul') do set "BEHIND_COUNT=%%c"
if not "!BEHIND_COUNT!"=="" if !BEHIND_COUNT! GTR 0 (
    echo [THONG BAO] Kho GitHub co !BEHIND_COUNT! commit moi hon may tinh nay. Dang dong bo an toan...
    git merge --no-edit -X theirs origin/main
    if errorlevel 1 (
        echo [LOI] Co xung dot khi dong bo voi origin/main. Hay kiem tra thu cong truoc khi day ma.
        pause
        exit /b 1
    )
    git checkout origin/main -- README.md
)

REM 3. Hien thi danh sach thay doi va hoi ghi chu commit
echo.
echo [3/5] Danh sach tep thay doi chuan bi day len:
git status -s
echo.

set "COMMIT_MSG="
set /p "COMMIT_MSG=Nhap ghi chu commit ngan gon (bo trong se dung mac dinh): "
if "!COMMIT_MSG!"=="" set "COMMIT_MSG=Update SysMonitor Widget source code"

REM 4. Them ma nguon sach va chac chan README.md khong nam trong commit
echo.
echo [4/5] Dang dong goi commit...
git add .
git checkout origin/main -- README.md >nul 2>&1
git reset -- README.md >nul 2>&1

git diff --cached --quiet
if not errorlevel 1 (
    echo [THONG BAO] Khong co thay doi ma nguon nao moi de commit.
    pause
    exit /b 0
)

git commit -m "!COMMIT_MSG!"
if errorlevel 1 (
    echo [LOI] Tao commit that bai.
    pause
    exit /b 1
)

REM 5. Day ma nguon len nhanh main (khong bao gio dung --force)
echo.
echo [5/5] Dang day ma nguon len nhanh main (origin)...
git push -u origin main
if errorlevel 1 (
    echo [LOI] Day ma nguon that bai. Hay kiem tra quyen dang nhap GitHub hoac ket noi mang.
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo   HOAN TAT! Da day toan bo ma nguon len GitHub (giu nguyen README.md).
echo ============================================================================
pause
endlocal
exit /b 0
