-- =============================================================
-- FAControl — El expediente digital también para préstamos
-- Script: 026_expediente_de_prestamos.sql
--
-- PEDIDO (Yuber, 2026-07-30): que Contratos de PrestControl funcione como el de
-- DealControl —grid de clientes con lo importante y el expediente de archivos
-- adentro— y que al imprimir el pagaré o la intimación de pago se guarden solos
-- en el expediente del cliente.
--
-- QUÉ CAMBIA: el expediente nació atado a la venta del dealer
-- (`documento_venta.venta_id`). Ahora puede colgar de una VENTA o de un
-- PRÉSTAMO, así que la tabla pasa a llamarse `documento` y tiene las dos claves,
-- de las cuales va exactamente UNA.
--
-- POR QUÉ NO UNA COLUMNA (entidad, entidad_id) GENÉRICA: se pierden las claves
-- foráneas. Con dos columnas nulables el motor sigue garantizando que el
-- documento apunte a algo que existe, que es lo que evita expedientes huérfanos.
--
-- RUTAS EN DISCO: los documentos que ya existen conservan su ruta
-- (`<ventaId>/archivo`) y siguen abriéndose igual — la ruta se guarda por
-- documento, no se calcula. Los nuevos van a `ventas/<id>/` o `prestamos/<id>/`,
-- que además evita que la venta 5 y el préstamo 5 compartan carpeta.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- 1. Renombrar la tabla (solo si todavía tiene el nombre viejo)
SET @sql := IF(
  (SELECT COUNT(*) FROM information_schema.TABLES
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento_venta') = 1
  AND (SELECT COUNT(*) FROM information_schema.TABLES
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento') = 0,
  'RENAME TABLE documento_venta TO documento',
  'SELECT "la tabla ya se llama documento"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- 2. venta_id pasa a admitir NULL (un documento de préstamo no tiene venta)
SET @sql := IF(
  (SELECT IS_NULLABLE FROM information_schema.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento'
     AND COLUMN_NAME = 'venta_id') = 'NO',
  'ALTER TABLE documento MODIFY COLUMN venta_id BIGINT UNSIGNED NULL',
  'SELECT "venta_id ya admite NULL"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- 3. prestamo_id, con su clave foránea
SET @sql := IF(
  (SELECT COUNT(*) FROM information_schema.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento'
     AND COLUMN_NAME = 'prestamo_id') = 0,
  'ALTER TABLE documento ADD COLUMN prestamo_id BIGINT UNSIGNED NULL AFTER venta_id',
  'SELECT "prestamo_id ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @sql := IF(
  (SELECT COUNT(*) FROM information_schema.STATISTICS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento'
     AND INDEX_NAME = 'ix_documento_prestamo') = 0,
  'ALTER TABLE documento ADD KEY ix_documento_prestamo (prestamo_id)',
  'SELECT "ix_documento_prestamo ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @sql := IF(
  (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento'
     AND CONSTRAINT_NAME = 'fk_documento_prestamo') = 0,
  'ALTER TABLE documento ADD CONSTRAINT fk_documento_prestamo
     FOREIGN KEY (prestamo_id) REFERENCES prestamo (id) ON DELETE RESTRICT',
  'SELECT "fk_documento_prestamo ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- 4. Exactamente uno de los dos dueños. Sin esto podría entrar un documento
--    colgando de nada, o de una venta y un préstamo a la vez.
SET @sql := IF(
  (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento'
     AND CONSTRAINT_NAME = 'ck_documento_un_dueno') = 0,
  'ALTER TABLE documento ADD CONSTRAINT ck_documento_un_dueno
     CHECK ((venta_id IS NULL) <> (prestamo_id IS NULL))',
  'SELECT "ck_documento_un_dueno ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- 5. Tipos nuevos: el pagaré y la intimación que la app archiva sola al imprimir
SET @sql := IF(
  (SELECT COLUMN_TYPE FROM information_schema.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento' AND COLUMN_NAME = 'tipo')
   LIKE '%pagare%',
  'SELECT "documento.tipo ya admite pagare e intimacion"',
  "ALTER TABLE documento MODIFY COLUMN tipo
     ENUM('otro','factura_escaneada','contrato','identificacion','pagare','intimacion')
     NOT NULL DEFAULT 'otro'");
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Verificación (informativa al correr el script a mano)
SELECT
  (SELECT COUNT(*) FROM documento WHERE venta_id IS NOT NULL)    AS de_ventas,
  (SELECT COUNT(*) FROM documento WHERE prestamo_id IS NOT NULL) AS de_prestamos;
