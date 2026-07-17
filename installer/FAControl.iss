; =============================================================
; FAControl — Instalador (Inno Setup 6)
; Compilar:  ISCC.exe FAControl.iss
; Requiere:  ..\publish\ generado con:
;   dotnet publish src/FAControl.App -c Release -r win-x64 --self-contained true -o publish
; =============================================================

#define AppNombre "FAControl"
#define AppVersion "1.0.1"
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
; Scripts de base de datos y documentación
Source: "..\scripts\db\001_create_schema.sql"; DestDir: "{app}\scripts\db"; Flags: ignoreversion
Source: "..\scripts\db\003_crear_usuario_dedicado.sql"; DestDir: "{app}\scripts\db"; Flags: ignoreversion
Source: "..\docs\INSTALL.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "..\docs\MANUAL.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Dirs]
; La app escribe logs\ y ajustes.json junto al ejecutable:
; los usuarios estándar necesitan permiso de modificación
Name: "{app}"; Permissions: users-modify
Name: "{app}\logs"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppNombre}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Manual de usuario"; Filename: "{app}\docs\MANUAL.md"
Name: "{autodesktop}\{#AppNombre}"; Filename: "{app}\{#AppExe}"; Tasks: escritorio

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppNombre} ahora"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Los logs se van con la app; ajustes.json y la BD (MySQL) se conservan
Type: filesandordirs; Name: "{app}\logs"
