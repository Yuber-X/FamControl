-- =============================================================
-- FAControl — Cierre de alquiler con motivo, y devolucion tardia
-- Script: 031_alquiler_cierre.sql
--
-- Pedido del cliente (2026-07-30):
--   "agregar dentro de 'ver detalles' un btn editar ... y otro de
--    devuelto/cancelado que solo el admin puede tener, asi si se produce un
--    error de digitacion se pueda arreglar o si se cancela quede reflejado en
--    sus detalles (debe ser llamativo)."
--   "¿los btn 'devolver' y 'cancelar' no hacen practicamente lo mismo? si es
--    asi, con un solo btn seria suficiente."
--
-- SOBRE LOS DOS BOTONES: por dentro hacen casi lo mismo (cierran el contrato y
-- liberan el vehiculo), pero NO significan lo mismo y por eso no se pueden
-- fundir en una sola accion a ciegas:
--   * DEVUELTO  -> el alquiler se cumplio, el cliente uso el auto y lo trajo.
--                  La plata se gano y cuenta como ingreso.
--   * CANCELADO -> el alquiler no llego a pasar o se corto. Puede haber que
--                  devolver dinero, y NO deberia contarse como ingreso.
-- Fundirlos perderia esa diferencia justo en los reportes. La solucion es un
-- SOLO boton en pantalla que pregunta cual de los dos es, como pidio.
--
-- QUE AGREGA
--  * cerrado_motivo / cerrado_at — por que se cerro y cuando. Sin el motivo, un
--    alquiler cancelado en el historial no explica nada.
--  * cerrado_por — quien lo cerro. Cerrar libera un vehiculo y puede implicar
--    devolver plata: tiene que poder rendirse cuentas.
--  * dias_reales / monto_final — cuando el cliente devuelve TARDE o antes, lo
--    pactado y lo que realmente corresponde cobrar dejan de coincidir. Sin esto
--    el sistema seguiria mostrando el monto pactado como si nada.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'alquiler' AND COLUMN_NAME = 'cerrado_motivo');
SET @sql := IF(@existe = 0,
  "ALTER TABLE alquiler
     ADD COLUMN cerrado_motivo VARCHAR(250)   NULL AFTER estado,
     ADD COLUMN cerrado_at     DATETIME       NULL AFTER cerrado_motivo,
     ADD COLUMN cerrado_por    BIGINT UNSIGNED NULL AFTER cerrado_at,
     ADD COLUMN dias_reales    INT UNSIGNED   NULL AFTER dias,
     ADD COLUMN monto_final    DECIMAL(15,2)  NULL AFTER monto_total",
  'SELECT "alquiler ya tiene las columnas de cierre"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

SET @existe := (SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'alquiler'
    AND CONSTRAINT_NAME = 'fk_alquiler_cerrado_por');
SET @sql := IF(@existe = 0,
  "ALTER TABLE alquiler
     ADD CONSTRAINT fk_alquiler_cerrado_por FOREIGN KEY (cerrado_por)
     REFERENCES usuario (id) ON DELETE SET NULL",
  'SELECT "fk_alquiler_cerrado_por ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Back-fill de lo ya cerrado: se completa lo que se puede deducir y nada mas.
-- El motivo NO se inventa; queda NULL y la pantalla muestra "no indicado", que
-- es la verdad. Rellenarlo con un texto generico haria pasar por dato algo que
-- nadie escribio.
UPDATE alquiler
SET dias_reales = dias,
    monto_final = monto_total
WHERE estado = 'finalizado' AND dias_reales IS NULL;
