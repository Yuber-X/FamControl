-- =============================================================
-- FAControl — Los gastos de importación del alta pasan al libro
-- Script: 027_gastos_importacion_al_libro.sql
--
-- PEDIDO/DUDA DE YUBER (2026-07-31): "si agregué los gastos de importación en
-- la pantalla del vehículo, ¿no debería reflejarse en el grid de Importación /
-- gastos? no muestra nada, ¿o solo soy yo?"
--
-- No era él: había DOS fuentes de verdad para el mismo número. El formulario del
-- vehículo escribía el total directo en `vehiculo.gastos_importacion`, mientras
-- que la pantalla de Importación/gastos lee el libro `vehiculo_gasto`, que
-- quedaba vacío. El total existía, pero sin una sola línea que lo explicara.
--
-- Desde ahora el alta del vehículo crea la línea correspondiente. Este script
-- arregla los vehículos que YA estaban cargados: les pone la línea que falta,
-- por el monto que tienen, sin cambiar ningún total.
--
-- Idempotente: solo toca vehículos con gastos > 0 y SIN ninguna línea en el
-- libro. Correrlo dos veces no duplica nada.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

INSERT INTO vehiculo_gasto (vehiculo_id, concepto, monto, fecha)
SELECT v.id,
       'Gastos de importación (cargados al registrar el vehículo)',
       v.gastos_importacion,
       DATE(v.created_at)
FROM vehiculo v
WHERE v.gastos_importacion > 0
  AND v.deleted_at IS NULL
  AND NOT EXISTS (SELECT 1 FROM vehiculo_gasto g WHERE g.vehiculo_id = v.id);

-- Verificación: el total de la ficha y la suma del libro tienen que coincidir
SELECT v.codigo,
       v.gastos_importacion                              AS total_en_ficha,
       COALESCE(SUM(g.monto), 0)                         AS suma_del_libro,
       IF(v.gastos_importacion = COALESCE(SUM(g.monto), 0), 'OK', 'REVISAR') AS cuadra
FROM vehiculo v
LEFT JOIN vehiculo_gasto g ON g.vehiculo_id = v.id
WHERE v.deleted_at IS NULL
GROUP BY v.id ORDER BY v.codigo;
