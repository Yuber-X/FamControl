-- =============================================================
-- FAControl — Pagaré notarial (plantilla del cliente, 2026-08-26)
-- Script: 044_contrato_notarial.sql
--
-- Verónica (2026-08-26): "Este es el contrato notarial, los dueños lo
-- necesitan subido en el sistema, de las dos formas automático y editable".
-- Jean Carlo (2026-08-27), sobre si eran varios contratos: "Vamos hacer ese solo".
--
-- QUÉ SE GUARDA Y POR QUÉ ACÁ:
-- El acta describe a las partes con datos que el préstamo no tenía
-- (nacionalidad, estado civil, ocupación, sexo del deudor) y con condiciones
-- que hasta ahora vivían solo en el papel (cuántas cuotas en atraso hacen
-- exigible el total, días de gracia, mora, garantía completa, Registro de
-- Títulos).
--
-- Van en `prestamo` y NO en `cliente` a propósito: son la foto del contrato
-- en el momento de firmarlo. Si el deudor se casa el año que viene, el acta
-- que ya se firmó tiene que seguir diciendo "soltero" — es la misma regla por
-- la que la factura congela el precio de catálogo. Los datos que se repiten
-- entre préstamos del mismo cliente se proponen solos desde su préstamo
-- anterior, así que tampoco hay que escribirlos dos veces.
--
-- Todo es OPCIONAL: el acta se imprime igual con huecos en blanco para llenar
-- a mano, que es como se trabaja con un notario. Bloquear la impresión por un
-- campo vacío sería peor que imprimirla incompleta.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;

-- -------------------------------------------------------------
-- 1. Encabezado del acto
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='prestamo' AND COLUMN_NAME='acto_no');
SET @sql := IF(@existe=0,
  "ALTER TABLE prestamo
     ADD COLUMN acto_no        VARCHAR(30)  NULL AFTER garantia,
     ADD COLUMN folio_no       VARCHAR(30)  NULL AFTER acto_no,
     ADD COLUMN fecha_acto     DATE         NULL AFTER folio_no,
     ADD COLUMN municipio_acto VARCHAR(120) NULL AFTER fecha_acto",
  'SELECT "prestamo.acto_no ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 2. El deudor como lo describe el acta
--    deudor_sexo NO es un dato demográfico: el acta está declinada en género
--    de punta a punta (dominicano/a, domiciliado/a, EL DEUDOR / LA DEUDORA) y
--    sin esto el documento sale mal escrito la mitad de las veces.
--    0 = no indicado (usa la forma masculina genérica de la plantilla).
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='prestamo' AND COLUMN_NAME='deudor_sexo');
SET @sql := IF(@existe=0,
  "ALTER TABLE prestamo
     ADD COLUMN deudor_sexo         TINYINT UNSIGNED NOT NULL DEFAULT 0 AFTER municipio_acto,
     ADD COLUMN deudor_nacionalidad VARCHAR(60) NULL AFTER deudor_sexo,
     ADD COLUMN deudor_estado_civil VARCHAR(40) NULL AFTER deudor_nacionalidad,
     ADD COLUMN deudor_ocupacion    VARCHAR(80) NULL AFTER deudor_estado_civil",
  'SELECT "prestamo.deudor_sexo ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 3. Condiciones que el acta escribe y el sistema no tenía
-- -------------------------------------------------------------
SET @existe := (SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='prestamo' AND COLUMN_NAME='cuotas_exigibilidad');
SET @sql := IF(@existe=0,
  "ALTER TABLE prestamo
     ADD COLUMN cuotas_exigibilidad TINYINT UNSIGNED NULL AFTER deudor_ocupacion,
     ADD COLUMN dias_gracia         TINYINT UNSIGNED NULL AFTER cuotas_exigibilidad,
     ADD COLUMN mora_porcentaje     DECIMAL(5,2)     NULL AFTER dias_gracia,
     ADD COLUMN registro_titulos    VARCHAR(150)     NULL AFTER mora_porcentaje",
  'SELECT "prestamo.cuotas_exigibilidad ya existe"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;

-- -------------------------------------------------------------
-- 4. La garantía pasa a TEXT
--    La descripción legal de un inmueble no entra en 255 caracteres: la del
--    modelo que mandó el cliente (solar, superficie, designación catastral,
--    ubicación y la mejora con techo, piso y habitaciones) mide casi 400.
--    Con VARCHAR(255) MySQL la truncaba en silencio o rechazaba el INSERT
--    según el sql_mode, y en los dos casos el acta salía mal.
--    Ampliar nunca pierde datos, así que se aplica sin condición previa.
-- -------------------------------------------------------------
SET @tipo := (SELECT DATA_TYPE FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='prestamo' AND COLUMN_NAME='garantia');
SET @sql := IF(@tipo <> 'text',
  "ALTER TABLE prestamo MODIFY COLUMN garantia TEXT NULL",
  'SELECT "prestamo.garantia ya es TEXT"');
PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
