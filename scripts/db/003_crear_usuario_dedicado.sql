-- =============================================================
-- FAControl — Usuario MySQL dedicado para la instalación final
-- Script: 003_crear_usuario_dedicado.sql
-- Ejecutar como root UNA sola vez en la PC del cliente.
--
-- ⚠ IMPORTANTE: cambiá 'CAMBIAR-ESTA-CLAVE' por una contraseña real
--   y usá esa misma contraseña en el App.config de FAControl.
--   La aplicación NUNCA debe correr como root en producción.
-- =============================================================

-- Fuerza UTF-8: mysql.exe asume la codificacion de la consola y corrompe los acentos.
SET NAMES utf8mb4;
CREATE USER IF NOT EXISTS 'facontrol'@'localhost'
  IDENTIFIED BY 'CAMBIAR-ESTA-CLAVE';

-- Permisos solo sobre la base de datos de la aplicación (nada más)
GRANT SELECT, INSERT, UPDATE ON facontrol_db.* TO 'facontrol'@'localhost';

-- Necesarios para respaldar desde Configuración (mysqldump)
GRANT LOCK TABLES, SHOW VIEW ON facontrol_db.* TO 'facontrol'@'localhost';

FLUSH PRIVILEGES;

-- Verificación rápida:
-- SHOW GRANTS FOR 'facontrol'@'localhost';
--
-- Nota: NO se otorga DELETE (la app nunca borra: usa soft deletes) ni DROP/ALTER.
-- La restauración de respaldos desde Configuración requiere privilegios de
-- estructura (DROP/CREATE); hacela con root o otorgá temporalmente:
--   GRANT ALL PRIVILEGES ON facontrol_db.* TO 'facontrol'@'localhost';
