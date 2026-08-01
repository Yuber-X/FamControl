-- =============================================================
-- FAControl — Comision del vendedor en el punto de venta
-- Script: 037_pos_comision_vendedor.sql
--
-- Pedido del cliente (2026-08-01):
--   "agregar un textbox para escribir el porcentaje de la 'comision del
--    vendedor' (esto no puede salir en la factura, pero si en el cuadre del
--    dia, en la exportacion de excel y en 'vender' que se refleje junto al
--    subtotal); debe tener un checkbox para activar la comision del vendedor."
--
-- POR QUE VA EN pos_configuracion Y NO EN ajustes.json
-- Es del NEGOCIO, no de la terminal. Si el dueño fija 5% de comision, ese 5%
-- vale en las tres cajas; guardarlo por PC permitiria que cada caja calculara
-- una comision distinta para el mismo vendedor. Mismo criterio que el ITBIS.
--
-- OJO: DealControl tiene su propia comision en ajustes.json
-- (AjustesLocales.PorcentajeComisionVendedor) y NO se toca. Son dos negocios
-- distintos —vender autos y vender en el mostrador— y el porcentaje no tiene
-- por que ser el mismo. Mezclarlos seria justo lo que el cliente pidio evitar.
--
-- NO SALE EN LA FACTURA. La comision es un asunto entre el negocio y su
-- empleado; el cliente que compra no tiene nada que ver. Por eso vive aca y no
-- en las lineas de la venta.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'pos_configuracion'
    AND COLUMN_NAME = 'comision_activa');
SET @sql := IF(@existe = 0,
  "ALTER TABLE pos_configuracion
     ADD COLUMN comision_activa     TINYINT(1)   NOT NULL DEFAULT 0 AFTER itbis_tasa,
     ADD COLUMN comision_porcentaje DECIMAL(5,2) NOT NULL DEFAULT 0.00 AFTER comision_activa",
  'SELECT "pos_configuracion ya tiene la comision del vendedor"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
