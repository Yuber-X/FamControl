-- =============================================================
-- FAControl — Roles POR MODO
-- Script: 011_roles_por_modo.sql
-- Pedido de Yuber (2026-07-18): cada modo tiene sus propios roles y permisos.
-- Al darle acceso a un modo se le asigna el rol EQUIVALENTE de ese modo
-- (ej: Jessi Cobradora en Prest → Vendedora en DealControl), con los permisos
-- propios del modo (inventario/ventas/alquileres/gastos ≠ los de PrestControl).
--
-- DISEÑO de bajo riesgo: el login y usuario_permiso NO cambian; usuario_permiso
-- sigue siendo la UNIÓN efectiva. Se agrega usuario_modo_rol para recordar el
-- rol elegido por modo; al guardar, la app recalcula la unión. El acceso al modo
-- se mantiene por el permiso acceso_<modo> (incluido en cada rol de ese modo).
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001. Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 1. rol.modo: a qué modo pertenece el rol (NULL = global, p. ej. Admin)
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='rol' AND COLUMN_NAME='modo');
SET @sql := IF(@existe=0,
  "ALTER TABLE rol ADD COLUMN modo ENUM('prestcontrol','dealercontrol','autocontrol') NULL AFTER nombre",
  'SELECT "rol.modo ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Unicidad por (nombre, modo): 'Vendedor'/'Encargado' se repiten entre modos
SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='rol' AND INDEX_NAME='uq_rol_nombre');
SET @sql := IF(@existe>0, 'ALTER TABLE rol DROP INDEX uq_rol_nombre', 'SELECT "uq_rol_nombre ausente"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='rol' AND INDEX_NAME='uq_rol_nombre_modo');
SET @sql := IF(@existe=0, 'ALTER TABLE rol ADD UNIQUE KEY uq_rol_nombre_modo (nombre, modo)',
  'SELECT "uq_rol_nombre_modo ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Etiquetar los roles ya existentes
UPDATE rol SET modo=NULL         WHERE nombre='Admin';
UPDATE rol SET modo='prestcontrol' WHERE nombre IN ('Supervisor','Cobrador');

-- -------------------------------------------------------------
-- 2. usuario_modo_rol: rol del usuario en cada modo (acceso = fila presente)
-- -------------------------------------------------------------
CREATE TABLE IF NOT EXISTS usuario_modo_rol (
  usuario_id BIGINT UNSIGNED NOT NULL,
  modo       ENUM('prestcontrol','dealercontrol','autocontrol') NOT NULL,
  rol_id     INT UNSIGNED NOT NULL,
  PRIMARY KEY (usuario_id, modo),
  CONSTRAINT fk_umr_usuario FOREIGN KEY (usuario_id) REFERENCES usuario(id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_umr_rol     FOREIGN KEY (rol_id)     REFERENCES rol(id)     ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- 3. Permisos propios de DealControl (finos, distintos a PrestControl)
-- -------------------------------------------------------------
INSERT IGNORE INTO permiso (codigo, nombre, descripcion) VALUES
  ('inventario',        'Inventario (ver)',         'Consulta del inventario de vehículos'),
  ('inventario_editar', 'Inventario (crear/editar)','Alta, edición y baja de vehículos'),
  ('ventas',            'Ventas al contado',        'Registrar ventas al contado de vehículos'),
  ('alquileres',        'Alquileres (rent a car)',  'Registrar y devolver alquileres'),
  ('gastos',            'Importación / gastos',     'Gestionar los gastos de importación');

-- -------------------------------------------------------------
-- 4. Roles nuevos por modo (Encargado = manda todo el modo; Vendedor = opera)
-- -------------------------------------------------------------
INSERT IGNORE INTO rol (nombre, modo, descripcion) VALUES
  ('Encargado','dealercontrol','Gestiona el dealer: inventario, ventas, alquileres y gastos'),
  ('Vendedor', 'dealercontrol','Vende y alquila; consulta el inventario'),
  ('Encargado','autocontrol',  'Gestiona las ventas financiadas: crédito, cobros y contratos'),
  ('Vendedor', 'autocontrol',  'Crea ventas financiadas y cobra');

-- -------------------------------------------------------------
-- 5. rol_permiso de los roles nuevos (incluyen su acceso_<modo>)
-- -------------------------------------------------------------
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre='Encargado' AND r.modo='dealercontrol'
  AND p.codigo IN ('inventario','inventario_editar','ventas','alquileres','gastos',
                   'clientes','clientes_editar','reportes','historial','acceso_dealercontrol');

INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre='Vendedor' AND r.modo='dealercontrol'
  AND p.codigo IN ('inventario','ventas','alquileres','clientes','acceso_dealercontrol');

INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre='Encargado' AND r.modo='autocontrol'
  AND p.codigo IN ('prestamos','prestamos_crear','prestamos_cancelar','cobros',
                   'clientes','clientes_editar','reportes','historial','acceso_autocontrol');

INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre='Vendedor' AND r.modo='autocontrol'
  AND p.codigo IN ('prestamos','prestamos_crear','cobros','clientes','acceso_autocontrol');

-- -------------------------------------------------------------
-- 5b. El Admin (global) SIEMPRE tiene todos los permisos, incluidos los nuevos
--     (su rol_permiso/usuario_permiso se sembró antes de que existieran).
-- -------------------------------------------------------------
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p WHERE r.nombre='Admin';
INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, p.id FROM usuario u JOIN rol r ON r.id=u.rol_id CROSS JOIN permiso p WHERE r.nombre='Admin';

-- -------------------------------------------------------------
-- 6. Migrar usuarios existentes a usuario_modo_rol
-- -------------------------------------------------------------
-- Prest: los que ya tenían rol de PrestControl (Supervisor/Cobrador)
INSERT IGNORE INTO usuario_modo_rol (usuario_id, modo, rol_id)
SELECT u.id, 'prestcontrol', u.rol_id
FROM usuario u JOIN rol r ON r.id=u.rol_id
WHERE r.modo='prestcontrol';

-- Dealer/Auto: los que tenían acceso_* pasan a Vendedor de ese modo (no-admins)
INSERT IGNORE INTO usuario_modo_rol (usuario_id, modo, rol_id)
SELECT up.usuario_id, 'dealercontrol',
       (SELECT id FROM rol WHERE nombre='Vendedor' AND modo='dealercontrol')
FROM usuario_permiso up JOIN permiso p ON p.id=up.permiso_id
WHERE p.codigo='acceso_dealercontrol'
  AND up.usuario_id NOT IN (SELECT u.id FROM usuario u JOIN rol r ON r.id=u.rol_id WHERE r.nombre='Admin');

INSERT IGNORE INTO usuario_modo_rol (usuario_id, modo, rol_id)
SELECT up.usuario_id, 'autocontrol',
       (SELECT id FROM rol WHERE nombre='Vendedor' AND modo='autocontrol')
FROM usuario_permiso up JOIN permiso p ON p.id=up.permiso_id
WHERE p.codigo='acceso_autocontrol'
  AND up.usuario_id NOT IN (SELECT u.id FROM usuario u JOIN rol r ON r.id=u.rol_id WHERE r.nombre='Admin');

-- -------------------------------------------------------------
-- 7. Los no-admins pasan a rol_id NULL (Admin queda global) y se RECOMPUTA su
--    usuario_permiso desde usuario_modo_rol. El trigger borra usuario_permiso al
--    nulear el rol; por eso recomputamos justo después.
-- -------------------------------------------------------------
UPDATE usuario u JOIN rol r ON r.id=u.rol_id SET u.rol_id=NULL WHERE r.nombre <> 'Admin';

DELETE up FROM usuario_permiso up JOIN usuario u ON u.id=up.usuario_id WHERE u.rol_id IS NULL;
INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT umr.usuario_id, rp.permiso_id
FROM usuario_modo_rol umr JOIN rol_permiso rp ON rp.rol_id=umr.rol_id;

SELECT 'Roles por modo OK' AS resultado,
       (SELECT COUNT(*) FROM rol) AS roles,
       (SELECT COUNT(*) FROM usuario_modo_rol) AS asignaciones;
