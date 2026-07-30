-- =============================================================
-- FAControl — Acción "anular" en la auditoría
-- Script: 023_auditoria_anular.sql
--
-- POR QUÉ: al integrar el punto de venta (POS-500) aparece una operación que
-- la suite no tenía: ANULAR una factura. No es un "modificar" — una factura
-- emitida no se edita ni se borra nunca, se anula, y eso tiene que poder
-- buscarse solo en el Historial y distinguirse de un cambio cualquiera.
--
-- La auditoría del POS va a la MISMA tabla que el resto de la suite
-- (facontrol_db.auditoria): el cliente tiene un solo historial, no uno por
-- módulo.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente: agregar un valor al ENUM no toca ninguna fila.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

SET @sql := IF(
  (SELECT COLUMN_TYPE FROM information_schema.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'auditoria' AND COLUMN_NAME = 'accion')
   LIKE '%anular%',
  'SELECT "auditoria.accion ya admite anular"',
  "ALTER TABLE auditoria MODIFY COLUMN accion
     ENUM('crear','modificar','eliminar','consultar','login','logout','anular') NOT NULL");
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Verificación (informativa al correr el script a mano)
SELECT COLUMN_TYPE AS acciones_admitidas
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'auditoria' AND COLUMN_NAME = 'accion';
