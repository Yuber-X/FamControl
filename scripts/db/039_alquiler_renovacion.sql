-- =============================================================
-- FAControl — Renovacion de alquileres (rent a car)
-- Script: 039_alquiler_renovacion.sql
--
-- Pedido del cliente (2026-08-01):
--   "cuando se cumple el tiempo pactado del alquiler, el auto debe volver a
--    estar disponible o preguntar si el cliente seguira con el alquiler, esto
--    ultimo debe volver a programar la fecha nueva del auto en alquiler (habra
--    que actualizar su fecha de devolucion segun el usuario confirme la nueva
--    fecha y precio nuevo o el mismo)."
--
-- POR QUE UNA TABLA Y NO SOLO MOVER fecha_fin
-- Porque la tarifa puede cambiar en la renovacion. Si solo se corriera la
-- fecha, el monto del contrato (tarifa x dias) se recalcularia entero a la
-- tarifa nueva y le cambiaria el precio a dias que el cliente YA uso —y que
-- quizas ya pago—. Cada renovacion guarda SU tramo con SU tarifa; el contrato
-- vale la suma de los tramos.
--
-- Es la misma regla que rige el resto de la suite: lo ya cobrado no se toca.
--
-- QUE PASA CON alquiler.tarifa_dia
-- Queda con la tarifa ORIGINAL, la del primer tramo. La tarifa vigente es la
-- de la ultima renovacion (o la original si nunca se renovo). Guardarla asi
-- deja la historia completa sin columnas de mas.
--
-- alquiler.dias y alquiler.monto_total SI se actualizan: son el total pactado
-- del contrato, y todo lo que ya existe (cobros, reportes, exportacion) los lee
-- para saber cuanto hay que cobrar.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

CREATE TABLE IF NOT EXISTS alquiler_renovacion (
  id                 BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  alquiler_id        BIGINT UNSIGNED NOT NULL,
  -- Hasta cuando iba el contrato antes de esta renovacion, y hasta cuando va
  -- ahora. Con las dos fechas se reconstruye cada tramo sin ambiguedad.
  fecha_fin_anterior DATE          NOT NULL,
  fecha_fin_nueva    DATE          NOT NULL,
  -- Tarifa de ESTE tramo. Puede ser la misma de antes o una nueva.
  tarifa_dia         DECIMAL(15,2) NOT NULL,
  dias               INT UNSIGNED  NOT NULL,
  monto              DECIMAL(15,2) NOT NULL,
  notas              VARCHAR(250)  NULL,
  created_at         DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by         BIGINT UNSIGNED NULL,
  PRIMARY KEY (id),
  KEY ix_alquiler_renovacion_alquiler (alquiler_id, id),
  CONSTRAINT fk_alquiler_renovacion_alquiler FOREIGN KEY (alquiler_id)
    REFERENCES alquiler (id) ON DELETE RESTRICT,
  CONSTRAINT fk_alquiler_renovacion_usuario FOREIGN KEY (created_by)
    REFERENCES usuario (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
