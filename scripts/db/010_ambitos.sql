-- =============================================================
-- FAControl — Aislamiento por estancia (ámbito) + acceso por modo
-- Script: 010_ambitos.sql
-- Pedido de Yuber (2026-07-18): los datos de PrestControl no deben
-- mezclarse con los de Dealer/Auto (son estancias de trabajo distintas)
-- y un usuario que no sea Admin solo debe acceder a los modos que el
-- Admin le habilite (gestionado desde "permisos").
--
-- Decisión de negocio (confirmada): 3 dominios AISLADOS de clientes
-- (prestcontrol / dealercontrol / autocontrol) y cédula única POR
-- dominio, no global. Vehículo sigue compartido Dealer→Auto por FK.
--
-- MIGRACION para bases YA existentes. Las instalaciones nuevas reciben
-- lo mismo desde 001_create_schema.sql. Idempotente.
-- =============================================================

SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 1. cliente.ambito: a qué estancia pertenece cada ficha.
--    MySQL 8 no tiene "ADD COLUMN IF NOT EXISTS": se consulta antes.
--    Los clientes ya existentes son de PrestControl (única estancia
--    previa) — el DEFAULT los cubre.
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'cliente'
                  AND COLUMN_NAME = 'ambito');
SET @sql := IF(@existe = 0,
  "ALTER TABLE cliente
     ADD COLUMN ambito ENUM('prestcontrol','dealercontrol','autocontrol')
       NOT NULL DEFAULT 'prestcontrol' AFTER id",
  'SELECT "cliente.ambito ya existe"');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- Backfill explícito por si alguna fila quedara con valor vacío.
UPDATE cliente SET ambito = 'prestcontrol' WHERE ambito IS NULL OR ambito = '';

-- -------------------------------------------------------------
-- 2. Unicidad de cédula: pasa de GLOBAL a POR ámbito.
--    La misma persona puede tener ficha independiente en dos estancias.
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'cliente'
                  AND INDEX_NAME = 'uq_cliente_cedula');
SET @sql := IF(@existe > 0,
  'ALTER TABLE cliente DROP INDEX uq_cliente_cedula',
  'SELECT "uq_cliente_cedula ya fue removido"');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'cliente'
                  AND INDEX_NAME = 'uq_cliente_ambito_cedula');
SET @sql := IF(@existe = 0,
  'ALTER TABLE cliente ADD UNIQUE KEY uq_cliente_ambito_cedula (ambito, cedula)',
  'SELECT "uq_cliente_ambito_cedula ya existe"');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -------------------------------------------------------------
-- 3. Permisos de ACCESO por modo (gestionados desde la pantalla de
--    Usuarios, como cualquier otro permiso). El Admin entra a todo
--    sin necesitarlos; estos gobiernan a los demás roles.
-- -------------------------------------------------------------
INSERT IGNORE INTO permiso (codigo, nombre, descripcion) VALUES
  ('acceso_prestcontrol',  'Acceso a PrestControl',
     'Puede entrar a la estancia de préstamos personales'),
  ('acceso_dealercontrol', 'Acceso a DealControl',
     'Puede entrar a la estancia de inventario, ventas y alquiler de vehículos'),
  ('acceso_autocontrol',   'Acceso a AutoControl',
     'Puede entrar a la estancia de ventas financiadas de vehículos');

-- -------------------------------------------------------------
-- 4. rol_permiso: defaults por rol.
--    Admin: los tres.  Supervisor: los tres (supervisa todo).
--    Cobrador: solo PrestControl (el Admin le habilita más si aplica).
-- -------------------------------------------------------------
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre IN ('Admin', 'Supervisor')
  AND p.codigo IN ('acceso_prestcontrol','acceso_dealercontrol','acceso_autocontrol');

INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Cobrador'
  AND p.codigo = 'acceso_prestcontrol';

-- -------------------------------------------------------------
-- 5. Backfill de usuario_permiso para los usuarios YA existentes.
--    Los triggers siembran usuario_permiso al crear/cambiar el rol, pero
--    estos permisos no existían cuando se sembraron los usuarios previos:
--    hay que darles el acceso según su rol actual.
-- -------------------------------------------------------------
INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, rp.permiso_id
FROM usuario u
JOIN rol_permiso rp ON rp.rol_id = u.rol_id
JOIN permiso p ON p.id = rp.permiso_id
WHERE p.codigo IN ('acceso_prestcontrol','acceso_dealercontrol','acceso_autocontrol');
