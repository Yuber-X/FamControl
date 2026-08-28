-- =============================================================
-- FAControl — Comprobante fiscal POR COBRO, no por prestamo
-- Script: 041_ncf_por_cobro.sql
--
-- Pedido del cliente (2026-08-26, Veronica por WhatsApp):
--   "Si lo vi pero es que las facturas de ese prestamos todas salen con es
--    NCF y eso se debe cambiar con cada factura"
-- y (2026-08-24, por escrito):
--   "quieren que se tenga una forma manual de colocar el codigo fiscal [...]
--    en 'Nuevo Prestamo' y en 'registrar pago' [...] debe mostrarse en la
--    factura al imprimir."
--
-- EL PROBLEMA
-- El NCF vivia SOLO en `prestamo`. PagoService estampaba ese mismo numero en
-- el recibo de cada cobro (`Ncf: prestamo.Ncf`), asi que un prestamo de 24
-- cuotas entregaba 24 facturas con UN solo comprobante repetido.
--
-- Ante la DGII eso es incorrecto: cada comprobante ampara UN documento fiscal.
-- Si el cliente cobra 24 cuotas, emite 24 facturas y consume 24 NCF. Repetir
-- el numero deja el libro de ventas sin cuadrar y las facturas sin respaldo.
--
-- LA SOLUCION
-- El comprobante pasa a la operacion que de verdad factura: el cobro.
--
-- POR QUE LA COLUMNA VA EN `pago` Y ES UNIQUE
-- Un cobro puede tocar VARIAS cuotas, y cada cuota afectada genera su propia
-- fila en `pago` con su `numero_recibo`. Pero fiscalmente ese cobro es UN
-- solo documento, asi que consume UN solo NCF: se guarda en la fila principal
-- del cobro (la del recibo que encabeza el comprobante) y las demas filas del
-- mismo cobro quedan en NULL.
--
-- Con eso la clave unica funciona y protege de verdad: MySQL permite tantos
-- NULL como haga falta en un indice UNIQUE, pero rechaza dos cobros con el
-- mismo comprobante. Es la misma garantia que ya tiene `prestamo.ncf`
-- (uq_prestamo_ncf, 001 linea 232) — un NCF entregado no se repite ni se
-- reusa, aunque el cobro despues se anule.
--
-- LO QUE YA EXISTE NO SE TOCA
-- Los pagos historicos quedan con ncf NULL. No se les puede inventar un
-- comprobante hacia atras: los que ya se entregaron en papel llevan el NCF del
-- prestamo, y reescribir eso seria falsear el libro de ventas. De aca en
-- adelante cada cobro lleva el suyo.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 1. La columna
--    VARCHAR(19) igual que prestamo.ncf: alcanza para el NCF tradicional
--    (B02 + 8 digitos) y para el e-CF (E32 + 10), con margen.
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'pago' AND COLUMN_NAME = 'ncf');
SET @sql := IF(@existe = 0,
  "ALTER TABLE pago
     ADD COLUMN ncf VARCHAR(19) NULL COMMENT 'Comprobante fiscal del cobro (041). Solo en la fila principal.'
     AFTER metodo_pago",
  'SELECT "pago.ncf ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 2. La clave unica
--    Se agrega DESPUES de la columna y solo si no esta: un NCF no se repite
--    entre cobros. Los NULL no molestan (MySQL no los compara en UNIQUE).
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'pago'
    AND INDEX_NAME = 'uq_pago_ncf');
SET @sql := IF(@existe = 0,
  'ALTER TABLE pago ADD UNIQUE KEY uq_pago_ncf (ncf)',
  'SELECT "uq_pago_ncf ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
