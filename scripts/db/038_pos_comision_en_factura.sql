-- =============================================================
-- FAControl — Mostrar la comision del vendedor en la factura
-- Script: 038_pos_comision_en_factura.sql
--
-- Pedido del cliente (2026-08-01):
--   "Configuracion > Comision del vendedor > agregar un nuevo checkbox para
--    mostrar la comision del vendedor a la factura si esta activa."
--
-- QUE CAMBIA RESPECTO A 037
-- En 037 se decidio que la comision NUNCA saliera en la factura: es un asunto
-- entre el negocio y su empleado. El dueño ahora quiere poder mostrarla, asi
-- que deja de ser una regla y pasa a ser una OPCION.
--
-- Se agrega una columna aparte en vez de reusar comision_activa porque son dos
-- decisiones distintas: "cuanto gana el vendedor" (interno, para el cuadre) y
-- "el cliente ve cuanto gano el vendedor" (sale impreso). Quien quiera calcular
-- comision sin ensuciar el ticket no deberia verse obligado a elegir.
--
-- Arranca APAGADA, incluso en las bases que ya tenian la comision encendida:
-- el comportamiento de hoy es que no sale, y una migracion no cambia sola lo
-- que se imprime.
--
-- Depende de comision_activa: si la comision esta apagada no hay nada que
-- mostrar, y la casilla queda deshabilitada en Configuracion.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'pos_configuracion'
    AND COLUMN_NAME = 'comision_en_factura');
SET @sql := IF(@existe = 0,
  "ALTER TABLE pos_configuracion
     ADD COLUMN comision_en_factura TINYINT(1) NOT NULL DEFAULT 0 AFTER comision_porcentaje",
  'SELECT "pos_configuracion ya tiene comision_en_factura"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
