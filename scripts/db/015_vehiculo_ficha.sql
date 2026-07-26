-- =============================================================
-- FAControl — Inventario ampliado del dealer
-- Script: 015_vehiculo_ficha.sql
-- Pedido del cliente (2026-07-25):
--  * vehiculo.matricula — número del certificado de matrícula (DGII), distinto
--    de la placa. Sale en la ficha y en el contrato de venta.
--  * vehiculo_reparacion — historial de reparaciones/mantenimientos del
--    vehículo con detalle preciso y costo; se muestra en la ficha.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001. Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='vehiculo' AND COLUMN_NAME='matricula');
SET @sql := IF(@existe=0,
  "ALTER TABLE vehiculo ADD COLUMN matricula VARCHAR(30) NULL AFTER placa",
  'SELECT "vehiculo.matricula ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

CREATE TABLE IF NOT EXISTS vehiculo_reparacion (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  vehiculo_id BIGINT UNSIGNED NOT NULL,
  fecha       DATE          NOT NULL,
  detalle     VARCHAR(500)  NOT NULL,
  costo       DECIMAL(15,2) NOT NULL DEFAULT 0,
  created_at  DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by  BIGINT UNSIGNED NULL,
  deleted_at  DATETIME      NULL,
  PRIMARY KEY (id),
  KEY ix_reparacion_vehiculo (vehiculo_id),
  CONSTRAINT fk_reparacion_vehiculo FOREIGN KEY (vehiculo_id)
    REFERENCES vehiculo (id) ON DELETE RESTRICT,
  CONSTRAINT fk_reparacion_usuario FOREIGN KEY (created_by)
    REFERENCES usuario (id) ON DELETE SET NULL
) ENGINE=InnoDB;
