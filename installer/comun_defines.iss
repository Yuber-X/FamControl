; =============================================================
; FAControl — Datos compartidos por el INSTALADOR y el ACTUALIZADOR
;
; Se incluye al PRINCIPIO de los dos .iss, antes de [Setup], porque estos
; valores se usan ahí. Tenerlos en un solo archivo evita el error clásico de
; publicar un actualizador con una versión distinta a la del instalador.
; =============================================================

#define AppNombre "FAControl"
#define AppVersion "2.1.1"
#define AppEditor "Yuber Santana"
#define AppExe "FAControl.App.exe"
#define AppTelefono "849-438-0242"

; Mutex que levanta la aplicación al arrancar (App.NombreMutex). Es lo que le
; permite al instalador saber que FAControl está ABIERTO.
;
; POR QUÉ (2026-09-05). La 2.1.0 se instaló en la PC del cliente y la aplicación
; siguió igual que antes. La app estaba abierta, sus DLL bloqueados, y Windows
; no los pudo reemplazar: los archivos quedaron diferidos al próximo reinicio.
; El asistente igual dijo "terminó". CloseApplications solo no alcanzó — el
; Restart Manager no siempre ve el proceso, y una ventana modal no se cierra.
;
; Con el mutex, Setup lo detecta ANTES de copiar y pide cerrar la aplicación.
; Si alguna vez cambia, hay que cambiarlo TAMBIÉN en App.xaml.cs.
#define AppMutexNombre "Global\FAControl.App.Instancia"

; OJO CON EL AppId: NO está acá a propósito.
;
; Tiene que ser IDÉNTICO en FAControl.iss y en FAControl_Update.iss — es lo que
; hace que el actualizador reconozca la instalación existente, herede su carpeta
; y no aparezca dos veces en "Agregar o quitar programas".
;
; Se escribe literal en los dos archivos en vez de salir de un #define porque el
; valor empieza con "{" y ahí el preprocesador y el escape de llaves de Inno se
; pisan: {{#Define} NO se expande (Inno lee "{{" como llave escapada y se saltea
; el "{#"), y el resultado sería un AppId basura. Un error que no se nota al
; compilar: se nota en la PC del cliente, con dos FAControl instalados.
;
;   AppId={{7E2B9C41-5D8F-4A36-9B1C-FACONTROL}
;
; Si alguna vez cambia, hay que cambiarlo en los DOS .iss y en la ruta del
; registro del [Code] de FAControl_Update.iss.
