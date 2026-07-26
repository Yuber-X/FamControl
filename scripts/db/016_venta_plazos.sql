-- =============================================================
-- FAControl — Financiamiento por plazos del dealer
-- Script: 016_venta_plazos.sql
-- Pedido del cliente (2026-07-25): "financiamiento > por plazos", con
-- "Total por pagar > lo pendiente > cantidad de plazos > lo pagado",
-- carta de compromiso y recibo de separación.
--
-- PRÁCTICA DEL DEALER RD (expediente real "DATOS Y CONDICIONES DE VENTAS"):
-- inicial/anticipo + N pagos pactados, SIN interés (el interés vive en
-- AutoControl, que es un préstamo de verdad con pagaré y amortización).
-- Acá el plan es un calendario de pagos simple del propio dealer.
--
-- DISEÑO:
--  * venta_vehiculo.tipo_venta — 'contado' | 'plazos' | 'separacion'
--  * venta_vehiculo.inicial     — anticipo/inicial recibido al firmar
--  * venta_vehiculo.fecha_limite — separación: vence a los N días (default 15,
--    "si el plazo se vence, debe ser límite 15 días, tiene derecho")
--  * venta_plazo — un renglón por plazo pactado (número, vencimiento, monto,
--    monto_pagado). Lo pagado se acumula ahí; el pendiente se deriva.
--  * venta_plazo_pago — abonos con recibo, para el historial y la auditoría.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001. Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 1. Columnas nuevas de venta_vehiculo
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='venta_vehiculo' AND COLUMN_NAME='tipo_venta');
SET @sql := IF(@existe=0,
  "ALTER TABLE venta_vehiculo ADD COLUMN tipo_venta ENUM('contado','plazos','separacion') NOT NULL DEFAULT 'contado' AFTER precio",
  'SELECT "venta_vehiculo.tipo_venta ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='venta_vehiculo' AND COLUMN_NAME='inicial');
SET @sql := IF(@existe=0,
  "ALTER TABLE venta_vehiculo ADD COLUMN inicial DECIMAL(15,2) NOT NULL DEFAULT 0.00 AFTER tipo_venta",
  'SELECT "venta_vehiculo.inicial ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='venta_vehiculo' AND COLUMN_NAME='fecha_limite');
SET @sql := IF(@existe=0,
  "ALTER TABLE venta_vehiculo ADD COLUMN fecha_limite DATE NULL AFTER inicial",
  'SELECT "venta_vehiculo.fecha_limite ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 2. venta_plazo: calendario de pagos pactado
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS venta_plazo (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  venta_id          BIGINT UNSIGNED NOT NULL,
  numero            INT UNSIGNED  NOT NULL,
  fecha_vencimiento DATE          NOT NULL,
  monto             DECIMAL(15,2) NOT NULL,
  monto_pagado      DECIMAL(15,2) NOT NULL DEFAULT 0.00,
  estado            ENUM('pendiente','pagado','cancelado') NOT NULL DEFAULT 'pendiente',
  created_at        DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at        DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_venta_plazo (venta_id, numero),
  CONSTRAINT fk_plazo_venta FOREIGN KEY (venta_id)
    REFERENCES venta_vehiculo (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- 3. venta_plazo_pago: abonos a los plazos (con recibo)
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS venta_plazo_pago (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  plazo_id       BIGINT UNSIGNED NOT NULL,
  numero_recibo  VARCHAR(20)   NOT NULL,
  fecha_pago     DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  monto          DECIMAL(15,2) NOT NULL,
  metodo_pago    ENUM('efectivo','transferencia','cheque','otro') NOT NULL DEFAULT 'efectivo',
  notas          TEXT          NULL,
  created_at     DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by     BIGINT UNSIGNED NULL,
  deleted_at     DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_plazo_pago_recibo (numero_recibo),
  KEY ix_plazo_pago_plazo (plazo_id),
  CONSTRAINT fk_plazo_pago_plazo   FOREIGN KEY (plazo_id)   REFERENCES venta_plazo (id) ON DELETE RESTRICT,
  CONSTRAINT fk_plazo_pago_usuario FOREIGN KEY (created_by) REFERENCES usuario (id)     ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- 4. Contador del recibo de plazos (RV-000001)
-- -------------------------------------------------------------
INSERT IGNORE INTO contador (nombre, valor) VALUES ('recibo_venta', 0);
