-- =============================================================
-- FAControl — Comprobante fiscal en los cobros del dealer
-- Script: 042_ncf_en_cobros_del_dealer.sql
--
-- Pedido del cliente (2026-08-24, por escrito):
--   "Agregar lo del comprobante fiscal manual desde cobros tanto en
--    'alquileres' como en 'ventas', igual como se hizo o se va hacer en
--    PrestControl."
--
-- CONTEXTO
-- El 041 llevo el comprobante al cobro de un prestamo. DealerControl factura
-- por otros dos caminos —el cobro de un alquiler y el abono a un plazo de una
-- venta financiada— y ninguno tenia donde guardar un NCF. Hasta hoy esas
-- facturas salian sin comprobante.
--
-- MAS SIMPLE QUE EN PRESTCONTROL
-- `pago` parte un cobro en varias filas (una por cuota), asi que alli el NCF
-- vive en la fila principal. Aca no hace falta esa distincion: tanto
-- `alquiler_pago` como `venta_plazo_pago` guardan UNA fila por cobro, que es
-- exactamente un documento fiscal. La columna es 1:1 con la factura.
--
-- LA CLAVE UNICA
-- Una por tabla, igual que uq_prestamo_ncf y uq_pago_ncf: un comprobante
-- entregado no se repite ni se reusa (regla DGII). Los NULL no molestan —
-- MySQL no los compara en un indice UNIQUE— asi que los cobros sin
-- comprobante conviven sin chocar entre si.
--
-- Cada estancia consume de SU secuencia (030): el NCF de un alquiler sale del
-- talonario de DealerControl, no del de los prestamos.
--
-- LO QUE YA EXISTE NO SE TOCA: los cobros historicos quedan en NULL.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 1. alquiler_pago.ncf
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'alquiler_pago' AND COLUMN_NAME = 'ncf');
SET @sql := IF(@existe = 0,
  "ALTER TABLE alquiler_pago
     ADD COLUMN ncf VARCHAR(19) NULL COMMENT 'Comprobante fiscal del cobro (042)'
     AFTER metodo_pago",
  'SELECT "alquiler_pago.ncf ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'alquiler_pago'
    AND INDEX_NAME = 'uq_alquiler_pago_ncf');
SET @sql := IF(@existe = 0,
  'ALTER TABLE alquiler_pago ADD UNIQUE KEY uq_alquiler_pago_ncf (ncf)',
  'SELECT "uq_alquiler_pago_ncf ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 2. venta_plazo_pago.ncf
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'venta_plazo_pago' AND COLUMN_NAME = 'ncf');
SET @sql := IF(@existe = 0,
  "ALTER TABLE venta_plazo_pago
     ADD COLUMN ncf VARCHAR(19) NULL COMMENT 'Comprobante fiscal del cobro (042)'
     AFTER metodo_pago",
  'SELECT "venta_plazo_pago.ncf ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'venta_plazo_pago'
    AND INDEX_NAME = 'uq_plazo_pago_ncf');
SET @sql := IF(@existe = 0,
  'ALTER TABLE venta_plazo_pago ADD UNIQUE KEY uq_plazo_pago_ncf (ncf)',
  'SELECT "uq_plazo_pago_ncf ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
