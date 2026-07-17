-- =============================================================
-- FAControl — DealerControl: inventario de vehículos
-- Script: 008_vehiculos.sql
-- Tier 5 (2026-07-17): el vehículo como ACTIVO. NACE en DealerControl.
-- AutoControl lo CONSUME por FK (crédito vehicular) — nunca se duplican
-- los datos del vehículo entre modos.
--
-- MIGRACION: para bases YA existentes. Las instalaciones nuevas reciben
-- lo mismo desde 001_create_schema.sql. Idempotente.
-- =============================================================

-- Fuerza UTF-8: mysql.exe asume la codificacion de la consola y corrompe los acentos.
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- vehiculo: unidad del inventario del dealer.
--   costo_total = costo_adquisicion + gastos_importacion
--   ganancia    = precio_venta - costo_total  (se calcula, no se guarda)
-- Soft delete vía deleted_at. Código secuencial V-0001 (contador 'vehiculo').
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS vehiculo (
  id                 BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo             VARCHAR(20)   NOT NULL,               -- V-0001
  vin                VARCHAR(17)   NULL,                   -- chasis / VIN
  marca              VARCHAR(50)   NOT NULL,
  modelo             VARCHAR(50)   NOT NULL,
  anio               SMALLINT UNSIGNED NULL,
  color              VARCHAR(30)   NULL,
  placa              VARCHAR(15)   NULL,                   -- matrícula / chapa
  tipo               ENUM('sedan','suv','jeepeta','camioneta','camion','motor','otro')
                       NOT NULL DEFAULT 'otro',
  kilometraje        INT UNSIGNED  NULL,
  costo_adquisicion  DECIMAL(15,2) NOT NULL DEFAULT 0.00,  -- lo que costó comprarlo
  gastos_importacion DECIMAL(15,2) NOT NULL DEFAULT 0.00,  -- aduana, flete, preparación
  precio_venta       DECIMAL(15,2) NOT NULL DEFAULT 0.00,  -- precio de lista
  estado             ENUM('disponible','reservado','vendido','alquilado','baja')
                       NOT NULL DEFAULT 'disponible',
  notas              TEXT          NULL,
  created_at         DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at         DATETIME      NULL,
  deleted_at         DATETIME      NULL,                   -- soft delete: leer con deleted_at IS NULL
  PRIMARY KEY (id),
  UNIQUE KEY uq_vehiculo_codigo (codigo),
  KEY ix_vehiculo_estado (estado),
  KEY ix_vehiculo_vin (vin)
) ENGINE=InnoDB;

-- Correlativo atómico para el código V-0001 (mismo patrón que recibo/prestamo).
INSERT INTO contador (nombre, valor) VALUES ('vehiculo', 0)
  ON DUPLICATE KEY UPDATE nombre = nombre;

-- -------------------------------------------------------------
-- Permisos del módulo Dealer (multicuentas). Admin los hereda por el
-- CROSS JOIN de 001; Supervisor gestiona inventario; Cobrador no ve Dealer.
-- -------------------------------------------------------------
INSERT INTO permiso (codigo, nombre, descripcion) VALUES
  ('vehiculos',        'Vehículos (ver)',          'Consulta del inventario de vehículos'),
  ('vehiculos_editar', 'Vehículos (crear/editar)', 'Alta, edición y baja de vehículos')
  ON DUPLICATE KEY UPDATE nombre = VALUES(nombre), descripcion = VALUES(descripcion);

-- Admin: ambos permisos.
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Admin' AND p.codigo IN ('vehiculos','vehiculos_editar');

-- Supervisor: ambos permisos (opera el inventario).
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Supervisor' AND p.codigo IN ('vehiculos','vehiculos_editar');

-- Propaga los permisos nuevos a los usuarios ya existentes de esos roles
-- (el trigger solo actúa al cambiar el rol de un usuario, no al crear permisos).
INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, rp.permiso_id
FROM usuario u
JOIN rol_permiso rp ON rp.rol_id = u.rol_id
JOIN permiso p ON p.id = rp.permiso_id
WHERE p.codigo IN ('vehiculos','vehiculos_editar');
