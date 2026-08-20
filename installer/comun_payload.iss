; =============================================================
; FAControl — Contenido compartido por el INSTALADOR y el ACTUALIZADOR
;
; Se incluye al FINAL de los dos .iss. Acá va lo que se copia a la PC: es
; idéntico en los dos casos, y lo único que cambia entre uno y otro son los
; prerequisitos (MySQL, AnyDesk, Google Drive), que solo trae el instalador.
;
; LO QUE UNA ACTUALIZACIÓN NO PISA (y por qué)
;   FAControl.App.dll.config  la contraseña de MySQL que se configuró al instalar
;   licencia.json             la activación (pisarla obligaría a reactivar)
;   ajustes.json              respaldo automático, tema, escala, correo
;   expedientes\              contratos escaneados: son documentos del negocio
;   la base de datos          no la toca el instalador; la app la pone al día sola
; =============================================================

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
; Aplicación publicada (self-contained: no requiere instalar .NET)
Source: "..\publish\*"; DestDir: "{app}"; \
  Excludes: "FAControl.App.dll.config"; \
  Flags: ignoreversion recursesubdirs createallsubdirs
; La configuración (cadena de conexión) NUNCA se pisa en actualizaciones.
; Se envía la de ESTA carpeta, no la de publish\: esa es la de desarrollo y
; apunta a la base local con root/root. Mandársela al cliente sería decirle al
; instalador que la password de MySQL tiene que ser "root". La que va lleva un
; marcador que hay que reemplazar (paso 4.6 de INSTALL.md).
Source: "FAControl.App.dll.config"; DestDir: "{app}"; \
  Flags: onlyifdoesntexist uninsneveruninstall
; Scripts de base de datos y documentación.
; Desde la 2.0.0 la aplicación aplica sola las migraciones que falten al
; arrancar (MigradorEsquema), así que estas copias son de respaldo: sirven para
; revisar qué hace cada una o para correrlas a mano con aplicar.ps1 si hiciera
; falta. Se excluyen a propósito: 999_rollback.sql (BORRA la base entera — no
; tiene nada que hacer en la máquina del cliente) y los seeds de datos de prueba.
Source: "..\scripts\db\*.sql"; DestDir: "{app}\scripts\db"; \
  Excludes: "999_rollback.sql,002_seed_data.sql,seed_*.sql"; \
  Flags: ignoreversion
Source: "..\docs\INSTALL.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\MANUAL.md"; DestDir: "{app}\docs"; Flags: ignoreversion
; Cómo dejar andando el correo automático de Gmail. Va instalada porque el
; paso que traba a todo el mundo (prender la verificación en 2 pasos) se hace
; en el navegador, delante de la PC del cliente.
Source: "..\docs\CORREO-GMAIL.md"; DestDir: "{app}\docs"; Flags: ignoreversion
; Herramienta de rescate: restablece la password de root de MySQL cuando la PC
; ya tenia MySQL y nadie sabe cual es (caso real del 01-08-2026). Se instala
; ademas de ir suelta en el .rar, porque si FAControl quedo instalado pero no
; conecta, esta carpeta es lo unico que el tecnico tiene seguro a mano.
Source: "..\scripts\soporte\reset_password_root_mysql.bat"; \
  DestDir: "{app}\scripts\soporte"; Flags: ignoreversion

[Dirs]
; La app escribe logs\, ajustes.json y licencia.json junto al ejecutable:
; los usuarios estándar necesitan permiso de modificación
Name: "{app}"; Permissions: users-modify
Name: "{app}\logs"; Permissions: users-modify
; Expediente digital de los contratos (018): acá van los archivos que sube el
; usuario. NO se borra al desinstalar — son documentos del negocio.
Name: "{app}\expedientes"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppNombre}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Manual de usuario"; Filename: "{app}\docs\MANUAL.md"
Name: "{autodesktop}\{#AppNombre}"; Filename: "{app}\{#AppExe}"; Tasks: escritorio

[UninstallDelete]
; Los logs se van con la app. Se CONSERVAN a propósito: ajustes.json, la
; licencia (licencia.json — si no, reinstalar borraría la activación), la
; carpeta expedientes\ (documentos del cliente) y la base MySQL.
Type: filesandordirs; Name: "{app}\logs"
