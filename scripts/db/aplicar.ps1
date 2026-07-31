<#
    Aplica los scripts de base de datos pendientes de FAControl.

    POR QUE EXISTE
    --------------
    1) PowerShell NO acepta el operador "<" (dice: "The '<' operator is reserved
       for future use"), asi que la forma clasica de correr un .sql —
           mysql -u root -p facontrol_db < script.sql
       — falla en PowerShell aunque funcione en CMD.
    2) Correr las migraciones a mano obliga a acordarse de cuales ya se
       aplicaron. Varias NO son repetibles (005 falla al segundo intento), asi
       que "aplicar todo por las dudas" rompe la base.

    Este script lleva el registro solo, en la tabla `esquema_migracion`: aplica
    unicamente lo que falta y anota lo que aplico.

    USO
    ---
        .\scripts\db\aplicar.ps1                # aplica lo pendiente
        .\scripts\db\aplicar.ps1 -Listar        # solo muestra el estado
        .\scripts\db\aplicar.ps1 -Script 028    # fuerza uno puntual
        .\scripts\db\aplicar.ps1 -Base otra_db  # sobre otra base

    Se puede correr desde cualquier carpeta: las rutas salen de la ubicacion del
    propio script, no del directorio actual.

    PRIMERA CORRIDA
    ---------------
    Si la tabla de registro no existe, se crea y se marcan TODOS los scripts
    actuales como aplicados, sin ejecutarlos. Es lo correcto en los dos casos
    posibles: una base al dia (las migraciones ya se corrieron a mano) o una
    base recien creada por la aplicacion (001_create_schema.sql ya trae todo
    lo de las migraciones incorporado).
#>

param(
    [string]$Base     = "facontrol_db",
    [string]$Usuario  = "root",
    [string]$Servidor = "localhost",
    [int]   $Puerto   = 3306,
    # Prefijo de un script puntual ("028" o "028_venta_cancelada"). Lo corre
    # aunque ya figure como aplicado.
    [string]$Script   = "",
    # Muestra que esta aplicado y que falta, sin tocar nada.
    [switch]$Listar
)

$ErrorActionPreference = "Stop"
$carpeta = $PSScriptRoot

# --- Ubicar el cliente mysql -------------------------------------------------
# El instalador de MySQL no agrega su carpeta al PATH, asi que casi nunca esta
# disponible como comando suelto. Se busca donde se instala por defecto.
# Sin "?." : Windows PowerShell 5.1 (el que trae Windows) no lo entiende.
$comando = Get-Command mysql -ErrorAction SilentlyContinue
$mysql   = if ($comando) { $comando.Source } else { $null }
if (-not $mysql) {
    $mysql = Get-ChildItem "C:\Program Files\MySQL\MySQL Server *\bin\mysql.exe",
                           "C:\Program Files (x86)\MySQL\MySQL Server *\bin\mysql.exe" `
                           -ErrorAction SilentlyContinue |
             Select-Object -First 1 -ExpandProperty FullName
}
if (-not $mysql) {
    Write-Host "No encontre mysql.exe." -ForegroundColor Red
    Write-Host "Agregalo al PATH o instalalo. Suele estar en:" -ForegroundColor Yellow
    Write-Host '  C:\Program Files\MySQL\MySQL Server 8.0\bin'
    exit 1
}

# 001 y 002 no se migran: el primer arranque los corre la aplicacion (van
# embebidos en el ejecutable). 999_rollback y los seed de prueba, tampoco.
$scripts = Get-ChildItem -Path $carpeta -Filter "0*.sql" |
           Where-Object { $_.Name -notmatch '^(001|002|999)_' } |
           Sort-Object Name
if (-not $scripts) {
    Write-Host "No hay scripts de migracion en $carpeta" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "Base: $Base ($Servidor`:$Puerto, usuario $Usuario)" -ForegroundColor Cyan

# La clave se pide UNA vez y se pasa por variable de entorno: escribirla en la
# linea de comandos (-pMiClave) la deja visible en el historial y en la lista de
# procesos. MYSQL_PWD vive solo mientras corre este script. Si ya viene puesta
# desde afuera no se pregunta (permite correrlo desatendido) ni se borra.
$claveVieneDeAfuera = -not [string]::IsNullOrEmpty($env:MYSQL_PWD)
if (-not $claveVieneDeAfuera) {
    $clave = Read-Host "Contrasena de MySQL para '$Usuario'" -AsSecureString
    $env:MYSQL_PWD = [System.Net.NetworkCredential]::new("", $clave).Password
}

<#
    Corre SQL y devuelve @{ Ok = $bool; Salida = $texto }.

    El 2>&1 se maneja adentro a proposito: mysql.exe escribe sus errores por
    stderr y, con $ErrorActionPreference = 'Stop', PowerShell convierte esa
    salida en una excepcion que cortaria el script entero. Aca se baja a
    'Continue' solo durante la llamada para poder informar el error y seguir.
#>
function Invoke-Sql {
    param([string]$Sql)
    $anterior = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $salida = & $mysql "--host=$Servidor" "--port=$Puerto" "--user=$Usuario" `
                           "--default-character-set=utf8mb4" "--batch" "--skip-column-names" `
                           $Base "-e" $Sql 2>&1
        return @{ Ok = ($LASTEXITCODE -eq 0); Salida = ($salida -join "`n") }
    }
    finally { $ErrorActionPreference = $anterior }
}

function Escapar { param([string]$t) $t -replace "'", "''" }

try {
    # --- Registro de migraciones --------------------------------------------
    $existia = (Invoke-Sql "SHOW TABLES LIKE 'esquema_migracion';").Salida.Trim()

    $crear = Invoke-Sql @"
CREATE TABLE IF NOT EXISTS esquema_migracion (
  script      VARCHAR(120) NOT NULL PRIMARY KEY,
  aplicado_at DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP())
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
"@
    if (-not $crear.Ok) {
        Write-Host ""
        Write-Host "No pude conectar a la base:" -ForegroundColor Red
        Write-Host $crear.Salida -ForegroundColor DarkGray
        exit 1
    }

    # Primera corrida: se toma la base como al dia (ver cabecera del archivo).
    if (-not $existia) {
        $valores = ($scripts | ForEach-Object { "('$(Escapar $_.Name)')" }) -join ","
        $r = Invoke-Sql "INSERT IGNORE INTO esquema_migracion (script) VALUES $valores;"
        if (-not $r.Ok) {
            Write-Host $r.Salida -ForegroundColor Red
            exit 1
        }
        Write-Host ""
        Write-Host "Primera corrida: cree el registro de migraciones y marque los" -ForegroundColor Yellow
        Write-Host "$($scripts.Count) scripts actuales como aplicados, SIN ejecutarlos." -ForegroundColor Yellow
        Write-Host "De ahora en mas solo se aplica lo nuevo." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Si tu base NO estaba al dia, corre el que falte a mano:" -ForegroundColor DarkGray
        Write-Host "  .\scripts\db\aplicar.ps1 -Script 0NN" -ForegroundColor DarkGray
        Write-Host ""
        exit 0
    }

    $aplicados = (Invoke-Sql "SELECT script FROM esquema_migracion;").Salida `
                    -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }

    # --- Que corresponde correr ---------------------------------------------
    if ($Script) {
        $pendientes = @($scripts | Where-Object { $_.Name -like "$Script*" })
        if (-not $pendientes) {
            Write-Host "Ningun script empieza con '$Script'." -ForegroundColor Red
            exit 1
        }
    } else {
        $pendientes = @($scripts | Where-Object { $aplicados -notcontains $_.Name })
    }

    if ($Listar) {
        Write-Host ""
        foreach ($s in $scripts) {
            $marca = if ($aplicados -contains $s.Name) { "aplicado " } else { "PENDIENTE" }
            $color = if ($aplicados -contains $s.Name) { "DarkGray" } else { "Yellow" }
            Write-Host ("  [{0}] {1}" -f $marca, $s.Name) -ForegroundColor $color
        }
        Write-Host ""
        exit 0
    }

    if (-not $pendientes) {
        Write-Host "La base ya esta al dia: nada que aplicar." -ForegroundColor Green
        exit 0
    }

    Write-Host "Pendientes: $($pendientes.Count)" -ForegroundColor Cyan
    Write-Host ""

    $fallaron = 0
    foreach ($s in $pendientes) {
        Write-Host ("  {0,-42} " -f $s.Name) -NoNewline

        # "source" es un comando del cliente mysql y evita la redireccion por
        # completo. La ruta va con / porque el cliente toma la barra invertida
        # como escape.
        $r = Invoke-Sql ("source " + ($s.FullName -replace '\\', '/'))

        if ($r.Ok) {
            Invoke-Sql "INSERT IGNORE INTO esquema_migracion (script) VALUES ('$(Escapar $s.Name)');" | Out-Null
            Write-Host "OK" -ForegroundColor Green
        } else {
            # No se anota, y se corta: las migraciones dependen del orden, seguir
            # con la siguiente sobre un esquema a medio migrar empeora las cosas.
            $fallaron++
            Write-Host "FALLO" -ForegroundColor Red
            $r.Salida -split "`n" | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
            break
        }
    }

    Write-Host ""
    if ($fallaron -eq 0) {
        Write-Host "Listo: la base esta al dia." -ForegroundColor Green
    } else {
        Write-Host "Se corto en el script de arriba. No se aplico nada despues de el." -ForegroundColor Red
        exit 1
    }
}
finally {
    # Que la clave no quede en el entorno pase lo que pase
    if (-not $claveVieneDeAfuera) { Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue }
}
