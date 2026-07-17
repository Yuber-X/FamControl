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
