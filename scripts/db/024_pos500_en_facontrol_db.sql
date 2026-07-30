-- =============================================================
-- FAControl — El punto de venta pasa a la base de la suite
-- Script: 024_pos500_en_facontrol_db.sql
--
-- CAMBIO DE CRITERIO (Yuber, 2026-07-30, después de probarlo): el POS-500
-- guardaba sus datos en `pos500_db`, aparte. Se unifica todo en `facontrol_db`
-- porque **dos respaldos confunden al usuario**: el cliente veía dos .sql en la
-- carpeta y no sabía cuál era "el bueno". Con una sola base hay un solo archivo
-- y no hay forma de restaurar la mitad del negocio.
--
-- DE PASO ARREGLA UN ERROR REAL: al vender fallaba con
--   "Cannot add or update a child row: fk_factura_usuario ... REFERENCES usuario"
-- porque `pos500_db` conservaba su tabla `usuario` vacía (era del POS-500
-- independiente) y la factura apuntaba ahí, no a los usuarios de la suite. Con
-- todo en la misma base, la clave foránea vuelve a ser real y correcta.
--
-- PREFIJO `pos_`: las tablas del punto de venta conviven con las de préstamos y
-- dealer, y varias se llamaban igual (`cliente`). El prefijo las agrupa de un
-- vistazo y evita tocar las reglas del `cliente` de la suite, que tiene ámbito,
-- cédula obligatoria y apellido.
--
-- EL CLIENTE DEL MOSTRADOR ES OTRA COSA: en retail casi nadie se registra, por
-- eso `pos_cliente` admite cédula nula y no pide apellido. No se mezcla con el
-- cliente de préstamos ni con el del dealer.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- pos_cliente: cliente del mostrador. Cédula OPCIONAL, sin apellido.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS pos_cliente (
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
  UNIQUE KEY uq_pos_cliente_cedula (cedula)      -- múltiples NULL permitidos
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pos_producto: inventario con caducidad (el semáforo se calcula, no se guarda)
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS pos_producto (
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
  UNIQUE KEY uq_pos_producto_codigo (codigo),    -- múltiples NULL permitidos
  KEY ix_pos_producto_nombre (nombre),
  KEY ix_pos_producto_caducidad (fecha_caducidad)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pos_factura: NUNCA se elimina, solo se anula. Totales persistidos + la tasa
-- de ITBIS aplicada (el histórico no cambia si mañana cambia la tasa).
-- cliente_id NULL = consumidor final.
-- usuario_id: AHORA sí con clave foránea, porque el usuario vive en esta misma
-- base. Era justo lo que fallaba con la base separada.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS pos_factura (
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
  UNIQUE KEY uq_pos_factura_numero (numero_factura),
  KEY ix_pos_factura_fecha (fecha_emision),
  KEY ix_pos_factura_usuario (usuario_id, fecha_emision),
  CONSTRAINT fk_pos_factura_cliente FOREIGN KEY (cliente_id)
    REFERENCES pos_cliente (id) ON DELETE RESTRICT,
  CONSTRAINT fk_pos_factura_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pos_detalle: líneas de factura (RESTRICT: las facturas no se borran)
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS pos_detalle (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  factura_id      BIGINT UNSIGNED NOT NULL,
  producto_id     BIGINT UNSIGNED NOT NULL,
  cantidad        INT           NOT NULL,
  precio_unitario DECIMAL(15,2) NOT NULL,        -- precio al momento de la venta
  subtotal        DECIMAL(15,2) NOT NULL,
  PRIMARY KEY (id),
  KEY ix_pos_detalle_factura (factura_id),
  KEY ix_pos_detalle_producto (producto_id),
  CONSTRAINT fk_pos_detalle_factura FOREIGN KEY (factura_id)
    REFERENCES pos_factura (id) ON DELETE RESTRICT,
  CONSTRAINT fk_pos_detalle_producto FOREIGN KEY (producto_id)
    REFERENCES pos_producto (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pos_cuadre_caja: cierre por cajero y día de negocio; inmutable tras crearse
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS pos_cuadre_caja (
  id                     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  usuario_id             BIGINT UNSIGNED NOT NULL,
  fecha                  DATE          NOT NULL,  -- día de negocio (UTC-4)
  total_facturas         INT           NOT NULL,
  total_vendido          DECIMAL(15,2) NOT NULL,
  tiempo_activo_segundos INT           NOT NULL DEFAULT 0,
  created_at             DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  PRIMARY KEY (id),
  UNIQUE KEY uq_pos_cuadre_usuario_fecha (usuario_id, fecha),
  CONSTRAINT fk_pos_cuadre_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pos_configuracion: UNA sola fila (id fijo = 1). ITBIS, moneda, numeración de
-- facturas y lo que sale en el ticket.
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS pos_configuracion (
  id                       TINYINT UNSIGNED NOT NULL,
  nombre_negocio           VARCHAR(150)  NOT NULL DEFAULT 'Mi Negocio',
  rnc                      VARCHAR(20)   NULL,
  direccion                VARCHAR(250)  NULL,
  telefono                 VARCHAR(20)   NULL,
  email                    VARCHAR(150)  NULL,
  logo_ruta                VARCHAR(500)  NULL,
  itbis_activo             TINYINT(1)    NOT NULL DEFAULT 1,
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
  CONSTRAINT ck_pos_config_unica CHECK (id = 1)
) ENGINE=InnoDB;

INSERT IGNORE INTO pos_configuracion (id) VALUES (1);

-- -------------------------------------------------------------
-- Verificación (informativa al correr el script a mano)
-- -------------------------------------------------------------
SELECT TABLE_NAME, TABLE_ROWS
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME LIKE 'pos\\_%'
ORDER BY TABLE_NAME;

-- NOTA sobre la base vieja: si existe `pos500_db` de las pruebas, se puede
-- borrar a mano cuando se confirme que el punto de venta funciona acá:
--   DROP DATABASE pos500_db;
-- No se hace desde este script a propósito — un script de migración no debería
-- borrar bases por su cuenta.
