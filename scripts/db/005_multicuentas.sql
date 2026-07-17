-- =============================================================
-- FAControl — Multicuentas: roles y permisos
-- Script: 005_multicuentas.sql
-- Pedido del cliente 2026-07-16: "Multicuentas (Añadir roles y
-- funciones de la misma, parecido a POS-500 pero orientado a
-- prestamistas [parecen que tendran personal para su trabajo])".
--
-- MIGRACION: para bases YA existentes. Las instalaciones nuevas
-- reciben lo mismo desde 001_create_schema.sql.
-- Idempotente: se puede ejecutar dos veces sin romper nada.
-- =============================================================

-- Fuerza UTF-8: mysql.exe asume la codificacion de la consola y corrompe los acentos.
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- rol: catálogo (Admin / Supervisor / Cobrador)
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS rol (
  id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
  nombre      VARCHAR(50)  NOT NULL,
  descripcion VARCHAR(200) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_rol_nombre (nombre)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- permiso: catálogo por módulo/acción
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS permiso (
  id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo      VARCHAR(50)  NOT NULL,             -- ej: 'prestamos_crear'
  nombre      VARCHAR(100) NOT NULL,
  descripcion VARCHAR(200) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_permiso_codigo (codigo)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- rol_permiso: qué otorga cada rol (los defaults por rol)
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS rol_permiso (
  rol_id     INT UNSIGNED NOT NULL,
  permiso_id INT UNSIGNED NOT NULL,
  PRIMARY KEY (rol_id, permiso_id),
  CONSTRAINT fk_rolperm_rol FOREIGN KEY (rol_id)
    REFERENCES rol (id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_rolperm_permiso FOREIGN KEY (permiso_id)
    REFERENCES permiso (id) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- usuario_permiso: permisos EFECTIVOS por usuario.
-- Los triggers los siembran desde rol_permiso al crear el usuario
-- o al cambiarle el rol; el Admin puede ajustarlos uno por uno
-- (overrides) sin tocar el rol.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS usuario_permiso (
  usuario_id BIGINT UNSIGNED NOT NULL,
  permiso_id INT UNSIGNED    NOT NULL,
  PRIMARY KEY (usuario_id, permiso_id),
  CONSTRAINT fk_usuperm_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_usuperm_permiso FOREIGN KEY (permiso_id)
    REFERENCES permiso (id) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- usuario: columnas nuevas (rol y apellido)
-- MySQL 8 no tiene "ADD COLUMN IF NOT EXISTS": se consulta antes.
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = 'facontrol_db' AND TABLE_NAME = 'usuario'
                  AND COLUMN_NAME = 'rol_id');
SET @sql := IF(@existe = 0,
  'ALTER TABLE usuario ADD COLUMN rol_id INT UNSIGNED NULL AFTER nombre',
  'SELECT "rol_id ya existe"');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = 'facontrol_db' AND TABLE_NAME = 'usuario'
                  AND COLUMN_NAME = 'apellido');
SET @sql := IF(@existe = 0,
  'ALTER TABLE usuario ADD COLUMN apellido VARCHAR(100) NULL AFTER nombre',
  'SELECT "apellido ya existe"');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = 'facontrol_db' AND TABLE_NAME = 'usuario'
                  AND COLUMN_NAME = 'updated_at');
SET @sql := IF(@existe = 0,
  'ALTER TABLE usuario ADD COLUMN updated_at DATETIME NULL',
  'SELECT "updated_at ya existe"');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @existe := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
                WHERE TABLE_SCHEMA = 'facontrol_db' AND TABLE_NAME = 'usuario'
                  AND CONSTRAINT_NAME = 'fk_usuario_rol');
SET @sql := IF(@existe = 0,
  'ALTER TABLE usuario ADD CONSTRAINT fk_usuario_rol FOREIGN KEY (rol_id)
     REFERENCES rol (id) ON DELETE SET NULL ON UPDATE CASCADE',
  'SELECT "fk_usuario_rol ya existe"');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -------------------------------------------------------------
-- Catálogo de roles y permisos (orientado a prestamistas)
-- -------------------------------------------------------------
INSERT IGNORE INTO rol (nombre, descripcion) VALUES
  ('Admin',      'Control total: usuarios, configuración y autorización de préstamos'),
  ('Supervisor', 'Opera y supervisa la cartera, sin administrar usuarios ni configuración'),
  ('Cobrador',   'Cobra en la calle: registra pagos y consulta su cartera');

INSERT IGNORE INTO permiso (codigo, nombre, descripcion) VALUES
  ('panel',               'Panel',                     'KPIs de la cartera'),
  ('clientes',            'Clientes (ver)',            'Consulta de clientes'),
  ('clientes_editar',     'Clientes (crear/editar)',   'Alta, edición y baja de clientes'),
  ('prestamos',           'Préstamos (ver)',           'Consulta de préstamos y su amortización'),
  ('prestamos_crear',     'Préstamos (crear)',         'Crear préstamos nuevos'),
  ('prestamos_autorizar', 'Autorizar préstamos',       'Aprobar préstamos nuevos (regla del cliente 2026-07-16)'),
  ('prestamos_cancelar',  'Cancelar préstamos',        'Permiso especial: cancelación con auditoría'),
  ('cobros',              'Cobros',                    'Registrar pagos y emitir recibos'),
  ('reportes',            'Reportes',                  'Reportes por fecha y por cliente'),
  ('historial',           'Historial',                 'Auditoría de operaciones'),
  ('usuarios',            'Admin de usuarios',         'CRUD de usuarios, roles y overrides'),
  ('configuracion',       'Configuración',             'EXCLUSIVO Admin');

-- Admin: todo
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Admin';

-- Supervisor: toda la operación, sin usuarios/configuración ni autorizar
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Supervisor'
  AND p.codigo IN ('panel','clientes','clientes_editar','prestamos','prestamos_crear',
                   'prestamos_cancelar','cobros','reportes','historial');

-- Cobrador: cobra, consulta y SI crea prestamos, pero cada uno necesita
-- la autorizacion de un admin (prestamos_autorizar). Sin prestamos_crear
-- no podria ni abrir la pantalla y el flujo de autorizacion nunca correria.
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Cobrador'
  AND p.codigo IN ('panel','clientes','prestamos','prestamos_crear','cobros');

-- -------------------------------------------------------------
-- Usuarios ya existentes (los de la instalación mono-usuario):
-- pasan a ser Admin, para que nadie se quede afuera al migrar.
-- -------------------------------------------------------------
UPDATE usuario SET rol_id = (SELECT id FROM rol WHERE nombre = 'Admin')
WHERE rol_id IS NULL;

-- Siembra los permisos efectivos de los usuarios ya existentes
INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, rp.permiso_id
FROM usuario u
JOIN rol_permiso rp ON rp.rol_id = u.rol_id;

-- =============================================================
-- TRIGGERS: sincronizan usuario_permiso con el rol (patrón POS-400/POS-500).
-- Los overrides manuales del Admin se conservan MIENTRAS no cambie el rol;
-- al cambiar de rol se resiembra desde cero.
--
-- OJO: los marcadores "-- @bloque" no son decoración. El protocolo de MySQL
-- rechaza DELIMITER, asi que VerificadorBaseDatos parte el archivo por esos
-- marcadores y manda cada trigger como sentencia independiente.
-- =============================================================

DROP TRIGGER IF EXISTS trg_usuario_after_insert;
DROP TRIGGER IF EXISTS trg_usuario_after_update;

DELIMITER $$

-- @bloque
CREATE TRIGGER trg_usuario_after_insert
AFTER INSERT ON usuario
FOR EACH ROW
BEGIN
  IF NEW.rol_id IS NOT NULL THEN
    INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
    SELECT NEW.id, rp.permiso_id
    FROM rol_permiso rp
    WHERE rp.rol_id = NEW.rol_id;
  END IF;
END$$

-- @bloque
CREATE TRIGGER trg_usuario_after_update
AFTER UPDATE ON usuario
FOR EACH ROW
BEGIN
  IF (OLD.rol_id IS NULL AND NEW.rol_id IS NOT NULL)
     OR (OLD.rol_id IS NOT NULL AND NEW.rol_id IS NULL)
     OR (OLD.rol_id <> NEW.rol_id) THEN
    DELETE FROM usuario_permiso WHERE usuario_id = NEW.id;
    IF NEW.rol_id IS NOT NULL THEN
      INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
      SELECT NEW.id, rp.permiso_id
      FROM rol_permiso rp
      WHERE rp.rol_id = NEW.rol_id;
    END IF;
  END IF;
END$$

DELIMITER ;
