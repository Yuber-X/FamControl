-- =============================================================
-- FAControl — Esquema inicial
-- Script: 001_create_schema.sql
-- Motor: MySQL 8.0+ · InnoDB · utf8mb4_unicode_ci
-- Regla: dinero en DECIMAL(15,2), fechas DATETIME en UTC
-- =============================================================

-- Fuerza UTF-8: mysql.exe asume la codificacion de la consola y corrompe los acentos.
SET NAMES utf8mb4;
CREATE DATABASE IF NOT EXISTS facontrol_db
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE facontrol_db;

-- -------------------------------------------------------------
-- rol: catálogo (Admin / Supervisor / Cobrador)
-- Multicuentas — pedido del cliente 2026-07-16.
-- -------------------------------------------------------------
CREATE TABLE rol (
  id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
  nombre      VARCHAR(50)  NOT NULL,
  descripcion VARCHAR(200) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_rol_nombre (nombre)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- permiso: catálogo por módulo/acción
-- -------------------------------------------------------------
CREATE TABLE permiso (
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
CREATE TABLE rol_permiso (
  rol_id     INT UNSIGNED NOT NULL,
  permiso_id INT UNSIGNED NOT NULL,
  PRIMARY KEY (rol_id, permiso_id),
  CONSTRAINT fk_rolperm_rol FOREIGN KEY (rol_id)
    REFERENCES rol (id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_rolperm_permiso FOREIGN KEY (permiso_id)
    REFERENCES permiso (id) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- usuario: empleados del negocio (MULTIUSUARIO desde 2026-07-16)
-- -------------------------------------------------------------
CREATE TABLE usuario (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  username      VARCHAR(50)  NOT NULL,
  password_hash VARCHAR(100) NOT NULL,           -- BCrypt cost 12
  nombre        VARCHAR(100) NOT NULL,
  apellido      VARCHAR(100) NULL,
  rol_id        INT UNSIGNED NULL,
  activo        TINYINT(1)   NOT NULL DEFAULT 1,
  created_at    DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at    DATETIME     NULL,
  last_login_at DATETIME     NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_usuario_username (username),
  CONSTRAINT fk_usuario_rol FOREIGN KEY (rol_id)
    REFERENCES rol (id) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- usuario_permiso: permisos EFECTIVOS por usuario.
-- Los triggers los siembran desde rol_permiso; el Admin los ajusta
-- uno por uno (overrides) sin tocar el rol.
-- -------------------------------------------------------------
CREATE TABLE usuario_permiso (
  usuario_id BIGINT UNSIGNED NOT NULL,
  permiso_id INT UNSIGNED    NOT NULL,
  PRIMARY KEY (usuario_id, permiso_id),
  CONSTRAINT fk_usuperm_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_usuperm_permiso FOREIGN KEY (permiso_id)
    REFERENCES permiso (id) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- sesion: registro de logins/logouts
-- -------------------------------------------------------------
CREATE TABLE sesion (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  usuario_id BIGINT UNSIGNED NOT NULL,
  login_at   DATETIME    NOT NULL DEFAULT (UTC_TIMESTAMP()),
  logout_at  DATETIME    NULL,
  ip_local   VARCHAR(45) NULL,
  PRIMARY KEY (id),
  KEY ix_sesion_usuario (usuario_id),
  CONSTRAINT fk_sesion_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- cliente: personas a las que se les presta (soft delete)
-- -------------------------------------------------------------
CREATE TABLE cliente (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  cedula     VARCHAR(13)  NOT NULL,               -- formato 001-1234567-8
  nombre     VARCHAR(100) NOT NULL,
  apellido   VARCHAR(100) NOT NULL,
  telefono   VARCHAR(20)  NULL,
  direccion  VARCHAR(255) NULL,
  email      VARCHAR(150) NULL,
  notas      TEXT         NULL,
  created_at DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at DATETIME     NULL,
  deleted_at DATETIME     NULL,                   -- soft delete: leer con deleted_at IS NULL
  PRIMARY KEY (id),
  UNIQUE KEY uq_cliente_cedula (cedula),
  KEY ix_cliente_nombre (nombre, apellido)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- prestamo: contrato de préstamo
-- codigo: correlativo visible tipo P-0001 (mockup)
-- tasa_interes: tasa MENSUAL en % (convención prestamista RD);
--   se convierte a tasa por período según modalidad al calcular
-- -------------------------------------------------------------
CREATE TABLE prestamo (
  id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo              VARCHAR(10)   NOT NULL,     -- P-0001
  cliente_id          BIGINT UNSIGNED NOT NULL,
  monto_capital       DECIMAL(15,2) NOT NULL,
  moneda              CHAR(3)       NOT NULL DEFAULT 'DOP',
  tasa_interes        DECIMAL(8,4)  NOT NULL,     -- % mensual, ej. 10.0000
  plazo_cuotas        INT UNSIGNED  NOT NULL,
  modalidad           ENUM('diaria','semanal','quincenal','mensual','pago_unico') NOT NULL,
  metodo_amortizacion ENUM('frances','cuota_fija') NOT NULL DEFAULT 'cuota_fija',
  fecha_inicio        DATE          NOT NULL,     -- fecha del primer pago (hora local del negocio)
  garantia            VARCHAR(255)  NULL,
  estado              ENUM('activo','pagado','cancelado') NOT NULL DEFAULT 'activo',
  notas               TEXT          NULL,
  created_at          DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at          DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_prestamo_codigo (codigo),
  KEY ix_prestamo_cliente (cliente_id),
  KEY ix_prestamo_estado (estado),
  CONSTRAINT fk_prestamo_cliente FOREIGN KEY (cliente_id)
    REFERENCES cliente (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- cuota: cada cuota individual del préstamo
-- Nota: se agrega 'cancelada' al ENUM porque cancelar un préstamo
-- marca sus cuotas restantes como canceladas (regla §8.4 CLAUDE.md
-- del proyecto) sin borrarlas jamás.
-- -------------------------------------------------------------
CREATE TABLE cuota (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  prestamo_id       BIGINT UNSIGNED NOT NULL,
  numero_cuota      INT UNSIGNED  NOT NULL,
  fecha_vencimiento DATE          NOT NULL,
  capital           DECIMAL(15,2) NOT NULL,
  interes           DECIMAL(15,2) NOT NULL,
  monto_total       DECIMAL(15,2) NOT NULL,
  saldo_despues     DECIMAL(15,2) NOT NULL,       -- saldo de capital tras pagar esta cuota
  monto_pagado      DECIMAL(15,2) NOT NULL DEFAULT 0.00, -- acumulado de abonos aplicados
  estado            ENUM('pendiente','pagada','vencida','en_mora','cancelada') NOT NULL DEFAULT 'pendiente',
  created_at        DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at        DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_cuota_prestamo_numero (prestamo_id, numero_cuota),
  KEY ix_cuota_vencimiento (fecha_vencimiento, estado),
  CONSTRAINT fk_cuota_prestamo FOREIGN KEY (prestamo_id)
    REFERENCES prestamo (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pago: abono a una cuota (soft delete; un pago NUNCA se modifica,
-- errores se corrigen con pago compensatorio negativo)
-- monto_interes/monto_capital: desglose del abono (primero interés,
-- luego capital) — necesario para abonos parciales
-- -------------------------------------------------------------
CREATE TABLE pago (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  cuota_id      BIGINT UNSIGNED NOT NULL,
  numero_recibo VARCHAR(12)   NOT NULL,           -- R-000001, secuencial atómico, nunca se reutiliza
  fecha_pago    DATETIME      NOT NULL,           -- UTC
  monto_pagado  DECIMAL(15,2) NOT NULL,
  monto_interes DECIMAL(15,2) NOT NULL DEFAULT 0.00,
  monto_capital DECIMAL(15,2) NOT NULL DEFAULT 0.00,
  metodo_pago   ENUM('efectivo','transferencia','cheque','otro') NOT NULL DEFAULT 'efectivo',
  notas         TEXT          NULL,
  created_by    BIGINT UNSIGNED NULL,             -- quién cobró (para el reporte por usuario)
  created_at    DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at    DATETIME      NULL,
  deleted_at    DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_pago_recibo (numero_recibo),
  KEY ix_pago_cuota (cuota_id),
  KEY ix_pago_fecha (fecha_pago),
  CONSTRAINT fk_pago_cuota FOREIGN KEY (cuota_id)
    REFERENCES cuota (id) ON DELETE RESTRICT,
  CONSTRAINT fk_pago_usuario FOREIGN KEY (created_by)
    REFERENCES usuario (id) ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- auditoria: log inmutable de operaciones (nunca se borra)
-- -------------------------------------------------------------
CREATE TABLE auditoria (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  usuario_id  BIGINT UNSIGNED NOT NULL,
  entidad     VARCHAR(50)  NOT NULL,              -- 'cliente', 'prestamo', 'cuota', 'pago', 'usuario'
  entidad_id  BIGINT UNSIGNED NULL,
  accion      ENUM('crear','modificar','eliminar','consultar','login','logout') NOT NULL,
  descripcion TEXT         NULL,
  ip_local    VARCHAR(45)  NULL,
  timestamp   DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  PRIMARY KEY (id),
  KEY ix_auditoria_entidad (entidad, entidad_id),
  KEY ix_auditoria_timestamp (timestamp),
  CONSTRAINT fk_auditoria_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- contador: correlativos atómicos (numero_recibo, codigo prestamo)
-- Uso: SELECT valor FROM contador WHERE nombre=? FOR UPDATE;
--      UPDATE contador SET valor = valor + 1 ...  (misma transacción)
-- -------------------------------------------------------------
CREATE TABLE contador (
  nombre VARCHAR(30)     NOT NULL,
  valor  BIGINT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (nombre)
) ENGINE=InnoDB;

INSERT INTO contador (nombre, valor) VALUES
  ('recibo', 0),
  ('prestamo', 0),
  ('vehiculo', 0);

-- -------------------------------------------------------------
-- vehiculo: inventario del dealer (DealerControl — Tier 5).
-- El vehículo como ACTIVO: nace aquí; AutoControl lo consume por FK.
--   costo_total = costo_adquisicion + gastos_importacion
--   ganancia    = precio_venta - costo_total  (se calcula, no se guarda)
-- Soft delete vía deleted_at. Código secuencial V-0001 (contador 'vehiculo').
-- -------------------------------------------------------------
CREATE TABLE vehiculo (
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
  deleted_at         DATETIME      NULL,                   -- soft delete
  PRIMARY KEY (id),
  UNIQUE KEY uq_vehiculo_codigo (codigo),
  KEY ix_vehiculo_estado (estado),
  KEY ix_vehiculo_vin (vin)
) ENGINE=InnoDB;

-- =============================================================
-- Catálogo de roles y permisos (multicuentas — cliente 2026-07-16)
-- Va acá y no en el seed porque NO son datos de prueba: sin esto la
-- aplicación no puede autenticar a nadie.
-- =============================================================
INSERT INTO rol (nombre, descripcion) VALUES
  ('Admin',      'Control total: usuarios, configuración y autorización de préstamos'),
  ('Supervisor', 'Opera y supervisa la cartera, sin administrar usuarios ni configuración'),
  ('Cobrador',   'Cobra en la calle: registra pagos y consulta su cartera');

INSERT INTO permiso (codigo, nombre, descripcion) VALUES
  ('panel',               'Panel',                     'KPIs de la cartera'),
  ('clientes',            'Clientes (ver)',            'Consulta de clientes'),
  ('clientes_editar',     'Clientes (crear/editar)',   'Alta, edición y baja de clientes'),
  ('prestamos',           'Préstamos (ver)',           'Consulta de préstamos y su amortización'),
  ('prestamos_crear',     'Préstamos (crear)',         'Crear préstamos nuevos'),
  ('prestamos_autorizar', 'Autorizar préstamos',       'Aprobar préstamos nuevos'),
  ('prestamos_cancelar',  'Cancelar préstamos',        'Permiso especial: cancelación con auditoría'),
  ('cobros',              'Cobros',                    'Registrar pagos y emitir recibos'),
  ('reportes',            'Reportes',                  'Reportes por fecha y por cliente'),
  ('historial',           'Historial',                 'Auditoría de operaciones'),
  ('usuarios',            'Admin de usuarios',         'CRUD de usuarios, roles y overrides'),
  ('configuracion',       'Configuración',             'EXCLUSIVO Admin'),
  ('vehiculos',           'Vehículos (ver)',           'Consulta del inventario de vehículos (DealerControl)'),
  ('vehiculos_editar',    'Vehículos (crear/editar)',  'Alta, edición y baja de vehículos');

-- Admin: todo
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Admin';

-- Supervisor: toda la operación, sin usuarios/configuración ni autorizar
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Supervisor'
  AND p.codigo IN ('panel','clientes','clientes_editar','prestamos','prestamos_crear',
                   'prestamos_cancelar','cobros','reportes','historial',
                   'vehiculos','vehiculos_editar');

-- Cobrador: cobra, consulta y SI crea prestamos, pero cada uno necesita
-- la autorizacion de un admin (prestamos_autorizar). Sin prestamos_crear
-- no podria ni abrir la pantalla y el flujo de autorizacion nunca correria.
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Cobrador'
  AND p.codigo IN ('panel','clientes','prestamos','prestamos_crear','cobros');

-- =============================================================
-- TRIGGERS: sincronizan usuario_permiso con el rol (patrón POS-400/POS-500).
--
-- OJO: los marcadores "DELIMITER $$" y el separador "$$" NO son decoración
-- ni sirven solo para mysql.exe. El protocolo de MySQL rechaza DELIMITER,
-- asi que VerificadorBaseDatos.ObtenerBloquesEjecutables() parte esta zona
-- y manda cada trigger como comando independiente. No reformatear a mano.
-- =============================================================

DELIMITER $$

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
