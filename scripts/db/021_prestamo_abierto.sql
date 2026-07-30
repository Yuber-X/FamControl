-- =============================================================
-- FAControl — Préstamo ABIERTO (solo interés)
-- Script: 021_prestamo_abierto.sql
--
-- POR QUÉ: el listado de clientes reales que pasó el cliente el 29/07/2026 tiene
-- 7 de 10 préstamos así — "Cuotas: abierto · Abono a capital: abierto · Total a
-- pagar: RD$16,500" — es decir, el cliente paga SOLO el interés cada mes y el
-- capital queda abierto hasta que decida saldarlo. Es la forma de trabajo más
-- común del prestamista con montos grandes.
--
-- PrestControl solo sabía de dos métodos (francés e interés fijo dominicano), y
-- ninguno representa esto: los dos obligan a repartir el capital entre las
-- cuotas. Cargar esos préstamos con otro método habría falseado la cartera.
--
-- CÓMO SE REPRESENTA: N cuotas de puro interés (capital 0) y el capital completo
-- en la última. N es el horizonte acordado, no una obligación de saldar: si el
-- cliente sigue pagando interés, el préstamo se renueva y se corre la última.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente: agregar un valor al ENUM no toca ninguna fila.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

SET @tiene := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'prestamo'
    AND COLUMN_NAME = 'metodo_amortizacion'
    AND COLUMN_TYPE LIKE '%solo_interes%');

SET @sql := IF(@tiene = 0,
  "ALTER TABLE prestamo MODIFY COLUMN metodo_amortizacion
     ENUM('frances','cuota_fija','solo_interes') NOT NULL DEFAULT 'cuota_fija'",
  'SELECT "metodo_amortizacion ya admite solo_interes"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Verificación (informativa al correr el script a mano)
SELECT COLUMN_TYPE AS metodos_admitidos
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'prestamo'
  AND COLUMN_NAME = 'metodo_amortizacion';
