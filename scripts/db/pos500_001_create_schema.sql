-- =============================================================
-- FAControl — Esquema del punto de venta (POS-500)
-- Script: pos500_001_create_schema.sql
-- Base:   pos500_db  ·  InnoDB · utf8mb4_unicode_ci
--
-- POR QUÉ UNA BASE APARTE (decisión con Yuber, 2026-07-30): el POS-500 se vende
-- por separado de FAControl. Con sus datos en su propia base, el día que un
-- cliente compre solo el punto de venta se le lleva `pos500_db` y listo, sin
-- tener que desenredar tablas de préstamos y vehículos.
--
-- QUÉ **NO** ESTÁ ACÁ, a propósito:
--   usuario · rol · permiso · rol_permiso · usuario_permiso · sesion · auditoria
-- Todo eso vive en `facontrol_db` y es COMPARTIDO por los tres modos de la
-- suite, que es la regla que el cliente repitió: "lo único que podrán compartir
-- son los usuarios + roles (por respectivos modos) + permisos otorgados".
--
-- CONSECUENCIA TÉCNICA: MySQL no admite claves foráneas entre bases distintas,
-- así que `usuario_id` queda como columna simple (sin FK). La integridad la
-- garantiza la aplicación, que es la única que escribe acá. Está anotado en cada
-- tabla donde pasa.
--
-- Este script lo ejecuta sola la aplicación en el primer arranque del modo
-- POS-500 (viaja embebido en FAControl.Data). No hace falta correrlo a mano.
-- =============================================================
SET NAMES utf8mb4;

CREATE DATABASE IF NOT EXISTS pos500_db
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE pos500_db;

-- -------------------------------------------------------------
-- cliente: opcional en la venta (regla Yuber 2026-07-11).
-- Es el cliente DEL MOSTRADOR y no tiene nada que ver con el cliente de
-- préstamos ni con el del dealer: en retail casi nadie se registra, y la cédula
-- es opcional.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cliente (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  cedula     VARCHAR(20)  NULL,
  nombre     VARCHAR(150) NOT NULL,
  telefono   VARCHAR(20)  NULL,
  direccion  VARCHAR(250) NULL,
  notas      TEXT         NULL,
  created_at DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at DATETIME     NULL,
  deleted_at DATETIME     NULL,                  -- soft delete
  PRIMARY KEY (id),
  UNIQUE KEY uq_cliente_cedula (cedula)          -- múltiples NULL permitidos
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- producto: inventario con caducidad (el semáforo se calcula, no se persiste)
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS producto (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo          VARCHAR(50)   NULL,            -- código de barras / interno
  nombre          VARCHAR(150)  NOT NULL,
  precio          DECIMAL(15,2) NOT NULL,
  cantidad        INT           NOT NULL DEFAULT 0,
  descripcion     TEXT          NULL,
  fecha_caducidad DATE          NULL,
  created_at      DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at      DATETIME      NULL,
  deleted_at      DATETIME      NULL,            -- soft delete
  PRIMARY KEY (id),
  UNIQUE KEY uq_producto_codigo (codigo),        -- múltiples NULL permitidos
  KEY ix_producto_nombre (nombre),
  KEY ix_producto_caducidad (fecha_caducidad)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- factura: NUNCA se elimina, solo se anula. Totales persistidos + la tasa de
-- ITBIS aplicada (para que el histórico no cambie si mañana cambia la tasa).
-- cliente_id NULL = venta sin cliente / consumidor final.
--
-- usuario_id: quién facturó. SIN clave foránea — el usuario vive en
-- facontrol_db (ver el encabezado).
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS factura (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  numero_factura    VARCHAR(30)   NOT NULL,
  cliente_id        BIGINT UNSIGNED NULL,
  usuario_id        BIGINT UNSIGNED NOT NULL,
  fecha_emision     DATETIME      NOT NULL,      -- UTC
  subtotal          DECIMAL(15,2) NOT NULL,
  itbis_tasa        DECIMAL(5,2)  NOT NULL,      -- 18.00 al día de hoy
  itbis             DECIMAL(15,2) NOT NULL,
  total             DECIMAL(15,2) NOT NULL,
  metodo_pago       ENUM('efectivo','tarjeta','transferencia','mixto') NOT NULL,
  efectivo_recibido DECIMAL(15,2) NULL,          -- solo efectivo/mixto
  cambio            DECIMAL(15,2) NULL,
  estado            ENUM('emitida','anulada') NOT NULL DEFAULT 'emitida',
  anulada_at        DATETIME      NULL,
  anulada_motivo    VARCHAR(250)  NULL,
  created_at        DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  PRIMARY KEY (id),
  UNIQUE KEY uq_factura_numero (numero_factura),
  KEY ix_factura_fecha (fecha_emision),
  KEY ix_factura_usuario (usuario_id, fecha_emision),
  CONSTRAINT fk_factura_cliente FOREIGN KEY (cliente_id)
    REFERENCES cliente (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- detalle: líneas de factura (RESTRICT: las facturas no se borran)
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS detalle (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  factura_id      BIGINT UNSIGNED NOT NULL,
  producto_id     BIGINT UNSIGNED NOT NULL,
  cantidad        INT           NOT NULL,
  precio_unitario DECIMAL(15,2) NOT NULL,        -- precio al momento de la venta
  subtotal        DECIMAL(15,2) NOT NULL,        -- cantidad * precio_unitario
  PRIMARY KEY (id),
  KEY ix_detalle_factura (factura_id),
  KEY ix_detalle_producto (producto_id),
  CONSTRAINT fk_detalle_factura FOREIGN KEY (factura_id)
    REFERENCES factura (id) ON DELETE RESTRICT,
  CONSTRAINT fk_detalle_producto FOREIGN KEY (producto_id)
    REFERENCES producto (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- cuadre_caja: cierre por usuario y día de negocio; inmutable tras crearse.
-- usuario_id sin FK, por lo mismo que factura.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS cuadre_caja (
  id                     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  usuario_id             BIGINT UNSIGNED NOT NULL,
  fecha                  DATE          NOT NULL,  -- día de negocio (UTC-4)
  total_facturas         INT           NOT NULL,
  total_vendido          DECIMAL(15,2) NOT NULL,
  tiempo_activo_segundos INT           NOT NULL DEFAULT 0,
  created_at             DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  PRIMARY KEY (id),
  UNIQUE KEY uq_cuadre_usuario_fecha (usuario_id, fecha)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- configuracion_negocio: UNA sola fila (id fijo = 1).
-- Es la configuración del PUNTO DE VENTA: ITBIS, moneda, numeración de
-- facturas y datos que salen en el ticket. Los datos del negocio de la suite
-- (nombre, RNC, teléfono) se siguen editando en Configuración de FAControl;
-- acá viven los que solo le importan al POS.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS configuracion_negocio (
  id                       TINYINT UNSIGNED NOT NULL,
  nombre_negocio           VARCHAR(150)  NOT NULL DEFAULT 'Mi Negocio',
  rnc                      VARCHAR(20)   NULL,
  direccion                VARCHAR(250)  NULL,
  telefono                 VARCHAR(20)   NULL,
  email                    VARCHAR(150)  NULL,
  logo_ruta                VARCHAR(500)  NULL,
  itbis_activo             TINYINT(1)    NOT NULL DEFAULT 1,   -- OFF: sin ITBIS en venta/ticket
  itbis_tasa               DECIMAL(5,2)  NOT NULL DEFAULT 18.00,
  redondeo                 ENUM('centavo','peso','arriba') NOT NULL DEFAULT 'centavo',
  moneda_simbolo           VARCHAR(10)   NOT NULL DEFAULT 'RD$',
  formato_miles            ENUM('coma','punto') NOT NULL DEFAULT 'coma',
  factura_prefijo          VARCHAR(10)   NOT NULL DEFAULT 'F-',
  factura_siguiente        BIGINT UNSIGNED NOT NULL DEFAULT 1,  -- SELECT ... FOR UPDATE al emitir
  factura_formato          ENUM('simple','con_anio') NOT NULL DEFAULT 'simple',
  mostrar_cliente_en_venta TINYINT(1)    NOT NULL DEFAULT 1,
  updated_at               DATETIME      NULL,
  PRIMARY KEY (id),
  CONSTRAINT ck_config_unica CHECK (id = 1)
) ENGINE=InnoDB;

INSERT IGNORE INTO configuracion_negocio (id) VALUES (1);
