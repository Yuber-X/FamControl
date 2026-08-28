-- =============================================================
-- FAControl — El capital pagado de cada cuota, guardado y no deducido
-- Script: 043_capital_pagado_por_cuota.sql
--
-- POR QUE
-- `cuota.monto_pagado` es UN solo acumulador. El reparto entre interes y
-- capital no se guardaba: se DEDUCIA con la regla "primero interes":
--
--     CapitalPendiente = capital - MAX(0, monto_pagado - interes)
--
-- Esa regla vale para un cobro normal, pero NO para un abono a capital, que
-- por definicion no paga interes: `DistribuirConAbono` lo manda entero contra
-- el capital. Al persistirse como un unico `monto_pagado`, la intencion se
-- pierde y el sistema vuelve a deducir "primero interes".
--
-- EL NUMERO CONCRETO (prestamo abierto de 1,000,000 al 2%, abono de 200,000):
--     capital pendiente REAL      800,000
--     capital pendiente DEDUCIDO  820,000   <-- se come 20,000 del abono
--
-- Se detecto al implementar el recalculo de interes sobre capital rebajado que
-- pidio el cliente el 2026-08-27: ese recalculo tiene que partir del capital
-- real, y un segundo abono se calcularia contra 816,000 en vez de 800,000.
--
-- LA SOLUCION
-- Guardar el capital pagado en vez de deducirlo. `pago.monto_capital` ya lleva
-- el reparto VERDADERO de cada cobro desde el primer dia, asi que la columna
-- nueva es la suma de lo que ya existe — no hay que inventar ningun dato.
--
-- EL BACKFILL ES EXACTO
-- Se suma `pago.monto_capital` por cuota, salteando los pagos borrados
-- (deleted_at). Para toda la cartera cargada hasta hoy el resultado coincide
-- con lo que se venia deduciendo, EXCEPTO en las cuotas que recibieron abono a
-- capital — que es justamente donde la deduccion estaba mal.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- -------------------------------------------------------------
-- 1. La columna
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'cuota' AND COLUMN_NAME = 'capital_pagado');
SET @sql := IF(@existe = 0,
  "ALTER TABLE cuota
     ADD COLUMN capital_pagado DECIMAL(15,2) NOT NULL DEFAULT 0.00
     COMMENT 'Capital efectivamente cubierto (043). Suma de pago.monto_capital.'
     AFTER monto_pagado",
  'SELECT "cuota.capital_pagado ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 2. Backfill desde los pagos reales
--    Se corre SIEMPRE (no solo al crear la columna): es idempotente porque
--    reescribe con la suma exacta, no acumula. Si alguna vez la columna
--    quedara desfasada, volver a correr este script la reconcilia.
-- -------------------------------------------------------------
UPDATE cuota c
LEFT JOIN (
    SELECT cuota_id, SUM(monto_capital) AS capital
    FROM pago
    WHERE deleted_at IS NULL
    GROUP BY cuota_id
) p ON p.cuota_id = c.id
SET c.capital_pagado = COALESCE(p.capital, 0.00);

-- Verificacion (informativa al correr el script a mano): cuotas donde lo
-- guardado y lo que se deducia NO coinciden. Son las que recibieron abono.
SELECT COUNT(*) AS cuotas_que_la_deduccion_calculaba_mal
FROM cuota
WHERE capital_pagado <> GREATEST(0, LEAST(capital, monto_pagado - interes));
