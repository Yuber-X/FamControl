-- =============================================================
-- FAControl — Quién cobró cada pago (reporte por usuario)
-- Script: 007_pago_created_by.sql
-- Pedido del cliente 2026-07-19: filtro por usuario en Reportes,
-- como el cierre de caja. MIGRACION idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA='facontrol_db' AND TABLE_NAME='pago'
                  AND COLUMN_NAME='created_by');
SET @sql := IF(@existe=0,
  'ALTER TABLE pago ADD COLUMN created_by BIGINT UNSIGNED NULL AFTER notas',
  'SELECT "created_by ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @existe := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
                WHERE TABLE_SCHEMA='facontrol_db' AND TABLE_NAME='pago'
                  AND CONSTRAINT_NAME='fk_pago_usuario');
SET @sql := IF(@existe=0,
  'ALTER TABLE pago ADD CONSTRAINT fk_pago_usuario FOREIGN KEY (created_by) REFERENCES usuario (id) ON DELETE SET NULL',
  'SELECT "fk_pago_usuario ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Backfill: los pagos anteriores a esta columna toman el usuario de la
-- auditoría de su operación. Los pagos de una misma operación comparten
-- fecha_pago exacta, así que un solo audit por operación alcanza para todos.
-- Idempotente: solo toca los que están en NULL.
UPDATE pago p
JOIN (
  SELECT p2.fecha_pago, MAX(a.usuario_id) AS usuario_id
  FROM pago p2
  JOIN auditoria a ON a.entidad = 'pago' AND a.accion = 'crear' AND a.entidad_id = p2.id
  GROUP BY p2.fecha_pago
) src ON src.fecha_pago = p.fecha_pago
SET p.created_by = src.usuario_id
WHERE p.created_by IS NULL;
