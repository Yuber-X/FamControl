; =============================================================
; FAControl — INSTALADOR COMPLETO (Inno Setup 6)
; Compilar:  ISCC.exe FAControl.iss
; Requiere:  ..\publish\ generado con:
;   dotnet publish src/FAControl.App -c Release -r win-x64 --self-contained true -o publish
;
; ¿CUÁL DE LOS DOS MANDAR?
;   FAControl_Setup_x.y.z.exe   PC nueva: trae MySQL, AnyDesk y Google Drive.
;   FAControl_Update_x.y.z.exe  PC que YA tiene FAControl: solo la aplicación.
;                               Ver FAControl_Update.iss.
;
; PREREQUISITOS (pedido del cliente 2026-07-29): si los instaladores de AnyDesk,
; MySQL y Google Drive están en installer\prerequisitos\, el asistente ofrece
; instalarlos antes de abrir FAControl. Si NO están, el instalador compila igual
; y esa página simplemente no aparece. Ver prerequisitos\LEEME.txt.
; =============================================================

#include "comun_defines.iss"

; --- Prerequisitos: nombre esperado de cada instalador ---
#define DirPrereq "prerequisitos"
#define ExeAnyDesk "AnyDesk.exe"
; Bundle COMPLETO de MySQL (593 MB): trae MySQL Server 8.0 Y Workbench adentro.
; Se cambio el 2026-08-02 por el instalador WEB de 2 MB, que descargaba todo al
; correr: dependia de la conexion del local y en la instalacion del 01-08 se
; salteo la pantalla "Accounts and Roles", que es donde se elige la password de
; root. Con el bundle el asistente corre entero y sin internet.
#define ExeMySql "mysql-installer-community-8.0.46.0.msi"
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

#include "comun_payload.iss"
