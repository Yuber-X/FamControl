-- =============================================================
-- FAControl — Esquema inicial
-- Script: 001_create_schema.sql
-- Motor: MySQL 8.0+ · InnoDB · utf8mb4_unicode_ci
-- Regla: dinero en DECIMAL(15,2), fechas DATETIME en UTC
-- =============================================================

CREATE DATABASE IF NOT EXISTS facontrol_db
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE facontrol_db;

-- -------------------------------------------------------------
-- usuario: cuenta única del prestamista (sistema mono-usuario)
-- -------------------------------------------------------------
CREATE TABLE usuario (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  username      VARCHAR(50)  NOT NULL,
  password_hash VARCHAR(100) NOT NULL,           -- BCrypt cost 12
  nombre        VARCHAR(100) NOT NULL,
  activo        TINYINT(1)   NOT NULL DEFAULT 1,
  created_at    DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  last_login_at DATETIME     NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_usuario_username (username)
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
  modalidad           ENUM('diaria','semanal','quincenal','mensual') NOT NULL,
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
  created_at    DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at    DATETIME      NULL,
  deleted_at    DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_pago_recibo (numero_recibo),
  KEY ix_pago_cuota (cuota_id),
  KEY ix_pago_fecha (fecha_pago),
  CONSTRAINT fk_pago_cuota FOREIGN KEY (cuota_id)
    REFERENCES cuota (id) ON DELETE RESTRICT
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
  ('prestamo', 0);
