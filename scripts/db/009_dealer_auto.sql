-- =============================================================
-- FAControl — DealerControl (venta contado, rent a car, gastos) + AutoControl
-- Script: 009_dealer_auto.sql
-- Tier 5 (2026-07-17):
--   · venta_vehiculo   — venta al contado del dealer
--   · alquiler         — rent a car
--   · vehiculo_gasto   — gestión de importación (gastos detallados)
--   · prestamo.vehiculo_id — AutoControl: crédito con el vehículo en garantía.
--     Un crédito vehicular ES un prestamo con vehiculo_id (reusa cuotas, cobros,
--     pagaré, amortización). El vehículo nace en Dealer y AutoControl lo consume.
--
-- MIGRACION idempotente para bases YA existentes. Las nuevas lo reciben de 001.
-- =============================================================

SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- AutoControl: el préstamo puede tener un vehículo en garantía.
-- NULL = préstamo personal/hipotecario (PrestControl).
-- -------------------------------------------------------------
SET @existe_col := (SELECT COUNT(*) FROM information_schema.columns
                    WHERE table_schema = 'facontrol_db' AND table_name = 'prestamo'
                      AND column_name = 'vehiculo_id');
SET @sql := IF(@existe_col = 0,
  'ALTER TABLE prestamo ADD COLUMN vehiculo_id BIGINT UNSIGNED NULL AFTER cliente_id,
     ADD KEY ix_prestamo_vehiculo (vehiculo_id),
     ADD CONSTRAINT fk_prestamo_vehiculo FOREIGN KEY (vehiculo_id)
       REFERENCES vehiculo (id) ON DELETE RESTRICT',
  'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -------------------------------------------------------------
-- venta_vehiculo: venta al contado (Dealer). Al vender, el vehículo
-- pasa a estado 'vendido' (lo hace el Service en una transacción).
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS venta_vehiculo (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo      VARCHAR(20)   NOT NULL,              -- VC-0001
  vehiculo_id BIGINT UNSIGNED NOT NULL,
  cliente_id  BIGINT UNSIGNED NOT NULL,
  fecha_venta DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  precio      DECIMAL(15,2) NOT NULL,
  metodo_pago ENUM('efectivo','transferencia','cheque','otro') NOT NULL DEFAULT 'efectivo',
  notas       TEXT          NULL,
  created_at  DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by  BIGINT UNSIGNED NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_venta_codigo (codigo),
  KEY ix_venta_vehiculo (vehiculo_id),
  KEY ix_venta_cliente (cliente_id),
  CONSTRAINT fk_venta_vehiculo FOREIGN KEY (vehiculo_id) REFERENCES vehiculo (id) ON DELETE RESTRICT,
  CONSTRAINT fk_venta_cliente  FOREIGN KEY (cliente_id)  REFERENCES cliente (id)  ON DELETE RESTRICT,
  CONSTRAINT fk_venta_usuario  FOREIGN KEY (created_by)  REFERENCES usuario (id)  ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- alquiler: rent a car (Dealer). Activo mientras el vehículo está
-- rentado; al devolver, vuelve a 'disponible'.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS alquiler (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo           VARCHAR(20)   NOT NULL,          -- AL-0001
  vehiculo_id      BIGINT UNSIGNED NOT NULL,
  cliente_id       BIGINT UNSIGNED NOT NULL,
  fecha_inicio     DATE          NOT NULL,
  fecha_fin        DATE          NOT NULL,           -- devolución pactada
  fecha_devolucion DATE          NULL,               -- devolución real
  tarifa_dia       DECIMAL(15,2) NOT NULL,
  dias             INT UNSIGNED  NOT NULL,
  monto_total      DECIMAL(15,2) NOT NULL,
  estado           ENUM('activo','finalizado','cancelado') NOT NULL DEFAULT 'activo',
  notas            TEXT          NULL,
  created_at       DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by       BIGINT UNSIGNED NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_alquiler_codigo (codigo),
  KEY ix_alquiler_vehiculo (vehiculo_id),
  KEY ix_alquiler_estado (estado),
  CONSTRAINT fk_alquiler_vehiculo FOREIGN KEY (vehiculo_id) REFERENCES vehiculo (id) ON DELETE RESTRICT,
  CONSTRAINT fk_alquiler_cliente  FOREIGN KEY (cliente_id)  REFERENCES cliente (id)  ON DELETE RESTRICT,
  CONSTRAINT fk_alquiler_usuario  FOREIGN KEY (created_by)  REFERENCES usuario (id)  ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- vehiculo_gasto: gestión de importación (gastos detallados).
-- La suma se refleja en vehiculo.gastos_importacion (lo mantiene el Service).
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS vehiculo_gasto (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  vehiculo_id BIGINT UNSIGNED NOT NULL,
  concepto    VARCHAR(100)  NOT NULL,               -- 'Aduana', 'Flete', 'Grúa'...
  monto       DECIMAL(15,2) NOT NULL,
  fecha       DATE          NOT NULL,
  created_at  DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by  BIGINT UNSIGNED NULL,
  PRIMARY KEY (id),
  KEY ix_gasto_vehiculo (vehiculo_id),
  CONSTRAINT fk_gasto_vehiculo FOREIGN KEY (vehiculo_id) REFERENCES vehiculo (id) ON DELETE CASCADE,
  CONSTRAINT fk_gasto_usuario  FOREIGN KEY (created_by)  REFERENCES usuario (id)  ON DELETE SET NULL
) ENGINE=InnoDB;

-- Correlativos de venta al contado y alquiler.
INSERT INTO contador (nombre, valor) VALUES ('venta', 0), ('alquiler', 0)
  ON DUPLICATE KEY UPDATE nombre = nombre;
