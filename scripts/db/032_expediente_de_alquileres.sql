-- =============================================================
-- FAControl — El expediente digital también para alquileres
-- Script: 032_expediente_de_alquileres.sql
--
-- PEDIDO (Yuber, 2026-07-30): que los alquileres tengan su "ver detalles" al
-- estilo de Financiamiento de venta, y que "tambien debe mostrarse en
-- contratos". Un alquiler tiene sus propios papeles: el contrato firmado, la
-- licencia del conductor y las fotos del auto al salir y al volver —que son las
-- que evitan discusiones por un golpe que ya venía—.
--
-- Sigue exactamente el molde de 026: el expediente ya colgaba de una VENTA o de
-- un PRESTAMO; ahora suma ALQUILER. Tres columnas nulables de las cuales va
-- exactamente UNA, con sus claves foraneas.
--
-- POR QUE NO UNA COLUMNA (entidad, entidad_id) GENERICA: se pierden las claves
-- foraneas, que es lo que hoy garantiza que ningun documento quede colgando de
-- algo que no existe.
--
-- CARPETAS: los nuevos van a `alquileres/<id>/`. El prefijo por tipo es lo que
-- evita que el alquiler 5 y la venta 5 compartan archivos.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 1. alquiler_id, con su indice y su clave foranea
-- -------------------------------------------------------------
SET @sql := IF(
  (SELECT COUNT(*) FROM information_schema.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento'
     AND COLUMN_NAME = 'alquiler_id') = 0,
  'ALTER TABLE documento ADD COLUMN alquiler_id BIGINT UNSIGNED NULL AFTER prestamo_id',
  'SELECT "alquiler_id ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @sql := IF(
  (SELECT COUNT(*) FROM information_schema.STATISTICS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento'
     AND INDEX_NAME = 'ix_documento_alquiler') = 0,
  'ALTER TABLE documento ADD KEY ix_documento_alquiler (alquiler_id)',
  'SELECT "ix_documento_alquiler ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @sql := IF(
  (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento'
     AND CONSTRAINT_NAME = 'fk_documento_alquiler') = 0,
  'ALTER TABLE documento ADD CONSTRAINT fk_documento_alquiler
     FOREIGN KEY (alquiler_id) REFERENCES alquiler (id) ON DELETE RESTRICT',
  'SELECT "fk_documento_alquiler ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 2. La regla "exactamente un dueño" pasa de dos a tres columnas.
--    Se suelta la vieja y se pone la nueva: un CHECK sobre dos columnas dejaria
--    entrar un documento con alquiler_id Y venta_id a la vez.
-- -------------------------------------------------------------
SET @sql := IF(
  (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento'
     AND CONSTRAINT_NAME = 'ck_documento_un_dueno') > 0,
  'ALTER TABLE documento DROP CHECK ck_documento_un_dueno',
  'SELECT "ck_documento_un_dueno ya no esta"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Suma de los tres = 1. Es la forma directa de decir "uno y solo uno" con tres
-- columnas; con <> encadenados la cuenta da falsos positivos (tres nulos y tres
-- llenos se comportarian igual).
SET @sql := IF(
  (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'documento'
     AND CONSTRAINT_NAME = 'ck_documento_un_dueno_3') = 0,
  'ALTER TABLE documento ADD CONSTRAINT ck_documento_un_dueno_3
     CHECK ((venta_id IS NOT NULL) + (prestamo_id IS NOT NULL) + (alquiler_id IS NOT NULL) = 1)',
  'SELECT "ck_documento_un_dueno_3 ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Verificacion (informativa al correr el script a mano)
SELECT
  (SELECT COUNT(*) FROM documento WHERE venta_id    IS NOT NULL) AS de_ventas,
  (SELECT COUNT(*) FROM documento WHERE prestamo_id IS NOT NULL) AS de_prestamos,
  (SELECT COUNT(*) FROM documento WHERE alquiler_id IS NOT NULL) AS de_alquileres;
