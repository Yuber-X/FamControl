-- =============================================================
-- FAControl — Los permisos NUEVOS también llegan al Admin
-- Script: 036_permisos_de_admin_al_dia.sql
--
-- BUG REPORTADO (2026-08-01):
--   "contratos > Falta la pantalla de contratos, no existe en el menu
--    principal. hay que arreglarlo."
--
-- QUE PASO — es un error mio, y vale la pena dejarlo escrito
-- La 033 creo el permiso 'contratos' y se lo dio al rol Admin en rol_permiso.
-- Pero lo que el login LEE no es rol_permiso: es usuario_permiso, la union
-- efectiva por usuario. Esa tabla la siembran los triggers cuando se crea el
-- usuario o cambia su rol — y los usuarios ya existian desde antes.
--
-- Peor: el paso 5 de la 033 recomputaba usuario_permiso SOLO para los usuarios
-- sin rol global, con el comentario "a los Admin y Programador no se los toca:
-- su autoridad no sale de las casillas por modo". Eso es cierto para las
-- casillas por modo, pero los dejo sin el permiso nuevo. Resultado: NADIE tenia
-- 'contratos' y la pantalla se oculto para todos, incluido el dueño.
--
-- LA LECCION: crear un permiso no basta. Si ya hay usuarios, hay que darselo
-- tambien en usuario_permiso, que es de donde sale el menu.
--
-- QUE HACE ESTE SCRIPT
-- Sincroniza usuario_permiso con rol_permiso para los usuarios con rol GLOBAL
-- (Admin y Programador): lo que su rol otorga, ellos lo tienen. Es exactamente
-- lo que hace el trigger al crear un usuario, aplicado a los que ya existian.
--
-- Solo AGREGA lo que falta; no quita nada. Los overrides que el Admin haya dado
-- a mano a otros usuarios quedan intactos.
--
-- MIGRACION para bases existentes; las nuevas ya nacen bien desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
SELECT u.id, rp.permiso_id
FROM usuario u
JOIN rol r          ON r.id = u.rol_id AND r.modo IS NULL
JOIN rol_permiso rp ON rp.rol_id = r.id;

-- Verificacion (informativa al correr el script a mano): cada usuario con rol
-- global deberia quedar con TODOS los permisos del catalogo.
SELECT u.username, r.nombre AS rol,
       (SELECT COUNT(*) FROM usuario_permiso up WHERE up.usuario_id = u.id) AS permisos,
       (SELECT COUNT(*) FROM permiso) AS total_catalogo
FROM usuario u
JOIN rol r ON r.id = u.rol_id AND r.modo IS NULL;
