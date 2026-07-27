-- =============================================================
-- FAControl — Rol PROGRAMADOR (autoridad total, blindado)
-- Script: 017_rol_programador.sql
-- Pedido del cliente (2026-07-27): "Un rol masivo para el programador donde
-- ningún admin pueda quitar su autoridad total o crear uno, excepto otro
-- Programador."
--
-- DISEÑO:
--  * `Programador` es un rol GLOBAL (modo NULL), igual que Admin, con TODOS
--    los permisos del catálogo — presentes y futuros (ver nota abajo).
--  * El blindaje NO vive en la base sino en UsuarioService: un Admin no ve,
--    no edita, no crea ni le restablece la contraseña a un Programador. La
--    base solo aporta el rol; la regla es de la aplicación (una sola puerta).
--  * NO se siembra ninguna cuenta acá: la cuenta del programador se crea con
--    el CÓDIGO 3 del launcher (recuperación de acceso), así no viaja ninguna
--    contraseña dentro del repositorio ni del instalador.
--
-- NOTA: cuando se agregue un permiso nuevo al catálogo hay que volver a
-- correr el INSERT de rol_permiso de este script (es idempotente) o el
-- Programador se quedaría sin ese permiso. Igual que con Admin.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- 1. El rol (global, como Admin). La clave única es (nombre, modo), pero MySQL
--    no considera iguales dos NULL, así que el INSERT ... SELECT con NOT EXISTS
--    es lo que garantiza que no se duplique al re-ejecutar.
INSERT INTO rol (nombre, modo, descripcion)
SELECT 'Programador', NULL,
       'Autoridad total del sistema — reservado al desarrollador. Solo otro Programador puede crearlo o modificarlo.'
FROM DUAL
WHERE NOT EXISTS (
  SELECT 1 FROM (SELECT id FROM rol WHERE nombre = 'Programador' AND modo IS NULL) AS existente
);

-- 2. Todos los permisos del catálogo
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id
FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Programador' AND r.modo IS NULL;
