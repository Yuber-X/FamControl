-- =============================================================
-- FAControl — Cancelación de una venta financiada (devolución del vehículo)
-- Script: 028_venta_cancelada.sql
--
-- PEDIDO (Yuber, 2026-07-31): "agregar un botón cancelar, por si el cliente
-- decide devolver el auto... que pueda reflejarse en financiamiento de venta
-- como cancelado".
--
-- CÓMO SE RESUELVE LA PLATA: no la decide el programa. El porcentaje que el
-- negocio RETIENE de lo ya pagado (por depreciación, uso y gastos) lo digita el
-- dueño en cada cancelación, y puede dejar uno fijo por defecto en Configuración
-- — decisión de Yuber, y es la correcta: ese porcentaje lo fija el contrato de
-- cada dealer, no el software.
--
-- La venta NO se borra nunca: queda con estado 'cancelada' y su motivo, igual
-- que un préstamo anulado. El vehículo vuelve al inventario como disponible y
-- los plazos pendientes quedan en 'cancelado'.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

SET @sql := IF(
  (SELECT COUNT(*) FROM information_schema.COLUMNS
   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'venta_vehiculo'
     AND COLUMN_NAME = 'estado') = 0,
  "ALTER TABLE venta_vehiculo
     ADD COLUMN estado ENUM('activa','cancelada') NOT NULL DEFAULT 'activa' AFTER tipo_venta,
     ADD COLUMN cancelada_at DATETIME NULL,
     ADD COLUMN cancelada_motivo VARCHAR(250) NULL,
     -- Lo que el negocio se queda de lo cobrado, y lo que se le devuelve.
     -- Se guardan los DOS montos ya calculados: si mañana cambia el porcentaje
     -- por defecto, esta cancelación tiene que seguir contando lo mismo.
     ADD COLUMN retencion_porcentaje DECIMAL(5,2) NULL,
     ADD COLUMN retenido DECIMAL(15,2) NULL,
     ADD COLUMN devuelto DECIMAL(15,2) NULL",
  'SELECT "venta_vehiculo ya tiene los campos de cancelacion"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- Verificación (informativa al correr el script a mano)
SELECT COLUMN_NAME, COLUMN_TYPE
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'venta_vehiculo'
  AND COLUMN_NAME IN ('estado','cancelada_at','cancelada_motivo',
                      'retencion_porcentaje','retenido','devuelto')
ORDER BY ORDINAL_POSITION;
