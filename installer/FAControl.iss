; =============================================================
; FAControl — Instalador (Inno Setup 6)
; Compilar:  ISCC.exe FAControl.iss
; Requiere:  ..\publish\ generado con:
;   dotnet publish src/FAControl.App -c Release -r win-x64 --self-contained true -o publish
;
; PREREQUISITOS (pedido del cliente 2026-07-29): si los instaladores de AnyDesk,
; MySQL y Google Drive están en installer\prerequisitos\, el asistente ofrece
; instalarlos antes de abrir FAControl. Si NO están, el instalador compila igual
; y esa página simplemente no aparece. Ver prerequisitos\LEEME.txt.
; =============================================================

#define AppNombre "FAControl"
#define AppVersion "1.8.0"
#define AppEditor "Yuber Santana"
#define AppExe "FAControl.App.exe"
#define AppTelefono "849-438-0242"

; --- Prerequisitos: nombre esperado de cada instalador ---
#define DirPrereq "prerequisitos"
#define ExeAnyDesk "AnyDesk.exe"
; Es el instalador WEB de MySQL: pesa 2 MB y descarga lo demas al correr,
; asi que la PC del cliente necesita internet durante ESE paso.
#define ExeMySql "mysql-installer-web-community-8.0.46.0.msi"
#define ExeDrive "GoogleDriveSetup.exe"

; FileExists se evalúa al COMPILAR: por eso el .iss sirve con y sin los archivos
#define TieneAnyDesk FileExists(AddBackslash(SourcePath) + DirPrereq + "\" + ExeAnyDesk)
#define TieneMySql   FileExists(AddBackslash(SourcePath) + DirPrereq + "\" + ExeMySql)
#define TieneDrive   FileExists(AddBackslash(SourcePath) + DirPrereq + "\" + ExeDrive)

[Setup]
AppId={{7E2B9C41-5D8F-4A36-9B1C-FACONTROL}
AppName={#AppNombre}
AppVersion={#AppVersion}
AppPublisher={#AppEditor}
AppSupportPhone={#AppTelefono}
DefaultDirName={autopf}\{#AppNombre}
DefaultGroupName={#AppNombre}
OutputDir=Output
OutputBaseFilename=FAControl_Setup_{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
; Ícono de FAControl: en el .exe del instalador, en el Panel de control y en los accesos directos
SetupIconFile=..\src\FAControl.App\Assets\facontrol.ico

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "escritorio"; Description: "Crear acceso directo en el escritorio"; \
  GroupDescription: "Accesos directos:"

; --- Prerequisitos: una casilla por programa, solo si el instalador está presente ---
#if TieneMySql
Name: "prereq_mysql"; Description: "Instalar MySQL Server (la base de datos de FAControl)"; \
  GroupDescription: "Programas necesarios:"
#endif
#if TieneAnyDesk
Name: "prereq_anydesk"; Description: "Instalar AnyDesk (para dar soporte a distancia)"; \
  GroupDescription: "Programas necesarios:"
#endif
#if TieneDrive
Name: "prereq_drive"; Description: "Instalar Google Drive (para subir los respaldos a la nube)"; \
  GroupDescription: "Programas necesarios:"
#endif

[Files]
; Aplicación publicada (self-contained: no requiere instalar .NET)
Source: "..\publish\*"; DestDir: "{app}"; \
  Excludes: "FAControl.App.dll.config"; \
  Flags: ignoreversion recursesubdirs createallsubdirs
; La configuración (cadena de conexión) NUNCA se pisa en actualizaciones
Source: "..\publish\FAControl.App.dll.config"; DestDir: "{app}"; \
  Flags: onlyifdoesntexist uninsneveruninstall
; Scripts de base de datos y documentación.
; Van TODAS las migraciones: una instalación nueva se arma sola con el esquema
; embebido, pero actualizar la base que ya tiene el cliente necesita correrlas.
; Se excluyen a propósito: 999_rollback.sql (BORRA la base entera — no tiene
; nada que hacer en la máquina del cliente) y los seeds de datos de prueba.
Source: "..\scripts\db\*.sql"; DestDir: "{app}\scripts\db"; \
  Excludes: "999_rollback.sql,002_seed_data.sql,seed_*.sql"; \
  Flags: ignoreversion
Source: "..\docs\INSTALL.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\MANUAL.md"; DestDir: "{app}\docs"; Flags: ignoreversion

; --- Prerequisitos: van a la carpeta temporal y se borran al terminar ---
#if TieneMySql
Source: "{#DirPrereq}\{#ExeMySql}"; DestDir: "{tmp}"; \
  Flags: deleteafterinstall; Tasks: prereq_mysql
#endif
#if TieneAnyDesk
Source: "{#DirPrereq}\{#ExeAnyDesk}"; DestDir: "{tmp}"; \
  Flags: deleteafterinstall; Tasks: prereq_anydesk
#endif
#if TieneDrive
Source: "{#DirPrereq}\{#ExeDrive}"; DestDir: "{tmp}"; \
  Flags: deleteafterinstall; Tasks: prereq_drive
#endif

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

[Run]
; ---- Primero los prerequisitos, DESPUÉS la app ----
; Van con la interfaz visible a propósito: MySQL pide contraseña de root y hay
; que elegirla con el cliente delante, no dejarla al azar.
#if TieneMySql
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\{#ExeMySql}"""; \
  StatusMsg: "Instalando MySQL Server…"; Tasks: prereq_mysql
#endif
#if TieneAnyDesk
Filename: "{tmp}\{#ExeAnyDesk}"; \
  StatusMsg: "Instalando AnyDesk…"; Tasks: prereq_anydesk
#endif
#if TieneDrive
Filename: "{tmp}\{#ExeDrive}"; \
  StatusMsg: "Instalando Google Drive…"; Tasks: prereq_drive
#endif

Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppNombre} ahora"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Los logs se van con la app. Se CONSERVAN a propósito: ajustes.json, la
; licencia (licencia.json — si no, reinstalar borraría la activación), la
; carpeta expedientes\ (documentos del cliente) y la base MySQL.
Type: filesandordirs; Name: "{app}\logs"
