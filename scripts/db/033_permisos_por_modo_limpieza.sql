-- =============================================================
-- FAControl — Permiso 'contratos' y limpieza de permisos de otra estancia
-- Script: 033_permisos_por_modo_limpieza.sql
--
-- Pedido del cliente (2026-07-31):
--   "Usuario > Editar usuario > Note que en los permisos falta el permiso para
--    'contratos', y tiene 2 permisos que no son de su modo, son 'vehiculos
--    (crear/editar)' y 'vehiculos (ver)'. Elimina las que no le pertenecen y
--    agrega los permisos de 'contratos'."
--
-- DE DONDE SALIAN LOS AJENOS
-- La pantalla de Usuarios arma las casillas de cada modo con los permisos que
-- otorga ALGUN rol de ese modo. Migraciones viejas —anteriores a los roles por
-- modo (011)— repartieron permisos filtrando solo por NOMBRE de rol:
--   * 008 le dio 'vehiculos' y 'vehiculos_editar' a "Supervisor", y el
--     Supervisor de PrestControl se lo comio;
--   * el mismo patron dejo 'prestamos', 'prestamos_crear' y
--     'prestamos_cancelar' en el Supervisor de POS-500.
-- Por eso PrestControl mostraba casillas de vehiculos y el punto de venta,
-- casillas de prestamos. No era la pantalla: eran los datos.
--
-- LA SOLUCION
-- Se declara que permisos puede otorgar cada modo y se borra de rol_permiso
-- todo lo que quede afuera, para los roles CON modo. Los roles globales (Admin,
-- Programador) no se tocan: por diseño tienen todo.
--
-- Borrar de rol_permiso es seguro: son los DEFAULTS del rol, no lo que el Admin
-- marco a mano. Lo marcado a mano vive en usuario_modo_permiso, que tambien se
-- limpia con el mismo criterio —una casilla de vehiculos guardada bajo
-- 'prestcontrol' no significa nada— y despues se recomputa usuario_permiso, que
-- es la union efectiva que lee el login.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 1. El permiso que faltaba: Contratos de PrestControl
--    Hasta hoy esa pantalla se abria con 'prestamos', asi que no se podia dar
--    el almacen de contratos sin dar tambien toda la cartera.
-- -------------------------------------------------------------
INSERT IGNORE INTO permiso (codigo, nombre, descripcion) VALUES
  ('contratos', 'Contratos', 'Almacén de contratos: pagarés y expediente de papeles del cliente');

-- Admin y Programador: todo, como siempre
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre IN ('Admin', 'Programador') AND r.modo IS NULL;

-- Supervisor de PrestControl: responde por la cartera, ve los contratos
INSERT IGNORE INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Supervisor' AND r.modo = 'prestcontrol' AND p.codigo = 'contratos';

-- El Cobrador NO lo recibe por defecto: cobra, no administra papeles. Si el
-- dueño quiere darselo, marca la casilla en Usuarios.

-- -------------------------------------------------------------
-- 2. Que permisos puede otorgar cada modo
--    Tabla temporal: se usa en los tres borrados de abajo y se descarta.
-- -------------------------------------------------------------
DROP TEMPORARY TABLE IF EXISTS permiso_de_modo;
CREATE TEMPORARY TABLE permiso_de_modo (
  modo   VARCHAR(20) NOT NULL,
  codigo VARCHAR(50) NOT NULL,
  PRIMARY KEY (modo, codigo)
);

INSERT INTO permiso_de_modo (modo, codigo) VALUES
  -- PrestControl: prestamos personales
  ('prestcontrol','panel'), ('prestcontrol','clientes'), ('prestcontrol','clientes_editar'),
  ('prestcontrol','prestamos'), ('prestcontrol','prestamos_crear'),
  ('prestcontrol','prestamos_autorizar'), ('prestcontrol','prestamos_cancelar'),
  ('prestcontrol','prestamos_editar'), ('prestcontrol','cobros'), ('prestcontrol','contratos'),
  ('prestcontrol','reportes'), ('prestcontrol','historial'), ('prestcontrol','acceso_prestcontrol'),
  -- DealControl: inventario, ventas y alquileres. Sus Contratos se abren con
  -- 'ventas' (son el expediente de la venta), no con el permiso nuevo.
  ('dealercontrol','panel'), ('dealercontrol','clientes'), ('dealercontrol','clientes_editar'),
  ('dealercontrol','inventario'), ('dealercontrol','inventario_editar'),
  ('dealercontrol','vehiculos'), ('dealercontrol','vehiculos_editar'),
  ('dealercontrol','ventas'), ('dealercontrol','ventas_editar'),
  ('dealercontrol','alquileres'), ('dealercontrol','alquileres_editar'),
  ('dealercontrol','gastos'), ('dealercontrol','reportes'), ('dealercontrol','historial'),
  ('dealercontrol','acceso_dealercontrol'),
  -- AutoControl: creditos vehiculares (son prestamos)
  ('autocontrol','panel'), ('autocontrol','clientes'), ('autocontrol','clientes_editar'),
  ('autocontrol','prestamos'), ('autocontrol','prestamos_crear'),
  ('autocontrol','prestamos_autorizar'), ('autocontrol','prestamos_cancelar'),
  ('autocontrol','prestamos_editar'), ('autocontrol','cobros'), ('autocontrol','contratos'),
  ('autocontrol','reportes'), ('autocontrol','historial'), ('autocontrol','acceso_autocontrol'),
  -- POS-500: piso de venta
  ('pos500','panel'), ('pos500','vender'), ('pos500','clientes'), ('pos500','clientes_editar'),
  ('pos500','productos'), ('pos500','almacen'), ('pos500','caducidad'),
  ('pos500','comprobantes'), ('pos500','comprobantes_todos'),
  ('pos500','cuadre'), ('pos500','cuadre_todos'), ('pos500','facturas_anular'),
  ('pos500','reportes'), ('pos500','acceso_pos500');

-- -------------------------------------------------------------
-- 3. Fuera los defaults que no corresponden al modo del rol
-- -------------------------------------------------------------
DELETE rp FROM rol_permiso rp
JOIN rol r     ON r.id = rp.rol_id
JOIN permiso p ON p.id = rp.permiso_id
WHERE r.modo IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM permiso_de_modo m
                  WHERE m.modo = r.modo AND m.codigo = p.codigo);

-- -------------------------------------------------------------
-- 4. Fuera las casillas guardadas que tampoco corresponden
-- -------------------------------------------------------------
DELETE ump FROM usuario_modo_permiso ump
JOIN permiso p ON p.id = ump.permiso_id
WHERE NOT EXISTS (SELECT 1 FROM permiso_de_modo m
                  WHERE m.modo = ump.modo AND m.codigo = p.codigo);

-- -------------------------------------------------------------
-- 5. Recomputar usuario_permiso (la union efectiva que lee el login) para los
--    usuarios SIN rol global. A los Admin y Programador no se los toca: su
--    autoridad no sale de las casillas por modo.
--
--    Se hace en dos pasos —borrar lo que ya no corresponde, agregar lo que
--    falta— en vez de vaciar y rellenar: asi, si algo fallara en el medio,
--    ningun usuario queda un instante sin permisos.
-- -------------------------------------------------------------
DELETE up FROM usuario_permiso up
JOIN usuario u ON u.id = up.usuario_id
LEFT JOIN rol r ON r.id = u.rol_id
WHERE (r.id IS NULL OR r.modo IS NOT NULL)          -- no es Admin/Programador global
  AND EXISTS (SELECT 1 FROM usuario_modo_rol umr WHERE umr.usuario_id = u.id)
  AND NOT EXISTS (SELECT 1 FROM usuario_modo_permiso ump
                  WHERE ump.usuario_id = up.usuario_id AND ump.permiso_id = up.permiso_id);

INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT ump.usuario_id, ump.permiso_id FROM usuario_modo_permiso ump;

-- El acceso a cada modo NO es una casilla: se tiene por tener rol en ese modo.
-- Al recomputar hay que volver a ponerlo o el usuario pierde la puerta.
INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT umr.usuario_id, p.id
FROM usuario_modo_rol umr
JOIN permiso p ON p.codigo = CONCAT('acceso_', umr.modo);

-- Verificacion (informativa al correr el script a mano): deberia dar 0
SELECT COUNT(*) AS permisos_ajenos_que_quedan
FROM rol_permiso rp
JOIN rol r     ON r.id = rp.rol_id
JOIN permiso p ON p.id = rp.permiso_id
WHERE r.modo IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM permiso_de_modo m
                  WHERE m.modo = r.modo AND m.codigo = p.codigo);

DROP TEMPORARY TABLE IF EXISTS permiso_de_modo;
