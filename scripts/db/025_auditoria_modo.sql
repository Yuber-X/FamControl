-- =============================================================
-- FAControl — El historial sabe de qué estancia es cada línea
-- Script: 025_auditoria_modo.sql
--
-- PEDIDO (Yuber, 2026-07-30): "en Historial debería estar limitado por su modo
-- (que solo muestre lo hecho en sus modos respectivos), o agregar en los filtros
-- los modos para identificar mejor el historial con más precisión."
--
-- Se hicieron las DOS cosas: la columna guarda en qué estancia se hizo cada
-- operación, el Historial arranca filtrado por la estancia activa, y desde el
-- filtro se puede pasar a "Todos los modos" cuando hace falta ver el conjunto.
--
-- POR QUÉ UNA COLUMNA Y NO ADIVINARLO POR LA ENTIDAD: `cliente` existe en las
-- tres estancias y `usuario` no es de ninguna. Adivinar habría clasificado mal
-- justo las filas que más se consultan.
--
-- Las filas viejas quedan en NULL y se muestran como "—": son de antes de que
-- se registrara el modo, y ocultarlas sería peor que mostrarlas sin etiqueta —
-- la auditoría no se toca ni se reescribe.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'auditoria' AND COLUMN_NAME = 'modo');
SET @sql := IF(@existe = 0,
  "ALTER TABLE auditoria
     ADD COLUMN modo ENUM('prestcontrol','dealercontrol','autocontrol','pos500') NULL AFTER usuario_id",
  'SELECT "auditoria.modo ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @existe := (SELECT COUNT(*) FROM information_schema.STATISTICS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'auditoria' AND INDEX_NAME = 'ix_auditoria_modo');
SET @sql := IF(@existe = 0,
  'ALTER TABLE auditoria ADD KEY ix_auditoria_modo (modo, id)',
  'SELECT "ix_auditoria_modo ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Verificación (informativa al correr el script a mano)
SELECT COALESCE(modo, '(sin modo — anterior a 025)') AS estancia, COUNT(*) AS lineas
FROM auditoria GROUP BY modo ORDER BY lineas DESC;
