-- =============================================================
-- Datos de PRUEBA para el semáforo de cobros / alertas / recordatorios.
-- Solo para Dev (facontrol_db). Idempotente: borra su propia semilla antes.
-- Clientes en ámbito PrestControl con cuotas por vencer, vencidas y en mora.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

-- ---- Limpieza de una corrida anterior (por marca en notas) ----
DELETE q FROM cuota q JOIN prestamo p ON p.id = q.prestamo_id WHERE p.notas = 'SEED_ALERTAS';
DELETE FROM prestamo WHERE notas = 'SEED_ALERTAS';
DELETE FROM cliente WHERE cedula IN ('402-0000001-1','402-0000002-2','402-0000003-3');

-- ================= Cliente A: EN MORA + vencido =================
INSERT INTO cliente (ambito, cedula, nombre, apellido, telefono, email)
  VALUES ('prestcontrol','402-0000001-1','Carlos','Moroso','809-555-0101','diegamer159@gmail.com');
SET @cliA := LAST_INSERT_ID();
INSERT INTO prestamo (codigo, cliente_id, monto_capital, tasa_interes, plazo_cuotas, modalidad,
                      metodo_amortizacion, fecha_inicio, estado, notas)
  VALUES ('P-0005', @cliA, 12000.00, 5.0000, 4, 'mensual', 'cuota_fija',
          DATE_SUB(CURDATE(), INTERVAL 3 MONTH), 'activo', 'SEED_ALERTAS');
SET @preA := LAST_INSERT_ID();
INSERT INTO cuota (prestamo_id, numero_cuota, fecha_vencimiento, capital, interes, monto_total, saldo_despues, monto_pagado, estado) VALUES
  (@preA, 1, DATE_SUB(CURDATE(), INTERVAL 40 DAY), 3000, 600, 3600, 9000, 0, 'pendiente'),  -- en mora (>15)
  (@preA, 2, DATE_SUB(CURDATE(), INTERVAL 10 DAY), 3000, 600, 3600, 6000, 0, 'pendiente'),  -- vencido (1-15)
  (@preA, 3, DATE_ADD(CURDATE(), INTERVAL 20 DAY), 3000, 600, 3600, 3000, 0, 'pendiente'),
  (@preA, 4, DATE_ADD(CURDATE(), INTERVAL 50 DAY), 3000, 600, 3600,    0, 0, 'pendiente');

-- ================= Cliente B: POR VENCER (≤7 días) =================
INSERT INTO cliente (ambito, cedula, nombre, apellido, telefono, email)
  VALUES ('prestcontrol','402-0000002-2','Rosa','PorVencer','809-555-0202','yubersantanalizardo@gmail.com');
SET @cliB := LAST_INSERT_ID();
INSERT INTO prestamo (codigo, cliente_id, monto_capital, tasa_interes, plazo_cuotas, modalidad,
                      metodo_amortizacion, fecha_inicio, estado, notas)
  VALUES ('P-0006', @cliB, 8000.00, 5.0000, 4, 'mensual', 'cuota_fija',
          DATE_SUB(CURDATE(), INTERVAL 3 DAY), 'activo', 'SEED_ALERTAS');
SET @preB := LAST_INSERT_ID();
INSERT INTO cuota (prestamo_id, numero_cuota, fecha_vencimiento, capital, interes, monto_total, saldo_despues, monto_pagado, estado) VALUES
  (@preB, 1, DATE_ADD(CURDATE(), INTERVAL 3 DAY),  2000, 400, 2400, 6000, 0, 'pendiente'),  -- por vencer
  (@preB, 2, DATE_ADD(CURDATE(), INTERVAL 33 DAY), 2000, 400, 2400, 4000, 0, 'pendiente'),
  (@preB, 3, DATE_ADD(CURDATE(), INTERVAL 63 DAY), 2000, 400, 2400, 2000, 0, 'pendiente'),
  (@preB, 4, DATE_ADD(CURDATE(), INTERVAL 93 DAY), 2000, 400, 2400,    0, 0, 'pendiente');

-- ============ Cliente C: MIXTO (vencido + por vencer) ============
INSERT INTO cliente (ambito, cedula, nombre, apellido, telefono, email)
  VALUES ('prestcontrol','402-0000003-3','Luis','Mixto','809-555-0303', NULL);   -- sin email a propósito
SET @cliC := LAST_INSERT_ID();
INSERT INTO prestamo (codigo, cliente_id, monto_capital, tasa_interes, plazo_cuotas, modalidad,
                      metodo_amortizacion, fecha_inicio, estado, notas)
  VALUES ('P-0007', @cliC, 10000.00, 5.0000, 4, 'mensual', 'cuota_fija',
          DATE_SUB(CURDATE(), INTERVAL 1 MONTH), 'activo', 'SEED_ALERTAS');
SET @preC := LAST_INSERT_ID();
INSERT INTO cuota (prestamo_id, numero_cuota, fecha_vencimiento, capital, interes, monto_total, saldo_despues, monto_pagado, estado) VALUES
  (@preC, 1, DATE_SUB(CURDATE(), INTERVAL 3 DAY),  2500, 500, 3000, 7500, 0, 'pendiente'),  -- vencido
  (@preC, 2, DATE_ADD(CURDATE(), INTERVAL 6 DAY),  2500, 500, 3000, 5000, 0, 'pendiente'),  -- por vencer
  (@preC, 3, DATE_ADD(CURDATE(), INTERVAL 36 DAY), 2500, 500, 3000, 2500, 0, 'pendiente'),
  (@preC, 4, DATE_ADD(CURDATE(), INTERVAL 66 DAY), 2500, 500, 3000,    0, 0, 'pendiente');

-- El contador de préstamos debe quedar por encima de P-0007
UPDATE contador SET valor = GREATEST(valor, 7) WHERE nombre = 'prestamo';

SELECT 'Seed OK' AS resultado,
       (SELECT COUNT(*) FROM cliente WHERE cedula LIKE '402-000000%') AS clientes,
       (SELECT COUNT(*) FROM prestamo WHERE notas = 'SEED_ALERTAS') AS prestamos,
       (SELECT COUNT(*) FROM cuota q JOIN prestamo p ON p.id=q.prestamo_id WHERE p.notas='SEED_ALERTAS') AS cuotas;
