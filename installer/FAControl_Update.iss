; =============================================================
; FAControl — ACTUALIZADOR (Inno Setup 6)
; Compilar:  ISCC.exe FAControl_Update.iss
;
; POR QUÉ EXISTE (pedido del cliente 2026-08-06)
; ---------------------------------------------
; "ya el cliente tiene el software en funcionamiento, pero para agregar la nueva
;  version de FAControl temen que se tenga que hacer toda la instalacion desde
;  el inicio."
;
; No hace falta. Este .exe reemplaza SOLO la aplicación:
;   · no trae MySQL, ni AnyDesk, ni Google Drive (pesa ~80 MB en vez de ~900)
;   · no pregunta carpeta: usa la que ya tiene la instalación
;   · no toca la base de datos ni sus datos
;   · conserva la contraseña configurada, la licencia, los ajustes y los
;     expedientes escaneados (ver comun_payload.iss)
;
; El esquema de la base lo pone al día la propia aplicación en el primer
; arranque (MigradorEsquema): agrega columnas y valores nuevos, nunca borra.
; Por eso el actualizador NO necesita la contraseña de MySQL.
;
; SI FAControl NO ESTÁ INSTALADO, este .exe se niega a correr y manda a usar
; FAControl_Setup_x.y.z.exe. Instalar "la actualización" sobre una PC limpia
; dejaría la aplicación sin MySQL, que es el error más caro de diagnosticar.
; =============================================================

#include "comun_defines.iss"

[Setup]
; MISMO AppId que el instalador: así Windows lo ve como la misma aplicación,
; el actualizador hereda la carpeta elegida y no aparece dos veces en
; "Agregar o quitar programas".
AppId={{7E2B9C41-5D8F-4A36-9B1C-FACONTROL}
AppName={#AppNombre}
AppVersion={#AppVersion}
AppPublisher={#AppEditor}
AppSupportPhone={#AppTelefono}
DefaultDirName={autopf}\{#AppNombre}
DefaultGroupName={#AppNombre}
OutputDir=Output
OutputBaseFilename=FAControl_Update_{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=..\src\FAControl.App\Assets\facontrol.ico
UninstallDisplayIcon={app}\{#AppExe}

; --- Lo que hace corta la actualización ---
; UsePreviousAppDir es el default, pero acá se declara: es LA razón de que se
; pueda saltear la pantalla de carpeta sin riesgo de instalar en otro lado.
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=no

; Si FAControl está abierto, sus DLL están bloqueados y la copia fallaría a
; medias. Con esto Inno lo detecta por Restart Manager, ofrece cerrarlo y lo
; vuelve a abrir al terminar — en vez de pedir reiniciar Windows.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=yes

[Messages]
spanish.WelcomeLabel1=Actualizar [name]
spanish.WelcomeLabel2=Se va a actualizar [name] a la versión {#AppVersion} en este equipo.%n%nSolo se reemplaza el programa. NO se toca la base de datos, ni los clientes, préstamos, cobros o documentos escaneados. La contraseña de MySQL y la licencia también se conservan.%n%nSi FAControl está abierto, se cerrará solo.
spanish.FinishedLabel=La actualización terminó. Al abrir FAControl por primera vez, el sistema acomoda la base de datos solo; puede tardar unos segundos más de lo normal.

[Tasks]
; Va sin marcar: el acceso directo del escritorio ya existe de la instalación
; original. Está por si el cliente lo borró y lo quiere de vuelta.
Name: "escritorio"; Description: "Volver a crear el acceso directo en el escritorio"; \
  GroupDescription: "Accesos directos:"; Flags: unchecked

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppNombre} ahora"; \
  Flags: nowait postinstall skipifsilent

#include "comun_payload.iss"

; [Code] va ÚLTIMO y después del #include: todo lo que sigue a esta línea se
; lee como Pascal, así que un include acá adentro haría que Inno intente
; compilar el [Files] como si fuera código.
[Code]
{ Clave de desinstalación que escribió el instalador original. Si no está, en
  esta PC no hay FAControl y este .exe no es el que corresponde.
  El GUID va literal y tiene que coincidir con el AppId de arriba: ver la nota
  de comun_defines.iss sobre por qué acá no se usa el preprocesador. }
function RutaDesinstalacion(): String;
begin
  Result := 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' +
            '{7E2B9C41-5D8F-4A36-9B1C-FACONTROL}_is1';
end;

function EstaInstalado(): Boolean;
begin
  { Los dos hives: el instalador corre como admin (HKLM), pero una instalación
    vieja "solo para mí" habría quedado en HKCU. }
  Result := RegKeyExists(HKEY_LOCAL_MACHINE, RutaDesinstalacion()) or
            RegKeyExists(HKEY_CURRENT_USER, RutaDesinstalacion());
end;

function InitializeSetup(): Boolean;
begin
  Result := EstaInstalado();
  if not Result then
    MsgBox('En este equipo no hay ninguna instalación de FAControl.' + #13#10 + #13#10 +
           'Este archivo es solo para ACTUALIZAR una instalación que ya funciona: ' +
           'no trae MySQL, que es la base de datos que FAControl necesita.' + #13#10 + #13#10 +
           'Para instalar por primera vez usá FAControl_Setup_{#AppVersion}.exe.',
           mbCriticalError, MB_OK);
end;
