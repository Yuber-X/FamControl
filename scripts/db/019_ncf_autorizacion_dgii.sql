-- =============================================================
-- FAControl — Secuencia de comprobantes fiscales autorizada (real)
-- Script: 019_ncf_autorizacion_dgii.sql
--
-- Origen del dato: constancia de la Oficina Virtual de la DGII que el cliente
-- envió el 29/07/2026 (captura en "Freelancer - Claude Active\FamControl").
--
--   Solicitud:          6009897365 · 29/07/2026 · APROBADA
--   No. Autorización:   6005407803
--   Tipo comprobante:   FACTURA DE CRÉDITO FISCAL  → prefijo B01
--   Cantidad solicitada: 100      Cantidad APROBADA: 15
--   Número desde:       B0100000001
--   Número hasta:       B0100000015
--   Fecha vencimiento:  31/12/2027
--   Tipo de uso:        SISTEMAS
--
-- OJO con el número de autorización: la app no lo guarda (no hace falta para
-- emitir), pero el contador lo pide. Queda anotado acá y en docs/NCF-DGII.md.
--
-- IMPORTANTE — solo son 15 comprobantes. La DGII aprobó 15 de los 100 pedidos
-- porque la empresa arranca. La app avisa en Configuración → Comprobante fiscal
-- cuando quedan pocos, y BLOQUEA la asignación al agotarse en vez de repetir un
-- número (repetir sería la falta grave ante la DGII).
--
-- Idempotente: se puede correr sobre una base que ya tiene la secuencia. NO
-- pisa la columna `proxima` si ya se consumieron comprobantes — eso retrocedería
-- la numeración y haría que se emita dos veces el mismo NCF.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

INSERT INTO ncf_secuencia (prefijo, largo, proxima, fin_rango, vencimiento, activo)
VALUES ('B01', 8, 1, 15, '2027-12-31', 1)
ON DUPLICATE KEY UPDATE
  largo       = 8,
  -- Se queda con la MAYOR: si la instalación ya emitió comprobantes, la próxima
  -- no vuelve atrás.
  proxima     = GREATEST(proxima, 1),
  fin_rango   = 15,
  vencimiento = '2027-12-31',
  activo      = 1,
  updated_at  = UTC_TIMESTAMP();

-- Verificación (informativa al correr el script a mano)
SELECT prefijo, largo, proxima, fin_rango, vencimiento, activo,
       CONCAT(prefijo, LPAD(proxima, largo, '0'))               AS proximo_ncf,
       GREATEST(0, fin_rango - proxima + 1)                     AS disponibles
FROM ncf_secuencia
WHERE prefijo = 'B01';
