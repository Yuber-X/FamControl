-- =============================================================
-- FAControl — Permisos por pantalla (checkboxes) POR MODO
-- Script: 013_permisos_por_pantalla.sql
-- Pedido del cliente (2026-07-25): volver a los checkboxes de permisos por
-- pantalla activables por el Admin, CONVIVIENDO con los roles por modo:
-- el rol elegido precarga los checkboxes y el Admin ajusta fino por pantalla.
--
-- DISEÑO (bajo riesgo, mismo espíritu que 011):
--  * usuario_modo_permiso — el set de permisos MARCADOS de cada usuario en
--    cada modo. Se materializa al guardar (desde el rol si no se tocó nada).
--  * usuario_permiso SIGUE siendo la unión efectiva que lee el login:
--    ahora se recomputa como la unión de usuario_modo_permiso.
--  * El permiso acceso_<modo> se incluye SIEMPRE que el usuario tenga rol en
--    ese modo (la puerta de acceso no es un checkbox: se quita con "Sin acceso").
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001. Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

CREATE TABLE IF NOT EXISTS usuario_modo_permiso (
  usuario_id BIGINT UNSIGNED NOT NULL,
  modo       ENUM('prestcontrol','dealercontrol','autocontrol') NOT NULL,
  permiso_id INT UNSIGNED NOT NULL,
  PRIMARY KEY (usuario_id, modo, permiso_id),
  CONSTRAINT fk_ump_usuario FOREIGN KEY (usuario_id) REFERENCES usuario(id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_ump_permiso FOREIGN KEY (permiso_id) REFERENCES permiso(id) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- Backfill: materializar el set por modo de los usuarios existentes desde su
-- rol elegido (011), para que al abrir el formulario los checkboxes reflejen
-- lo que ya tienen. Idempotente por INSERT IGNORE.
INSERT IGNORE INTO usuario_modo_permiso (usuario_id, modo, permiso_id)
SELECT umr.usuario_id, umr.modo, rp.permiso_id
FROM usuario_modo_rol umr
JOIN rol_permiso rp ON rp.rol_id = umr.rol_id;
