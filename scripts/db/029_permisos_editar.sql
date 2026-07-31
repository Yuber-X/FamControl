-- =============================================================
-- FAControl — Permisos para CORREGIR contratos ya registrados
-- Script: 029_permisos_editar.sql
--
-- Pedido del cliente (2026-07-30):
--   "Prestamos > Detalles de prestamos > agreguemos un btn 'editar' que solo
--    los admin pueden tener, O UN PERMISO OTORGADO POR EL MISMO A UN USUARIO."
--   "...agregar dentro de 'ver detalles' un btn editar (tambien agregalo a
--    'financiamiento de venta') ... asi si se produce un error de digitacion
--    se pueda arreglar."
--
-- Osea: no es "solo Admin" a secas. Es un permiso como los demas, que el Admin
-- le puede dar a quien quiera desde la pantalla de Usuarios. Por eso van tres
-- permisos nuevos y no una comprobacion de rol quemada en el codigo.
--
-- POR QUE TRES Y NO UNO
-- Cada uno corresponde a una estancia distinta y a datos que no se mezclan
-- (regla del cliente). Que el encargado del dealer pueda corregir un alquiler
-- no significa que deba poder tocar los prestamos de PrestControl.
--
-- QUE SE PUEDE CORREGIR (la regla vive en los servicios, no aca)
-- Solo mientras NO haya un cobro registrado. Una vez que se emitio un recibo,
-- los numeros de ese papel estan en manos del cliente: cambiar el contrato por
-- detras haria que el recibo mienta. Con cobros hechos solo quedan editables
-- los datos que no son plata (notas, garantia, referencias).
--
-- OJO: los permisos aparecen como casilla en la pantalla de Usuarios solo si
-- ALGUN rol de ese modo los otorga (asi arma el catalogo la consulta de
-- ObtenerCatalogoPermisosDeModoAsync). Por eso ademas del Admin se los damos a
-- los roles de mando de cada modo.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 1. Los permisos
-- -------------------------------------------------------------
INSERT IGNORE INTO permiso (codigo, nombre, descripcion) VALUES
  ('prestamos_editar',  'Préstamos (editar)',
     'Corregir un préstamo ya registrado (errores de digitación)'),
  ('ventas_editar',     'Ventas (editar)',
     'Corregir una venta de vehículo ya registrada'),
  ('alquileres_editar', 'Alquileres (editar)',
     'Corregir un alquiler ya registrado');

-- -------------------------------------------------------------
-- 2. Quien los recibe por defecto
-- -------------------------------------------------------------

-- Admin y Programador (globales): todo, como siempre
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre IN ('Admin', 'Programador') AND r.modo IS NULL;

-- Supervisor de PrestControl: es quien responde por la cartera
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Supervisor' AND r.modo = 'prestcontrol'
  AND p.codigo = 'prestamos_editar';

-- Encargado de DealControl: manda el dealer
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Encargado' AND r.modo = 'dealercontrol'
  AND p.codigo IN ('ventas_editar', 'alquileres_editar');

-- Encargado de AutoControl: sus creditos vehiculares son prestamos
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Encargado' AND r.modo = 'autocontrol'
  AND p.codigo = 'prestamos_editar';

-- El Vendedor NO los recibe a proposito: vende, no corrige contratos ajenos.
-- Si el dueño quiere dárselo a alguien puntual, lo marca en Usuarios.

-- -------------------------------------------------------------
-- 3. Backfill de usuario_permiso
--    Los triggers siembran usuario_permiso al crear o cambiar el rol, pero
--    estos permisos no existian cuando se sembraron los usuarios de antes.
--    Sin esto, un Admin ya creado no veria el boton hasta reasignarse el rol.
-- -------------------------------------------------------------
INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, p.id
FROM usuario u
JOIN usuario_modo_rol umr ON umr.usuario_id = u.id
JOIN rol_permiso rp       ON rp.rol_id = umr.rol_id
JOIN permiso p            ON p.id = rp.permiso_id
WHERE p.codigo IN ('prestamos_editar', 'ventas_editar', 'alquileres_editar');

-- Los globales (Admin/Programador) no pasan por usuario_modo_rol: van por
-- usuario.rol_id.
INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, p.id
FROM usuario u
JOIN rol_permiso rp ON rp.rol_id = u.rol_id
JOIN permiso p      ON p.id = rp.permiso_id
WHERE p.codigo IN ('prestamos_editar', 'ventas_editar', 'alquileres_editar');

-- Y las casillas por modo, para que el formulario de Usuarios los muestre
-- marcados donde corresponde.
INSERT IGNORE INTO usuario_modo_permiso (usuario_id, modo, permiso_id)
SELECT umr.usuario_id, umr.modo, rp.permiso_id
FROM usuario_modo_rol umr
JOIN rol_permiso rp ON rp.rol_id = umr.rol_id
JOIN permiso p      ON p.id = rp.permiso_id
WHERE p.codigo IN ('prestamos_editar', 'ventas_editar', 'alquileres_editar');
