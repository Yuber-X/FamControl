-- =============================================================
-- FAControl — Copia congelada del pagaré notarial
-- Script: 045_acta_congelada.sql
--
-- Pedido del cliente (2026-09-04): "Si el Pagaré Notarial fue llenado con los
-- datos correspondientes del día que se hizo y se imprimió en Nuevo Préstamo,
-- este debe de guardar esos datos por si el usuario necesitara una copia
-- exacta (no se quiere tal error)".
--
-- EL PROBLEMA QUE RESUELVE. Hasta 044, las PARTES del acta (el notario, quien
-- firma por la empresa y los dos testigos) se leían de la configuración en el
-- momento de imprimir. Si el año que viene cambia el notario, reimprimir un
-- contrato firmado en 2026 sacaba un papel DISTINTO al que el deudor firmó:
-- mismo préstamo, otro notario, otros testigos. Para un documento con valor
-- ejecutorio eso no es un detalle estético.
--
-- Es la misma regla por la que la factura congela el precio de catálogo y por
-- la que los datos del deudor viven en `prestamo` y no en `cliente`.
--
-- DISEÑO. Una fila por préstamo, creada cuando el acta se llena. Si no existe,
-- el acta se arma con la configuración vigente — que es lo correcto para los
-- préstamos anteriores a este cambio: de esos no hay copia y no se puede
-- inventar una.
--
-- Se guarda TODO el texto tal como salió impreso, incluida la ocupación y la
-- nacionalidad, porque el acta las escribe y no se pueden deducir después.
--
-- MIGRACION para bases existentes; las nuevas reciben lo mismo desde 001.
-- Idempotente.
-- =============================================================
SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS prestamo_acta (
  prestamo_id BIGINT UNSIGNED NOT NULL,

  -- La empresa y el lugar
  empresa_direccion   VARCHAR(255) NULL,
  municipio           VARCHAR(120) NULL,

  -- El notario. La matricula es del Colegio Dominicano de Notarios.
  notario_nombre       VARCHAR(150) NULL,
  notario_matricula    VARCHAR(30)  NULL,
  notario_cedula       VARCHAR(20)  NULL,
  notario_estado_civil VARCHAR(40)  NULL,
  notario_ocupacion    VARCHAR(80)  NULL,
  notario_domicilio    VARCHAR(255) NULL,
  notario_nacionalidad VARCHAR(60)  NULL,
  -- 0 = sin indicar, 1 = masculino, 2 = femenino. El acta declina en genero.
  notario_sexo         TINYINT UNSIGNED NOT NULL DEFAULT 0,

  -- Quien firma por la acreedora
  repr_nombre       VARCHAR(150) NULL,
  repr_cedula       VARCHAR(20)  NULL,
  repr_estado_civil VARCHAR(40)  NULL,
  repr_ocupacion    VARCHAR(80)  NULL,
  repr_domicilio    VARCHAR(255) NULL,
  repr_nacionalidad VARCHAR(60)  NULL,
  repr_sexo         TINYINT UNSIGNED NOT NULL DEFAULT 0,

  -- Testigo 1
  t1_nombre       VARCHAR(150) NULL,
  t1_cedula       VARCHAR(20)  NULL,
  t1_estado_civil VARCHAR(40)  NULL,
  t1_ocupacion    VARCHAR(80)  NULL,
  t1_domicilio    VARCHAR(255) NULL,
  t1_nacionalidad VARCHAR(60)  NULL,
  t1_sexo         TINYINT UNSIGNED NOT NULL DEFAULT 0,

  -- Testigo 2
  t2_nombre       VARCHAR(150) NULL,
  t2_cedula       VARCHAR(20)  NULL,
  t2_estado_civil VARCHAR(40)  NULL,
  t2_ocupacion    VARCHAR(80)  NULL,
  t2_domicilio    VARCHAR(255) NULL,
  t2_nacionalidad VARCHAR(60)  NULL,
  t2_sexo         TINYINT UNSIGNED NOT NULL DEFAULT 0,

  created_at DATETIME NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at DATETIME NULL,

  PRIMARY KEY (prestamo_id),
  CONSTRAINT fk_prestamo_acta_prestamo FOREIGN KEY (prestamo_id)
    REFERENCES prestamo (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
