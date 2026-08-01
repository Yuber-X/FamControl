-- =============================================================
-- FAControl — Cobros de alquiler
-- Script: 034_alquiler_pagos.sql
--
-- Pedido del cliente (2026-07-31):
--   "Alquileres > Detalle del alquiler > se necesita un grid y su propia forma
--    de registrar cobros (parecido al 'financiamiento de venta')."
--
-- POR QUE HACIA FALTA
-- Hasta hoy el alquiler se cobraba de una sola vez, al cerrarlo: el sistema
-- sabia cuanto correspondia pero no si el cliente ya habia entregado algo. En
-- la practica los alquileres se pagan con adelanto al retirar el vehiculo y el
-- resto al devolverlo, y eso no tenia donde anotarse.
--
-- TABLA PROPIA, NO REUSAR LA DE LAS VENTAS
-- venta_plazo_pago cuelga de un PLAZO, y un alquiler no tiene plazos: tiene un
-- monto y abonos contra ese monto. Meterlos ahi obligaria a inventar plazos
-- falsos y a mezclar los datos de dos negocios distintos, que es justo lo que
-- el cliente pidio no hacer.
--
-- NUMERACION PROPIA (RA-000001)
-- Su propio contador, separado del de prestamos (R-) y del de ventas (RV-):
-- son tres talonarios distintos y cada uno tiene que poder rendirse por
-- separado. Un recibo entregado NUNCA se reusa, aunque el cobro se anule.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

CREATE TABLE IF NOT EXISTS alquiler_pago (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  alquiler_id    BIGINT UNSIGNED NOT NULL,
  numero_recibo  VARCHAR(20)   NOT NULL,
  fecha_pago     DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  monto          DECIMAL(15,2) NOT NULL,
  metodo_pago    ENUM('efectivo','transferencia','cheque','otro') NOT NULL DEFAULT 'efectivo',
  notas          TEXT          NULL,
  created_at     DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by     BIGINT UNSIGNED NULL,
  -- Soft delete: un cobro anulado deja su ficha y su numero de recibo, que NO
  -- se reusa. Es la misma regla que en prestamos y ventas.
  deleted_at     DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_alquiler_pago_recibo (numero_recibo),
  KEY ix_alquiler_pago_alquiler (alquiler_id),
  CONSTRAINT fk_alquiler_pago_alquiler FOREIGN KEY (alquiler_id)
    REFERENCES alquiler (id) ON DELETE RESTRICT,
  CONSTRAINT fk_alquiler_pago_usuario FOREIGN KEY (created_by)
    REFERENCES usuario (id) ON DELETE SET NULL
) ENGINE=InnoDB;

-- Contador del talonario de alquileres. INSERT IGNORE: si ya existe, se deja
-- como esta — pisarlo en cero repetiria numeros ya entregados.
INSERT IGNORE INTO contador (nombre, valor) VALUES ('recibo_alquiler', 0);
