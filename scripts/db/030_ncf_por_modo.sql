-- =============================================================
-- FAControl — La secuencia de comprobantes fiscales, POR MODO
-- Script: 030_ncf_por_modo.sql
--
-- Pedido del cliente (2026-07-30):
--   "En comprobante fiscal debe de estar vacio por cada modo (no tener los
--    mismos datos automaticos por cada modo, puede generar conflictos por si
--    son una empresa de multi-desempeños)."
--
-- EL PROBLEMA
-- `ncf_secuencia` guardaba UNA fila para toda la suite. Al abrir Configuracion
-- desde cualquier estancia se veia y editaba la misma secuencia, asi que
-- configurar el rango del dealer pisaba el de los prestamos. Peor: el consumo
-- es irreversible (un NCF entregado no se reusa, regla DGII), asi que dos
-- estancias tomando numeros del mismo rango entregan comprobantes que la DGII
-- espera de un unico libro de ventas.
--
-- Con un negocio de varios rubros —que es el caso— cada estancia puede tener su
-- propia autorizacion de la DGII, o incluso su propio RNC. Compartir el rango
-- es directamente incorrecto.
--
-- LA SOLUCION
-- Una fila por modo. La clave unica pasa de (prefijo) a (modo, prefijo): dos
-- estancias pueden usar el mismo prefijo B02 con rangos distintos, que es lo
-- normal cuando la DGII autoriza por separado.
--
-- BACKFILL
-- Lo que ya existe se asigna a 'prestcontrol': hasta hoy el NCF solo se
-- consumia al asignarlo a un prestamo, y los prestamos personales viven ahi.
-- Las demas estancias arrancan VACIAS, que es exactamente lo pedido.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 1. La columna modo
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ncf_secuencia' AND COLUMN_NAME = 'modo');
SET @sql := IF(@existe = 0,
  "ALTER TABLE ncf_secuencia
     ADD COLUMN modo ENUM('prestcontrol','dealercontrol','autocontrol','pos500')
     NOT NULL DEFAULT 'prestcontrol' AFTER id",
  'SELECT "ncf_secuencia.modo ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 2. La clave unica pasa a (modo, prefijo)
--    Se agrega la nueva ANTES de soltar la vieja: si algo fallara en el medio,
--    la tabla nunca queda sin proteccion contra prefijos duplicados.
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ncf_secuencia'
    AND INDEX_NAME = 'uq_ncf_secuencia_modo_prefijo');
SET @sql := IF(@existe = 0,
  'ALTER TABLE ncf_secuencia ADD UNIQUE KEY uq_ncf_secuencia_modo_prefijo (modo, prefijo)',
  'SELECT "uq_ncf_secuencia_modo_prefijo ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ncf_secuencia'
    AND INDEX_NAME = 'uq_ncf_secuencia_prefijo');
SET @sql := IF(@existe > 0,
  'ALTER TABLE ncf_secuencia DROP INDEX uq_ncf_secuencia_prefijo',
  'SELECT "uq_ncf_secuencia_prefijo ya no esta"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 3. El DEFAULT se saca despues del backfill.
--    Mientras existia, sirvio para que las filas viejas cayeran en
--    'prestcontrol' sin escribir un UPDATE. De aca en mas conviene que el modo
--    sea obligatorio y explicito: una fila sin modo declarado seria una
--    secuencia que nadie sabe de quien es.
-- -------------------------------------------------------------
ALTER TABLE ncf_secuencia
  MODIFY COLUMN modo ENUM('prestcontrol','dealercontrol','autocontrol','pos500') NOT NULL;
