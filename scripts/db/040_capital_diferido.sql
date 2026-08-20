-- =============================================================
-- FAControl — Interes fijo primero y capital despues
-- Script: 040_capital_diferido.sql
--
-- Pedido del cliente (2026-08-06):
--   "fueron unos clientes de los viejos que tienen otra forma de prestar que no
--    es la que esta en plataforma... que ellos le prestan con una taza fija de 6
--    meses, y del 7 en adelante cambia segun el capital que dejaron... tienen la
--    solicitud y estan analizando pero no pudieron imprimir cotizacion ni nada
--    porque el sistema no tiene esa opcion"
--
-- QUE ES
-- Las primeras cuotas son de PURO INTERES (como el prestamo abierto). Desde la
-- cuota que se elija, cada cuota lleva ademas un abono a capital CONSTANTE y el
-- interes pasa a calcularse sobre el saldo, que ahora si baja. La cuota total va
-- bajando mes a mes: es amortizacion alemana con periodo de gracia.
--
-- POR QUE UNA COLUMNA NUEVA Y NO DEDUCIRLO DE LAS CUOTAS
-- Se podria mirar en que cuota aparece el primer capital > 0, pero eso deja de
-- funcionar en cuanto el prestamo se corrige o se refinancia. El dato es del
-- CONTRATO ("acordamos 6 meses de solo interes"), asi que se guarda como tal.
--
-- NULL en cuota_inicio_capital significa "modo automatico": lo decide el sistema
-- con AmortizacionService.CuotaInicioCapitalSugerida (un tercio del plazo, que
-- es lo que da 7 sobre 18 en el ejemplo del cliente). Todos los prestamos que ya
-- existen quedan en NULL y no les cambia nada: la columna solo se lee cuando el
-- metodo es 'capital_diferido'.
--
-- LA PRIMERA QUE APLICA LA APLICACION SOLA
-- Desde la version 2.0.0 FAControl corre las migraciones pendientes al arrancar
-- (MigradorEsquema), asi que ya no hace falta correr aplicar.ps1 en la PC del
-- cliente. Por eso este script tiene que poder ejecutarse VARIAS VECES sin
-- romper nada: si el arranque se interrumpe a la mitad, el siguiente lo repite.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- --- 1. El metodo nuevo en el ENUM -------------------------------------------
SET @tiene_metodo := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'prestamo'
    AND COLUMN_NAME = 'metodo_amortizacion'
    AND COLUMN_TYPE LIKE '%capital_diferido%');

SET @sql := IF(@tiene_metodo = 0,
  "ALTER TABLE prestamo MODIFY COLUMN metodo_amortizacion
     ENUM('frances','cuota_fija','solo_interes','capital_diferido')
     NOT NULL DEFAULT 'cuota_fija'",
  'SELECT "metodo_amortizacion ya admite capital_diferido"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- --- 2. En que cuota empieza a cobrarse el capital ---------------------------
-- MySQL 8 NO tiene "ADD COLUMN IF NOT EXISTS" (eso es MariaDB): hay que
-- preguntarle al information_schema.
SET @tiene_col := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'prestamo'
    AND COLUMN_NAME = 'cuota_inicio_capital');

SET @sql := IF(@tiene_col = 0,
  "ALTER TABLE prestamo
     ADD COLUMN cuota_inicio_capital INT UNSIGNED NULL
     COMMENT 'capital_diferido: primera cuota con abono a capital. NULL = automatico'
     AFTER metodo_amortizacion",
  'SELECT "prestamo.cuota_inicio_capital ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- --- Verificacion (informativa al correr el script a mano) -------------------
SELECT COLUMN_NAME, COLUMN_TYPE
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'prestamo'
  AND COLUMN_NAME IN ('metodo_amortizacion', 'cuota_inicio_capital')
ORDER BY ORDINAL_POSITION;
