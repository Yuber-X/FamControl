@echo off
rem ===========================================================================
rem  FAControl - Restablecer la contrasena de root de MySQL 8.0 (Windows)
rem
rem  PARA QUE SIRVE
rem  Para cuando nadie sabe la contrasena de root y MySQL contesta
rem  "Access denied for user 'root'@'localhost'". Es el procedimiento oficial
rem  de Oracle (arrancar el servidor con --init-file) automatizado.
rem
rem  QUE NO HACE
rem  NO borra bases de datos, NO borra usuarios, NO toca los datos. Lo unico
rem  que cambia es la contrasena de root@localhost.
rem
rem  COMO SE USA
rem  Clic derecho sobre este archivo > Ejecutar como administrador.
rem
rem  DESPUES
rem  Hay que poner la contrasena nueva en FAControl.App.dll.config, dentro de
rem  la carpeta de instalacion. Sin eso, FAControl sigue sin abrir.
rem ===========================================================================

setlocal EnableExtensions
title FAControl - Restablecer la contrasena de root de MySQL

set "SERVICIO=MySQL80"
set "MYSQLD=C:\Program Files\MySQL\MySQL Server 8.0\bin\mysqld.exe"
set "MYSQL=C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe"
set "INI=C:\ProgramData\MySQL\MySQL Server 8.0\my.ini"
set "INIT=%ProgramData%\fa-reset-init.txt"

rem  Rutas completas de las herramientas de Windows. Con el PATH contaminado
rem  (Git Bash, por ejemplo, trae su propio "timeout") el script fallaba sin
rem  decir por que.
set "SYS=%SystemRoot%\System32"

echo.
echo  ==========================================================
echo   Restablecer la contrasena de root de MySQL
echo  ==========================================================
echo.

rem --- Requisito: permisos de administrador --------------------------------
"%SYS%\net.exe" session >nul 2>&1
if errorlevel 1 (
  echo  [X] Esto necesita permisos de administrador.
  echo      Cerra esta ventana, hace clic derecho sobre el archivo y elegi
  echo      "Ejecutar como administrador".
  echo.
  pause
  exit /b 1
)

rem --- Requisito: que MySQL este donde se espera ----------------------------
rem  Si esta instalado en otra ruta, corregi las lineas "set" de arriba.
if not exist "%MYSQLD%" (
  echo  [X] No encontre mysqld.exe en:
  echo      %MYSQLD%
  echo      Revisa donde quedo instalado MySQL y corregi la ruta arriba.
  echo.
  pause
  exit /b 1
)
if not exist "%INI%" (
  echo  [X] No encontre el my.ini en:
  echo      %INI%
  echo      Para ver la ruta real ejecuta:  sc qc %SERVICIO%
  echo      y mira la parte --defaults-file= .
  echo.
  pause
  exit /b 1
)

rem --- La contrasena nueva --------------------------------------------------
echo  Elegi la contrasena NUEVA para root.
echo.
echo  Consejo: usa SOLO letras y numeros, por ejemplo  FAControl2026
echo  Los simbolos dan dos problemas conocidos: el punto y coma ( ; ) parte
echo  la cadena de conexion de FAControl, y con el teclado en ingles varios
echo  simbolos salen en otra tecla.
echo.
set "NUEVA="
set /p "NUEVA=Contrasena nueva: "
if not defined NUEVA (
  echo.
  echo  [X] No escribiste nada. No se cambio nada.
  echo.
  pause
  exit /b 1
)

echo.
echo  ---------------------------------------------------------
echo  1/5  Parando el servicio %SERVICIO% ...
"%SYS%\net.exe" stop %SERVICIO%
if errorlevel 1 (
  echo.
  echo  [!] No se pudo parar el servicio. Puede que ya estuviera parado.
  echo      Si dice "Acceso denegado", esta ventana no es de administrador.
)

rem  El archivo lleva la contrasena en texto plano: se borra en el paso 4/5.
> "%INIT%" echo ALTER USER 'root'@'localhost' IDENTIFIED BY '%NUEVA%';

echo.
echo  2/5  Arrancando MySQL con la instruccion de cambio ...
rem  --defaults-file NO es opcional: sin el, mysqld apunta a otra carpeta de
rem  datos y parece que se perdieron las bases.
rem  En --init-file las barras van al reves: eso hace el %INIT:\=/%.
start "FAControl reset MySQL" /min "%MYSQLD%" --defaults-file="%INI%" --init-file="%INIT:\=/%" --console

echo       Esperando a que el servidor levante ...
set "LISTO="
for /l %%i in (1,1,30) do (
  if not defined LISTO (
    "%SYS%\timeout.exe" /t 2 /nobreak >nul
    set "MYSQL_PWD=%NUEVA%"
    "%MYSQL%" -u root -e "SELECT 1;" >nul 2>&1
    if not errorlevel 1 set "LISTO=si"
  )
)
set "MYSQL_PWD="

echo.
echo  3/5  Cerrando el arranque temporal ...
"%SYS%\taskkill.exe" /f /im mysqld.exe >nul 2>&1
"%SYS%\timeout.exe" /t 3 /nobreak >nul

echo  4/5  Borrando el archivo temporal con la contrasena ...
if exist "%INIT%" del /f /q "%INIT%"

echo  5/5  Arrancando el servicio %SERVICIO% de nuevo ...
"%SYS%\net.exe" start %SERVICIO%

echo.
echo  ---------------------------------------------------------
if defined LISTO (
  echo   [OK] La contrasena de root quedo cambiada.
  echo.
  echo   ANOTALA AHORA en el papel que le queda al duenio.
  echo.
  echo   FALTA UN PASO: abri con el Bloc de notas EJECUTADO COMO
  echo   ADMINISTRADOR el archivo
  echo       C:\Program Files\FAControl\FAControl.App.dll.config
  echo   y pone esa misma contrasena despues de  Pwd=
  echo   Recorda dejar el punto y coma final:   Pwd=loquesea;
) else (
  echo   [X] No pude confirmar el cambio.
  echo.
  echo   El servidor no llego a aceptar la contrasena nueva en 60 segundos.
  echo   Mira el archivo de error de MySQL, que suele ser:
  echo       C:\ProgramData\MySQL\MySQL Server 8.0\Data\*.err
  echo   Las ultimas lineas dicen por que no arranco.
  echo.
  echo   El servicio se volvio a arrancar igual, asi que la PC queda como
  echo   estaba antes de correr esto.
)
echo  ---------------------------------------------------------
echo.
pause
endlocal
