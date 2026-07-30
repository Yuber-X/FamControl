-- =============================================================
-- FAControl — POS-500 como modo de la suite: permisos y roles
-- Script: 022_pos500_permisos_roles.sql
--
-- Pedido del cliente (2026-07-30): "vamos a integrarlo junto a su db... con el
-- código ya será suficiente para aclarar si se quiere vender, solo será
-- activarlo y listo."
--
-- DÓNDE VIVE CADA COSA
--  * Los DATOS del punto de venta (productos, facturas, caja) van en una base
--    APARTE, `pos500_db`. Se vende por separado, así que sus datos tienen que
--    poder irse solos.
--  * Los USUARIOS, ROLES y PERMISOS siguen siendo los de facontrol_db, igual
--    que para los otros modos — es la regla que el cliente repitió: "lo único
--    que podrán compartir son los usuarios + roles (por respectivos modos) +
--    permisos otorgados". Por eso este script toca facontrol_db, no pos500_db.
--
-- PERMISOS QUE SE REUSAN (no se duplican): `panel`, `clientes`,
-- `clientes_editar`, `reportes`, `usuarios` y `configuracion` ya existen y son
-- las mismas pantallas conceptuales, filtradas por el modo activo.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 0. 'pos500' como valor de modo
--    `cliente.ambito` NO se toca: los clientes del punto de venta viven en
--    pos500_db, no acá. Las otras tres columnas sí, porque gobiernan roles y
--    permisos, que son compartidos.
-- -------------------------------------------------------------
SET @sql := IF(
  (SELECT COLUMN_TYPE FROM information_schema.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'rol' AND COLUMN_NAME = 'modo')
   LIKE '%pos500%',
  'SELECT "rol.modo ya admite pos500"',
  "ALTER TABLE rol MODIFY COLUMN modo
     ENUM('prestcontrol','dealercontrol','autocontrol','pos500') NULL");
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @sql := IF(
  (SELECT COLUMN_TYPE FROM information_schema.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuario_modo_rol' AND COLUMN_NAME = 'modo')
   LIKE '%pos500%',
  'SELECT "usuario_modo_rol.modo ya admite pos500"',
  "ALTER TABLE usuario_modo_rol MODIFY COLUMN modo
     ENUM('prestcontrol','dealercontrol','autocontrol','pos500') NOT NULL");
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @sql := IF(
  (SELECT COLUMN_TYPE FROM information_schema.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'usuario_modo_permiso' AND COLUMN_NAME = 'modo')
   LIKE '%pos500%',
  'SELECT "usuario_modo_permiso.modo ya admite pos500"',
  "ALTER TABLE usuario_modo_permiso MODIFY COLUMN modo
     ENUM('prestcontrol','dealercontrol','autocontrol','pos500') NOT NULL");
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 1. Permisos propios del punto de venta
-- -------------------------------------------------------------
INSERT IGNORE INTO permiso (codigo, nombre, descripcion) VALUES
  ('vender',              'Vender',                  'Facturar en el punto de venta'),
  ('productos',           'Productos',               'Catálogo de productos y precios'),
  ('almacen',             'Almacén',                 'Existencias y entradas de mercancía'),
  ('caducidad',           'Caducidad',               'Control de productos próximos a vencer'),
  ('comprobantes',        'Buscar comprobante',      'Buscar y reimprimir facturas propias'),
  ('comprobantes_todos',  'Comprobantes de todos',   'Ver los comprobantes de todos los cajeros'),
  ('cuadre',              'Cuadre de caja',          'Cerrar y consultar su propia caja'),
  ('cuadre_todos',        'Cuadre de todos',         'Ver el cuadre de caja de todos los cajeros'),
  ('facturas_anular',     'Anular facturas',         'Anular una factura ya emitida'),
  ('acceso_pos500',       'Acceso a POS-500',        'Puede entrar a la estancia del punto de venta');

-- -------------------------------------------------------------
-- 2. Roles del modo pos500
--    (mismos que traía POS-500: Supervisor, Cajero y Vendedor. El Admin y el
--     Programador son globales y entran sin rol de modo.)
-- -------------------------------------------------------------
INSERT INTO rol (nombre, modo, descripcion)
SELECT * FROM (
  SELECT 'Supervisor' AS nombre, 'pos500' AS modo,
         'Operación completa del piso de venta, sin configuración ni usuarios' AS descripcion
  UNION ALL SELECT 'Cajero', 'pos500',
         'Ventas, consulta de clientes, su propio cuadre y sus comprobantes'
  UNION ALL SELECT 'Vendedor', 'pos500',
         'Ventas y gestión de clientes'
) AS nuevos
WHERE NOT EXISTS (
  SELECT 1 FROM (SELECT nombre, modo FROM rol WHERE modo = 'pos500') AS existentes
  WHERE existentes.nombre = nuevos.nombre
);

-- -------------------------------------------------------------
-- 3. Qué otorga cada rol
-- -------------------------------------------------------------

-- Admin y Programador (globales): también los permisos nuevos
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id
FROM rol r CROSS JOIN permiso p
WHERE r.nombre IN ('Admin', 'Programador') AND r.modo IS NULL;

-- Supervisor del POS: todo el piso de venta, incluido lo de "todos"
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Supervisor' AND r.modo = 'pos500'
  AND p.codigo IN ('panel','vender','clientes','clientes_editar','productos','almacen',
                   'caducidad','comprobantes','comprobantes_todos','cuadre','cuadre_todos',
                   'reportes','facturas_anular','acceso_pos500');

-- Cajero: vende y cuadra LO SUYO (sin ver la caja ni los comprobantes ajenos)
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Cajero' AND r.modo = 'pos500'
  AND p.codigo IN ('vender','clientes','comprobantes','cuadre','acceso_pos500');

-- Vendedor: vende y administra clientes; no cuadra caja
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Vendedor' AND r.modo = 'pos500'
  AND p.codigo IN ('vender','clientes','clientes_editar','comprobantes','acceso_pos500');

-- -------------------------------------------------------------
-- 4. Los usuarios que YA existen no pierden nada: el trigger solo siembra
--    permisos al crear o cambiar el rol. A los Admin ya creados hay que
--    darles los permisos nuevos a mano (si no, el Admin no vería el POS).
-- -------------------------------------------------------------
INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, p.id
FROM usuario u
JOIN rol r ON r.id = u.rol_id AND r.modo IS NULL AND r.nombre IN ('Admin', 'Programador')
CROSS JOIN permiso p
WHERE p.codigo IN ('vender','productos','almacen','caducidad','comprobantes',
                   'comprobantes_todos','cuadre','cuadre_todos','facturas_anular',
                   'acceso_pos500');

-- Verificación (informativa al correr el script a mano)
SELECT r.nombre AS rol, r.modo, COUNT(rp.permiso_id) AS permisos
FROM rol r LEFT JOIN rol_permiso rp ON rp.rol_id = r.id
WHERE r.modo = 'pos500' OR r.modo IS NULL
GROUP BY r.id ORDER BY r.modo, r.nombre;
