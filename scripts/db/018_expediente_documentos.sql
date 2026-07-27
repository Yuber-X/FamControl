-- =============================================================
-- FAControl — Expediente digital del contrato (documentos adjuntos)
-- Script: 018_expediente_documentos.sql
-- Pedido del cliente (2026-07-27): en "Contratos > Ver detalles >
-- Financiamiento de la venta" hay que poder subir y guardar todas las
-- facturas, documentos e imágenes que el cliente entregó para la compra;
-- descargarlos todos en un ZIP; abrirlos con su app de Windows; y que entren
-- en el respaldo automático.
--
-- DISEÑO:
--  * El ARCHIVO no va en la base (un BLOB por cada foto de cédula hincha el
--    dump y hace lento el respaldo). Va al disco, en
--    <carpeta de la app>\expedientes\<venta_id>\, y acá se guarda su ficha.
--  * `ruta_relativa` es relativa a esa carpeta raíz: así mover la instalación
--    de PC no rompe el expediente.
--  * Soft delete (`deleted_at`): borrar un documento del expediente de una
--    compra es un acto administrativo, y por eso además es exclusivo del Admin.
--  * `tipo` distingue la FACTURA ESCANEADA (la firmada que reemplaza en pantalla
--    a la que emite el sistema) del resto de los papeles.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

CREATE TABLE IF NOT EXISTS documento_venta (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  venta_id       BIGINT UNSIGNED NOT NULL,
  -- Nombre tal como lo entregó el usuario (es lo que se muestra y lo que sale en el ZIP)
  nombre         VARCHAR(255)  NOT NULL,
  -- Ruta dentro de la carpeta de expedientes: '<venta_id>/<id>_<nombre>'
  ruta_relativa  VARCHAR(400)  NOT NULL,
  extension      VARCHAR(15)   NOT NULL,
  tamano_bytes   BIGINT UNSIGNED NOT NULL DEFAULT 0,
  tipo           ENUM('otro','factura_escaneada','contrato','identificacion') NOT NULL DEFAULT 'otro',
  notas          VARCHAR(300)  NULL,
  created_at     DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by     BIGINT UNSIGNED NULL,
  deleted_at     DATETIME      NULL,
  PRIMARY KEY (id),
  KEY ix_docventa_venta (venta_id),
  KEY ix_docventa_tipo (venta_id, tipo),
  CONSTRAINT fk_docventa_venta   FOREIGN KEY (venta_id)   REFERENCES venta_vehiculo (id) ON DELETE RESTRICT,
  CONSTRAINT fk_docventa_usuario FOREIGN KEY (created_by) REFERENCES usuario (id)        ON DELETE SET NULL
) ENGINE=InnoDB;
