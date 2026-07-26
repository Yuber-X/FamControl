-- =============================================================
-- FAControl — Panel principal propio de DealControl
-- Script: 014_panel_deal.sql
-- Pedido del cliente (2026-07-25): DealControl tiene su propio panel
-- (inventario, ventas, alquileres) sin mezclar datos de PrestControl.
-- El permiso 'panel' se otorga al Encargado del dealer (el Vendedor NO lo ve:
-- el panel muestra totales y pagos, que le están vedados).
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001. Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- 1. Encargado (DealControl) gana el permiso 'panel'
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Encargado' AND r.modo = 'dealercontrol' AND p.codigo = 'panel';

-- 2. Backfill de usuarios que YA tienen ese rol (sets por modo 013 + unión efectiva)
INSERT IGNORE INTO usuario_modo_permiso (usuario_id, modo, permiso_id)
SELECT umr.usuario_id, umr.modo, p.id
FROM usuario_modo_rol umr
JOIN rol r ON r.id = umr.rol_id
JOIN permiso p ON p.codigo = 'panel'
WHERE r.nombre = 'Encargado' AND r.modo = 'dealercontrol' AND umr.modo = 'dealercontrol';

INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT ump.usuario_id, ump.permiso_id
FROM usuario_modo_permiso ump
JOIN permiso p ON p.id = ump.permiso_id
WHERE p.codigo = 'panel' AND ump.modo = 'dealercontrol';
