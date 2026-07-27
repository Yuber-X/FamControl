; =============================================================
; FAControl — Instalador (Inno Setup 6)
; Compilar:  ISCC.exe FAControl.iss
; Requiere:  ..\publish\ generado con:
;   dotnet publish src/FAControl.App -c Release -r win-x64 --self-contained true -o publish
; =============================================================

#define AppNombre "FAControl"
#define AppVersion "1.5.0"
#define AppEditor "Yuber Santana"
#define AppExe "FAControl.App.exe"

[Setup]
AppId={{7E2B9C41-5D8F-4A36-9B1C-FACONTROL}
AppName={#AppNombre}
AppVersion={#AppVersion}
AppPublisher={#AppEditor}
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
SetupIconFile=..\src\FAControl.App\Assets\facontrol.ico

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "escritorio"; Description: "Crear acceso directo en el escritorio"; \
  GroupDescription: "Accesos directos:"

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
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppNombre} ahora"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Los logs se van con la app. Se CONSERVAN a propósito: ajustes.json, la
; licencia (licencia.json — si no, reinstalar borraría la activación), la
; carpeta expedientes\ (documentos del cliente) y la base MySQL.
Type: filesandordirs; Name: "{app}\logs"
