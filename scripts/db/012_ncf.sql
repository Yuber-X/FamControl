-- =============================================================
-- FAControl — Comprobante fiscal (NCF) por préstamo
-- Script: 012_ncf.sql
-- Pedido del cliente (2026-07-25): la empresa está legalizada ante la DGII
-- (usa la versión gratuita / Facturador Gratuito) y quiere que cada préstamo
-- lleve su comprobante fiscal junto al código.
--
-- DISEÑO:
--  * prestamo.ncf — el comprobante del préstamo. Se puede REGISTRAR uno
--    generado por fuera (Facturador Gratuito DGII) o ASIGNAR de la secuencia
--    local configurada. UNIQUE: un NCF jamás se repite (NULL permitido).
--  * ncf_secuencia — configuración de la secuencia autorizada por la DGII:
--    prefijo (ej. B02 / E32), próxima secuencia, fin del rango y vencimiento.
--    La reserva del siguiente número es atómica (FOR UPDATE) igual que los
--    contadores. Un NCF consumido NUNCA se reusa (regla DGII).
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001. Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 1. prestamo.ncf
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='prestamo' AND COLUMN_NAME='ncf');
SET @sql := IF(@existe=0,
  "ALTER TABLE prestamo ADD COLUMN ncf VARCHAR(19) NULL AFTER codigo",
  'SELECT "prestamo.ncf ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='prestamo' AND INDEX_NAME='uq_prestamo_ncf');
SET @sql := IF(@existe=0, 'ALTER TABLE prestamo ADD UNIQUE KEY uq_prestamo_ncf (ncf)',
  'SELECT "uq_prestamo_ncf ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 2. ncf_secuencia (una fila por tipo de comprobante; hoy se usa una sola)
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ncf_secuencia (
  id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
  -- Prefijo autorizado por la DGII: serie + tipo (ej. 'B02', 'E32')
  prefijo     VARCHAR(5)  NOT NULL,
  -- Largo de la parte numérica: 8 para NCF tradicional (B02...), 10 para e-CF (E32...)
  largo       TINYINT UNSIGNED NOT NULL DEFAULT 8,
  -- Próximo número a asignar y fin del rango autorizado (inclusive)
  proxima     BIGINT UNSIGNED NOT NULL DEFAULT 1,
  fin_rango   BIGINT UNSIGNED NULL,
  -- Fecha de vencimiento de la secuencia autorizada (la app avisa al acercarse)
  vencimiento DATE NULL,
  activo      TINYINT(1) NOT NULL DEFAULT 1,
  created_at  DATETIME NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at  DATETIME NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_ncf_secuencia_prefijo (prefijo)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
