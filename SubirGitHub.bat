@echo off
cd /d "Z:\Unity Projects\2025_Github\Projecto1\My project\Assets\Scripts"
echo ======================================
echo    🧠 Subiendo cambios a GitHub...
echo ======================================

set /p msg=Escribe el mensaje del commit: 

git add .
git commit -m "%msg%"
git push

echo --------------------------------------
echo ✅ Cambios subidos correctamente
pause
