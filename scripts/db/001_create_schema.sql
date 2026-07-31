-- =============================================================
-- FAControl — Esquema inicial
-- Script: 001_create_schema.sql
-- Motor: MySQL 8.0+ · InnoDB · utf8mb4_unicode_ci
-- Regla: dinero en DECIMAL(15,2), fechas DATETIME en UTC
-- =============================================================

-- Fuerza UTF-8: mysql.exe asume la codificacion de la consola y corrompe los acentos.
SET NAMES utf8mb4;
CREATE DATABASE IF NOT EXISTS facontrol_db
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE facontrol_db;

-- -------------------------------------------------------------
-- rol: catálogo (Admin / Supervisor / Cobrador)
-- Multicuentas — pedido del cliente 2026-07-16.
-- -------------------------------------------------------------
CREATE TABLE rol (
  id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
  nombre      VARCHAR(50)  NOT NULL,
  -- Modo al que pertenece el rol (roles por modo, 011); NULL = global (Admin)
  modo        ENUM('prestcontrol','dealercontrol','autocontrol','pos500') NULL,
  descripcion VARCHAR(200) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_rol_nombre_modo (nombre, modo)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- permiso: catálogo por módulo/acción
-- -------------------------------------------------------------
CREATE TABLE permiso (
  id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo      VARCHAR(50)  NOT NULL,             -- ej: 'prestamos_crear'
  nombre      VARCHAR(100) NOT NULL,
  descripcion VARCHAR(200) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_permiso_codigo (codigo)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- rol_permiso: qué otorga cada rol (los defaults por rol)
-- -------------------------------------------------------------
CREATE TABLE rol_permiso (
  rol_id     INT UNSIGNED NOT NULL,
  permiso_id INT UNSIGNED NOT NULL,
  PRIMARY KEY (rol_id, permiso_id),
  CONSTRAINT fk_rolperm_rol FOREIGN KEY (rol_id)
    REFERENCES rol (id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_rolperm_permiso FOREIGN KEY (permiso_id)
    REFERENCES permiso (id) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- usuario: empleados del negocio (MULTIUSUARIO desde 2026-07-16)
-- -------------------------------------------------------------
CREATE TABLE usuario (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  username      VARCHAR(50)  NOT NULL,
  password_hash VARCHAR(100) NOT NULL,           -- BCrypt cost 12
  nombre        VARCHAR(100) NOT NULL,
  apellido      VARCHAR(100) NULL,
  rol_id        INT UNSIGNED NULL,
  activo        TINYINT(1)   NOT NULL DEFAULT 1,
  created_at    DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at    DATETIME     NULL,
  last_login_at DATETIME     NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_usuario_username (username),
  CONSTRAINT fk_usuario_rol FOREIGN KEY (rol_id)
    REFERENCES rol (id) ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- usuario_permiso: permisos EFECTIVOS por usuario.
-- Los triggers los siembran desde rol_permiso; el Admin los ajusta
-- uno por uno (overrides) sin tocar el rol.
-- -------------------------------------------------------------
CREATE TABLE usuario_permiso (
  usuario_id BIGINT UNSIGNED NOT NULL,
  permiso_id INT UNSIGNED    NOT NULL,
  PRIMARY KEY (usuario_id, permiso_id),
  CONSTRAINT fk_usuperm_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_usuperm_permiso FOREIGN KEY (permiso_id)
    REFERENCES permiso (id) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- usuario_modo_rol: rol del usuario en CADA modo (roles por modo, 011).
-- Acceso a un modo = fila presente. El Admin (usuario.rol_id = Admin) es global
-- y no necesita filas. usuario_permiso sigue siendo la unión efectiva.
-- -------------------------------------------------------------
CREATE TABLE usuario_modo_rol (
  usuario_id BIGINT UNSIGNED NOT NULL,
  modo       ENUM('prestcontrol','dealercontrol','autocontrol','pos500') NOT NULL,
  rol_id     INT UNSIGNED NOT NULL,
  PRIMARY KEY (usuario_id, modo),
  CONSTRAINT fk_umr_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_umr_rol FOREIGN KEY (rol_id)
    REFERENCES rol (id) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- usuario_modo_permiso: permisos por pantalla MARCADOS por modo (013).
-- El rol elegido precarga el set; el Admin ajusta fino por checkbox.
-- usuario_permiso sigue siendo la unión efectiva que lee el login.
-- -------------------------------------------------------------
CREATE TABLE usuario_modo_permiso (
  usuario_id BIGINT UNSIGNED NOT NULL,
  modo       ENUM('prestcontrol','dealercontrol','autocontrol','pos500') NOT NULL,
  permiso_id INT UNSIGNED NOT NULL,
  PRIMARY KEY (usuario_id, modo, permiso_id),
  CONSTRAINT fk_ump_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT fk_ump_permiso FOREIGN KEY (permiso_id)
    REFERENCES permiso (id) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- sesion: registro de logins/logouts
-- -------------------------------------------------------------
CREATE TABLE sesion (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  usuario_id BIGINT UNSIGNED NOT NULL,
  login_at   DATETIME    NOT NULL DEFAULT (UTC_TIMESTAMP()),
  logout_at  DATETIME    NULL,
  ip_local   VARCHAR(45) NULL,
  PRIMARY KEY (id),
  KEY ix_sesion_usuario (usuario_id),
  CONSTRAINT fk_sesion_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- cliente: personas a las que se les presta (soft delete)
-- -------------------------------------------------------------
CREATE TABLE cliente (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  -- Estancia dueña de la ficha: los datos NO se mezclan entre modos
  -- (decisión Yuber 2026-07-18). Cédula única POR ámbito, no global.
  ambito     ENUM('prestcontrol','dealercontrol','autocontrol')
               NOT NULL DEFAULT 'prestcontrol',
  cedula     VARCHAR(13)  NOT NULL,               -- formato 001-1234567-8
  nombre     VARCHAR(100) NOT NULL,
  apellido   VARCHAR(100) NOT NULL,
  telefono   VARCHAR(20)  NULL,
  direccion  VARCHAR(255) NULL,
  email      VARCHAR(150) NULL,
  notas      TEXT         NULL,
  created_at DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at DATETIME     NULL,
  deleted_at DATETIME     NULL,                   -- soft delete: leer con deleted_at IS NULL
  PRIMARY KEY (id),
  UNIQUE KEY uq_cliente_ambito_cedula (ambito, cedula),
  KEY ix_cliente_nombre (nombre, apellido)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- prestamo: contrato de préstamo
-- codigo: correlativo visible tipo P-0001 (mockup)
-- tasa_interes: tasa MENSUAL en % (convención prestamista RD);
--   se convierte a tasa por período según modalidad al calcular
-- -------------------------------------------------------------
-- -------------------------------------------------------------
-- vehiculo: inventario del dealer (DealerControl — Tier 5).
-- El vehículo como ACTIVO: nace aquí; AutoControl lo consume por FK
-- (prestamo.vehiculo_id). Va ANTES de prestamo por esa dependencia.
--   costo_total = costo_adquisicion + gastos_importacion
--   ganancia    = precio_venta - costo_total  (se calcula, no se guarda)
-- Soft delete vía deleted_at. Código secuencial V-0001 (contador 'vehiculo').
-- -------------------------------------------------------------
CREATE TABLE vehiculo (
  id                 BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo             VARCHAR(20)   NOT NULL,               -- V-0001
  vin                VARCHAR(17)   NULL,                   -- chasis / VIN
  marca              VARCHAR(50)   NOT NULL,
  modelo             VARCHAR(50)   NOT NULL,
  anio               SMALLINT UNSIGNED NULL,
  color              VARCHAR(30)   NULL,
  placa              VARCHAR(15)   NULL,                   -- chapa
  matricula          VARCHAR(30)   NULL,                   -- nro. certificado de matrícula DGII (015)
  tipo               ENUM('sedan','suv','jeepeta','camioneta','camion','motor','otro')
                       NOT NULL DEFAULT 'otro',
  kilometraje        INT UNSIGNED  NULL,
  costo_adquisicion  DECIMAL(15,2) NOT NULL DEFAULT 0.00,  -- lo que costó comprarlo
  gastos_importacion DECIMAL(15,2) NOT NULL DEFAULT 0.00,  -- aduana, flete, preparación
  precio_venta       DECIMAL(15,2) NOT NULL DEFAULT 0.00,  -- precio de lista
  estado             ENUM('disponible','reservado','vendido','alquilado','baja')
                       NOT NULL DEFAULT 'disponible',
  notas              TEXT          NULL,
  created_at         DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at         DATETIME      NULL,
  deleted_at         DATETIME      NULL,                   -- soft delete
  PRIMARY KEY (id),
  UNIQUE KEY uq_vehiculo_codigo (codigo),
  KEY ix_vehiculo_estado (estado),
  KEY ix_vehiculo_vin (vin)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- prestamo: contrato de préstamo (PrestControl y AutoControl)
-- -------------------------------------------------------------
CREATE TABLE prestamo (
  id                  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo              VARCHAR(10)   NOT NULL,     -- P-0001
  ncf                 VARCHAR(19)   NULL,         -- comprobante fiscal (012): registrado o asignado de ncf_secuencia
  cliente_id          BIGINT UNSIGNED NOT NULL,
  vehiculo_id         BIGINT UNSIGNED NULL,       -- AutoControl: vehículo en garantía (NULL = préstamo personal)
  monto_capital       DECIMAL(15,2) NOT NULL,
  moneda              CHAR(3)       NOT NULL DEFAULT 'DOP',
  tasa_interes        DECIMAL(8,4)  NOT NULL,     -- % mensual, ej. 10.0000
  plazo_cuotas        INT UNSIGNED  NOT NULL,
  modalidad           ENUM('diaria','semanal','quincenal','mensual','pago_unico') NOT NULL,
  -- solo_interes (021): prestamo ABIERTO, paga solo interes y el capital queda abierto
  metodo_amortizacion ENUM('frances','cuota_fija','solo_interes') NOT NULL DEFAULT 'cuota_fija',
  fecha_inicio        DATE          NOT NULL,     -- fecha del primer pago (hora local del negocio)
  garantia            VARCHAR(255)  NULL,
  estado              ENUM('activo','pagado','cancelado') NOT NULL DEFAULT 'activo',
  notas               TEXT          NULL,
  created_at          DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at          DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_prestamo_codigo (codigo),
  UNIQUE KEY uq_prestamo_ncf (ncf),
  KEY ix_prestamo_cliente (cliente_id),
  KEY ix_prestamo_vehiculo (vehiculo_id),
  KEY ix_prestamo_estado (estado),
  CONSTRAINT fk_prestamo_cliente FOREIGN KEY (cliente_id)
    REFERENCES cliente (id) ON DELETE RESTRICT,
  CONSTRAINT fk_prestamo_vehiculo FOREIGN KEY (vehiculo_id)
    REFERENCES vehiculo (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- cuota: cada cuota individual del préstamo
-- Nota: se agrega 'cancelada' al ENUM porque cancelar un préstamo
-- marca sus cuotas restantes como canceladas (regla §8.4 CLAUDE.md
-- del proyecto) sin borrarlas jamás.
-- -------------------------------------------------------------
CREATE TABLE cuota (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  prestamo_id       BIGINT UNSIGNED NOT NULL,
  numero_cuota      INT UNSIGNED  NOT NULL,
  fecha_vencimiento DATE          NOT NULL,
  capital           DECIMAL(15,2) NOT NULL,
  interes           DECIMAL(15,2) NOT NULL,
  monto_total       DECIMAL(15,2) NOT NULL,
  saldo_despues     DECIMAL(15,2) NOT NULL,       -- saldo de capital tras pagar esta cuota
  monto_pagado      DECIMAL(15,2) NOT NULL DEFAULT 0.00, -- acumulado de abonos aplicados
  estado            ENUM('pendiente','pagada','vencida','en_mora','cancelada') NOT NULL DEFAULT 'pendiente',
  created_at        DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at        DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_cuota_prestamo_numero (prestamo_id, numero_cuota),
  KEY ix_cuota_vencimiento (fecha_vencimiento, estado),
  CONSTRAINT fk_cuota_prestamo FOREIGN KEY (prestamo_id)
    REFERENCES prestamo (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pago: abono a una cuota (soft delete; un pago NUNCA se modifica,
-- errores se corrigen con pago compensatorio negativo)
-- monto_interes/monto_capital: desglose del abono (primero interés,
-- luego capital) — necesario para abonos parciales
-- -------------------------------------------------------------
CREATE TABLE pago (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  cuota_id      BIGINT UNSIGNED NOT NULL,
  numero_recibo VARCHAR(12)   NOT NULL,           -- R-000001, secuencial atómico, nunca se reutiliza
  fecha_pago    DATETIME      NOT NULL,           -- UTC
  monto_pagado  DECIMAL(15,2) NOT NULL,
  monto_interes DECIMAL(15,2) NOT NULL DEFAULT 0.00,
  monto_capital DECIMAL(15,2) NOT NULL DEFAULT 0.00,
  metodo_pago   ENUM('efectivo','transferencia','cheque','otro') NOT NULL DEFAULT 'efectivo',
  notas         TEXT          NULL,
  created_by    BIGINT UNSIGNED NULL,             -- quién cobró (para el reporte por usuario)
  created_at    DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at    DATETIME      NULL,
  deleted_at    DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_pago_recibo (numero_recibo),
  KEY ix_pago_cuota (cuota_id),
  KEY ix_pago_fecha (fecha_pago),
  CONSTRAINT fk_pago_cuota FOREIGN KEY (cuota_id)
    REFERENCES cuota (id) ON DELETE RESTRICT,
  CONSTRAINT fk_pago_usuario FOREIGN KEY (created_by)
    REFERENCES usuario (id) ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- auditoria: log inmutable de operaciones (nunca se borra)
-- -------------------------------------------------------------
CREATE TABLE auditoria (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  usuario_id  BIGINT UNSIGNED NOT NULL,
  -- Estancia donde se hizo la operacion (025). NULL = linea anterior a esa
  -- migracion; se muestra como "—" y nunca se reescribe.
  modo        ENUM('prestcontrol','dealercontrol','autocontrol','pos500') NULL,
  entidad     VARCHAR(50)  NOT NULL,              -- 'cliente', 'prestamo', 'cuota', 'pago', 'usuario'
  entidad_id  BIGINT UNSIGNED NULL,
  accion      ENUM('crear','modificar','eliminar','consultar','login','logout','anular') NOT NULL,
  descripcion TEXT         NULL,
  ip_local    VARCHAR(45)  NULL,
  timestamp   DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  PRIMARY KEY (id),
  KEY ix_auditoria_entidad (entidad, entidad_id),
  KEY ix_auditoria_timestamp (timestamp),
  KEY ix_auditoria_modo (modo, id),
  CONSTRAINT fk_auditoria_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- contador: correlativos atómicos (numero_recibo, codigo prestamo)
-- Uso: SELECT valor FROM contador WHERE nombre=? FOR UPDATE;
--      UPDATE contador SET valor = valor + 1 ...  (misma transacción)
-- -------------------------------------------------------------
CREATE TABLE contador (
  nombre VARCHAR(30)     NOT NULL,
  valor  BIGINT UNSIGNED NOT NULL DEFAULT 0,
  PRIMARY KEY (nombre)
) ENGINE=InnoDB;

INSERT INTO contador (nombre, valor) VALUES
  ('recibo', 0),
  ('prestamo', 0),
  ('vehiculo', 0),
  ('venta', 0),
  ('alquiler', 0),
  ('recibo_venta', 0);   -- recibos de plazos del dealer (016): RV-000001

-- -------------------------------------------------------------
-- Secuencia de comprobantes fiscales (012): prefijo autorizado por la DGII
-- (B02 tradicional / E32 e-CF), próxima secuencia, fin de rango y vencimiento.
-- La reserva es atómica (FOR UPDATE); un NCF consumido nunca se reusa.
-- -------------------------------------------------------------
CREATE TABLE ncf_secuencia (
  id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
  -- Una secuencia POR MODO (030). Un negocio de varios rubros puede tener una
  -- autorizacion de la DGII por estancia, o hasta otro RNC; compartir el rango
  -- entregaria comprobantes que la DGII espera de un unico libro de ventas.
  modo        ENUM('prestcontrol','dealercontrol','autocontrol','pos500') NOT NULL,
  prefijo     VARCHAR(5)  NOT NULL,
  largo       TINYINT UNSIGNED NOT NULL DEFAULT 8,
  proxima     BIGINT UNSIGNED NOT NULL DEFAULT 1,
  fin_rango   BIGINT UNSIGNED NULL,
  vencimiento DATE NULL,
  activo      TINYINT(1) NOT NULL DEFAULT 1,
  created_at  DATETIME NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at  DATETIME NULL,
  PRIMARY KEY (id),
  -- Dos estancias pueden usar el mismo prefijo B02 con rangos distintos: es lo
  -- normal cuando la DGII autoriza por separado.
  UNIQUE KEY uq_ncf_secuencia_modo_prefijo (modo, prefijo)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- venta_vehiculo: venta al contado del dealer (DealerControl — Tier 5).
-- Al vender, el vehículo pasa a 'vendido' (Service, en transacción).
-- -------------------------------------------------------------
CREATE TABLE venta_vehiculo (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo      VARCHAR(20)   NOT NULL,              -- VC-0001
  vehiculo_id BIGINT UNSIGNED NOT NULL,
  cliente_id  BIGINT UNSIGNED NOT NULL,
  fecha_venta DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  precio      DECIMAL(15,2) NOT NULL,
  -- Financiamiento del dealer (016): contado, por plazos o separación/reserva
  tipo_venta   ENUM('contado','plazos','separacion') NOT NULL DEFAULT 'contado',
  -- Cancelacion de la venta (028): el cliente devolvio el vehiculo. La venta
  -- NO se borra, queda con su motivo y con los montos ya calculados — si
  -- manana cambia el porcentaje por defecto, esta sigue contando lo mismo.
  estado            ENUM('activa','cancelada') NOT NULL DEFAULT 'activa',
  cancelada_at      DATETIME      NULL,
  cancelada_motivo  VARCHAR(250)  NULL,
  retencion_porcentaje DECIMAL(5,2) NULL,
  retenido          DECIMAL(15,2) NULL,
  devuelto          DECIMAL(15,2) NULL,
  inicial      DECIMAL(15,2) NOT NULL DEFAULT 0.00,   -- anticipo recibido al firmar
  fecha_limite DATE          NULL,                    -- separación: vence a los N días (15 por default)
  metodo_pago ENUM('efectivo','transferencia','cheque','otro') NOT NULL DEFAULT 'efectivo',
  notas       TEXT          NULL,
  created_at  DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by  BIGINT UNSIGNED NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_venta_codigo (codigo),
  KEY ix_venta_vehiculo (vehiculo_id),
  KEY ix_venta_cliente (cliente_id),
  CONSTRAINT fk_venta_vehiculo FOREIGN KEY (vehiculo_id) REFERENCES vehiculo (id) ON DELETE RESTRICT,
  CONSTRAINT fk_venta_cliente  FOREIGN KEY (cliente_id)  REFERENCES cliente (id)  ON DELETE RESTRICT,
  CONSTRAINT fk_venta_usuario  FOREIGN KEY (created_by)  REFERENCES usuario (id)  ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- venta_plazo: calendario de pagos pactado del dealer (016).
-- Financiamiento propio SIN interés (el interés vive en AutoControl).
-- -------------------------------------------------------------
CREATE TABLE venta_plazo (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  venta_id          BIGINT UNSIGNED NOT NULL,
  numero            INT UNSIGNED  NOT NULL,
  fecha_vencimiento DATE          NOT NULL,
  monto             DECIMAL(15,2) NOT NULL,
  monto_pagado      DECIMAL(15,2) NOT NULL DEFAULT 0.00,
  estado            ENUM('pendiente','pagado','cancelado') NOT NULL DEFAULT 'pendiente',
  created_at        DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at        DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_venta_plazo (venta_id, numero),
  CONSTRAINT fk_plazo_venta FOREIGN KEY (venta_id)
    REFERENCES venta_vehiculo (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- venta_plazo_pago: abonos a los plazos, cada uno con su recibo (016).
-- -------------------------------------------------------------
CREATE TABLE venta_plazo_pago (
  id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  plazo_id       BIGINT UNSIGNED NOT NULL,
  numero_recibo  VARCHAR(20)   NOT NULL,
  fecha_pago     DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  monto          DECIMAL(15,2) NOT NULL,
  metodo_pago    ENUM('efectivo','transferencia','cheque','otro') NOT NULL DEFAULT 'efectivo',
  notas          TEXT          NULL,
  created_at     DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by     BIGINT UNSIGNED NULL,
  deleted_at     DATETIME      NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_plazo_pago_recibo (numero_recibo),
  KEY ix_plazo_pago_plazo (plazo_id),
  CONSTRAINT fk_plazo_pago_plazo   FOREIGN KEY (plazo_id)   REFERENCES venta_plazo (id) ON DELETE RESTRICT,
  CONSTRAINT fk_plazo_pago_usuario FOREIGN KEY (created_by) REFERENCES usuario (id)     ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- alquiler: rent a car (DealerControl — Tier 5).
-- -------------------------------------------------------------
CREATE TABLE alquiler (
  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo           VARCHAR(20)   NOT NULL,          -- AL-0001
  vehiculo_id      BIGINT UNSIGNED NOT NULL,
  cliente_id       BIGINT UNSIGNED NOT NULL,
  fecha_inicio     DATE          NOT NULL,
  fecha_fin        DATE          NOT NULL,           -- devolución pactada
  fecha_devolucion DATE          NULL,               -- devolución real
  tarifa_dia       DECIMAL(15,2) NOT NULL,
  dias             INT UNSIGNED  NOT NULL,
  -- Dias y monto REALES al cerrar (031): si el cliente devuelve tarde o antes,
  -- lo pactado y lo que corresponde cobrar dejan de coincidir.
  dias_reales      INT UNSIGNED  NULL,
  monto_total      DECIMAL(15,2) NOT NULL,
  monto_final      DECIMAL(15,2) NULL,
  estado           ENUM('activo','finalizado','cancelado') NOT NULL DEFAULT 'activo',
  -- Por que, cuando y quien cerro el contrato (031). Sin el motivo, un alquiler
  -- cancelado en el historial no explica nada.
  cerrado_motivo   VARCHAR(250)  NULL,
  cerrado_at       DATETIME      NULL,
  cerrado_por      BIGINT UNSIGNED NULL,
  notas            TEXT          NULL,
  created_at       DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by       BIGINT UNSIGNED NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_alquiler_codigo (codigo),
  KEY ix_alquiler_vehiculo (vehiculo_id),
  KEY ix_alquiler_estado (estado),
  CONSTRAINT fk_alquiler_vehiculo FOREIGN KEY (vehiculo_id) REFERENCES vehiculo (id) ON DELETE RESTRICT,
  CONSTRAINT fk_alquiler_cliente  FOREIGN KEY (cliente_id)  REFERENCES cliente (id)  ON DELETE RESTRICT,
  CONSTRAINT fk_alquiler_usuario  FOREIGN KEY (created_by)  REFERENCES usuario (id)  ON DELETE SET NULL,
  CONSTRAINT fk_alquiler_cerrado_por FOREIGN KEY (cerrado_por) REFERENCES usuario (id) ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- vehiculo_gasto: gestión de importación (gastos detallados).
-- La suma se refleja en vehiculo.gastos_importacion (lo mantiene el Service).
-- -------------------------------------------------------------
CREATE TABLE vehiculo_gasto (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  vehiculo_id BIGINT UNSIGNED NOT NULL,
  concepto    VARCHAR(100)  NOT NULL,
  monto       DECIMAL(15,2) NOT NULL,
  fecha       DATE          NOT NULL,
  created_at  DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by  BIGINT UNSIGNED NULL,
  PRIMARY KEY (id),
  KEY ix_gasto_vehiculo (vehiculo_id),
  CONSTRAINT fk_gasto_vehiculo FOREIGN KEY (vehiculo_id) REFERENCES vehiculo (id) ON DELETE CASCADE,
  CONSTRAINT fk_gasto_usuario  FOREIGN KEY (created_by)  REFERENCES usuario (id)  ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- vehiculo_reparacion: historial de reparaciones/mantenimientos (015).
-- Se muestra en la ficha del vehículo; soft delete.
-- -------------------------------------------------------------
CREATE TABLE vehiculo_reparacion (
  id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  vehiculo_id BIGINT UNSIGNED NOT NULL,
  fecha       DATE          NOT NULL,
  detalle     VARCHAR(500)  NOT NULL,
  costo       DECIMAL(15,2) NOT NULL DEFAULT 0,
  created_at  DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by  BIGINT UNSIGNED NULL,
  deleted_at  DATETIME      NULL,
  PRIMARY KEY (id),
  KEY ix_reparacion_vehiculo (vehiculo_id),
  CONSTRAINT fk_reparacion_vehiculo FOREIGN KEY (vehiculo_id)
    REFERENCES vehiculo (id) ON DELETE RESTRICT,
  CONSTRAINT fk_reparacion_usuario FOREIGN KEY (created_by)
    REFERENCES usuario (id) ON DELETE SET NULL
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- documento_venta: expediente digital del contrato (018).
-- El ARCHIVO vive en disco (<app>\expedientes\<venta_id>\); acá va su ficha.
-- Ver 018_expediente_documentos.sql para el porqué del diseño.
-- -------------------------------------------------------------
CREATE TABLE documento (
  id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  -- De que cuelga el expediente: una venta del dealer, un prestamo (026) o un
  -- alquiler (032). Va exactamente UNO; lo garantiza ck_documento_un_dueno_3.
  venta_id      BIGINT UNSIGNED NULL,
  prestamo_id   BIGINT UNSIGNED NULL,
  alquiler_id   BIGINT UNSIGNED NULL,
  nombre        VARCHAR(255) NOT NULL,
  -- Ruta relativa a la carpeta de expedientes: 'ventas/<id>/...' o
  -- 'prestamos/<id>/...'. Se guarda, no se recalcula.
  ruta_relativa VARCHAR(500) NOT NULL,
  extension     VARCHAR(10)  NOT NULL,
  tamano_bytes  BIGINT UNSIGNED NOT NULL DEFAULT 0,
  tipo          ENUM('otro','factura_escaneada','contrato','identificacion','pagare','intimacion')
                  NOT NULL DEFAULT 'otro',
  notas         TEXT         NULL,
  created_at    DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  created_by    BIGINT UNSIGNED NULL,
  deleted_at    DATETIME     NULL,
  PRIMARY KEY (id),
  KEY ix_documento_venta (venta_id),
  KEY ix_documento_prestamo (prestamo_id),
  KEY ix_documento_alquiler (alquiler_id),
  CONSTRAINT fk_documento_venta FOREIGN KEY (venta_id)
    REFERENCES venta_vehiculo (id) ON DELETE RESTRICT,
  CONSTRAINT fk_documento_prestamo FOREIGN KEY (prestamo_id)
    REFERENCES prestamo (id) ON DELETE RESTRICT,
  CONSTRAINT fk_documento_alquiler FOREIGN KEY (alquiler_id)
    REFERENCES alquiler (id) ON DELETE RESTRICT,
  CONSTRAINT fk_documento_usuario FOREIGN KEY (created_by)
    REFERENCES usuario (id) ON DELETE SET NULL,
  -- Suma de los tres = 1: la forma directa de decir "uno y solo uno". Con <>
  -- encadenados, tres nulos y tres llenos se comportarian igual.
  CONSTRAINT ck_documento_un_dueno_3
    CHECK ((venta_id IS NOT NULL) + (prestamo_id IS NOT NULL) + (alquiler_id IS NOT NULL) = 1)
) ENGINE=InnoDB;

-- =============================================================
-- Catálogo de roles y permisos (multicuentas — cliente 2026-07-16)
-- Va acá y no en el seed porque NO son datos de prueba: sin esto la
-- aplicación no puede autenticar a nadie.
-- =============================================================
-- Roles POR MODO (011): Admin es global (modo NULL); los demás pertenecen a un modo.
-- Programador (017) es global y blindado: solo otro Programador puede crearlo,
-- editarlo o asignarlo. No se siembra ninguna cuenta con ese rol (se crea con
-- el código 3 del launcher).
INSERT INTO rol (nombre, modo, descripcion) VALUES
  ('Admin',      NULL,           'Control total de los tres modos'),
  ('Programador', NULL,          'Autoridad total del sistema — reservado al desarrollador. Solo otro Programador puede crearlo o modificarlo.'),
  ('Supervisor', 'prestcontrol', 'Opera y supervisa la cartera de préstamos'),
  ('Cobrador',   'prestcontrol', 'Cobra en la calle: registra pagos y consulta su cartera'),
  ('Encargado',  'dealercontrol','Gestiona el dealer: inventario, ventas, alquileres y gastos'),
  ('Vendedor',   'dealercontrol','Vende y alquila; consulta el inventario'),
  ('Encargado',  'autocontrol',  'Gestiona las ventas financiadas: crédito, cobros y contratos'),
  ('Vendedor',   'autocontrol',  'Crea ventas financiadas y cobra'),
  -- POS-500 (022): punto de venta. Sus DATOS viven en pos500_db, pero los
  -- roles y permisos son compartidos y viven aca.
  ('Supervisor', 'pos500',       'Operacion completa del piso de venta, sin configuracion ni usuarios'),
  ('Cajero',     'pos500',       'Ventas, consulta de clientes, su propio cuadre y sus comprobantes'),
  ('Vendedor',   'pos500',       'Ventas y gestion de clientes');

INSERT INTO permiso (codigo, nombre, descripcion) VALUES
  ('panel',               'Panel',                     'KPIs de la cartera'),
  ('clientes',            'Clientes (ver)',            'Consulta de clientes'),
  ('clientes_editar',     'Clientes (crear/editar)',   'Alta, edición y baja de clientes'),
  ('prestamos',           'Préstamos (ver)',           'Consulta de préstamos y su amortización'),
  ('prestamos_crear',     'Préstamos (crear)',         'Crear préstamos nuevos'),
  ('prestamos_autorizar', 'Autorizar préstamos',       'Aprobar préstamos nuevos'),
  ('prestamos_cancelar',  'Cancelar préstamos',        'Permiso especial: cancelación con auditoría'),
  -- Corregir contratos ya registrados (029). Solo mientras no haya cobros:
  -- con un recibo emitido, cambiar el contrato haria mentir a ese papel.
  ('prestamos_editar',    'Préstamos (editar)',        'Corregir un préstamo ya registrado (errores de digitación)'),
  ('cobros',              'Cobros',                    'Registrar pagos y emitir recibos'),
  -- Almacen de contratos (033): pagares + expediente de papeles del cliente.
  ('contratos',           'Contratos',                 'Almacén de contratos: pagarés y expediente de papeles del cliente'),
  ('reportes',            'Reportes',                  'Reportes por fecha y por cliente'),
  ('historial',           'Historial',                 'Auditoría de operaciones'),
  ('usuarios',            'Admin de usuarios',         'CRUD de usuarios, roles y overrides'),
  ('configuracion',       'Configuración',             'EXCLUSIVO Admin'),
  ('vehiculos',           'Vehículos (ver)',           'Consulta del inventario de vehículos (DealControl)'),
  ('vehiculos_editar',    'Vehículos (crear/editar)',  'Alta, edición y baja de vehículos'),
  -- DealControl — permisos finos por operación (roles por modo, 011)
  ('inventario',          'Inventario (ver)',          'Consulta del inventario de vehículos'),
  ('inventario_editar',   'Inventario (crear/editar)', 'Alta, edición y baja de vehículos'),
  ('ventas',              'Ventas al contado',         'Registrar ventas al contado de vehículos'),
  ('ventas_editar',       'Ventas (editar)',           'Corregir una venta de vehículo ya registrada'),
  ('alquileres',          'Alquileres (rent a car)',   'Registrar y devolver alquileres'),
  ('alquileres_editar',   'Alquileres (editar)',       'Corregir un alquiler ya registrado'),
  ('gastos',              'Importación / gastos',      'Gestionar los gastos de importación'),
  -- Acceso por estancia/modo (aislamiento — cliente 2026-07-18)
  ('acceso_prestcontrol',  'Acceso a PrestControl',  'Puede entrar a la estancia de préstamos personales'),
  ('acceso_dealercontrol', 'Acceso a DealControl', 'Puede entrar a la estancia de inventario, ventas y alquiler de vehículos'),
  ('acceso_autocontrol',   'Acceso a AutoControl',   'Puede entrar a la estancia de ventas financiadas de vehículos'),
  -- POS-500 (022). `panel`, `clientes`, `clientes_editar` y `reportes` se
  -- reusan de arriba: son las mismas pantallas, filtradas por el modo activo.
  ('vender',              'Vender',                'Facturar en el punto de venta'),
  ('productos',           'Productos',             'Catalogo de productos y precios'),
  ('almacen',             'Almacen',               'Existencias y entradas de mercancia'),
  ('caducidad',           'Caducidad',             'Control de productos proximos a vencer'),
  ('comprobantes',        'Buscar comprobante',    'Buscar y reimprimir facturas propias'),
  ('comprobantes_todos',  'Comprobantes de todos', 'Ver los comprobantes de todos los cajeros'),
  ('cuadre',              'Cuadre de caja',        'Cerrar y consultar su propia caja'),
  ('cuadre_todos',        'Cuadre de todos',       'Ver el cuadre de caja de todos los cajeros'),
  ('facturas_anular',     'Anular facturas',       'Anular una factura ya emitida'),
  ('acceso_pos500',       'Acceso a POS-500',      'Puede entrar a la estancia del punto de venta');

-- Admin y Programador: todo
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre IN ('Admin', 'Programador') AND r.modo IS NULL;

-- Supervisor (PrestControl): toda la operación de préstamos, sin admin
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Supervisor' AND r.modo = 'prestcontrol'
  AND p.codigo IN ('panel','clientes','clientes_editar','prestamos','prestamos_crear',
                   'prestamos_cancelar','prestamos_editar','cobros','contratos','reportes',
                   'historial','acceso_prestcontrol');

-- Cobrador (PrestControl): cobra, consulta y crea préstamos (con autorización de Admin)
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Cobrador' AND r.modo = 'prestcontrol'
  AND p.codigo IN ('panel','clientes','prestamos','prestamos_crear','cobros','acceso_prestcontrol');

-- Encargado (DealControl): manda todo el dealer
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Encargado' AND r.modo = 'dealercontrol'
  AND p.codigo IN ('panel','inventario','inventario_editar','ventas','ventas_editar',
                   'alquileres','alquileres_editar','gastos',
                   'clientes','clientes_editar','reportes','historial','acceso_dealercontrol');

-- Vendedor (DealControl): vende y alquila; consulta el inventario
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Vendedor' AND r.modo = 'dealercontrol'
  AND p.codigo IN ('inventario','ventas','alquileres','clientes','acceso_dealercontrol');

-- Encargado (AutoControl): manda las ventas financiadas
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Encargado' AND r.modo = 'autocontrol'
  AND p.codigo IN ('prestamos','prestamos_crear','prestamos_cancelar','prestamos_editar','cobros',
                   'contratos','clientes','clientes_editar','reportes','historial','acceso_autocontrol');

-- Vendedor (AutoControl): crea ventas financiadas y cobra
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Vendedor' AND r.modo = 'autocontrol'
  AND p.codigo IN ('prestamos','prestamos_crear','cobros','clientes','acceso_autocontrol');

-- POS-500 (022) — Supervisor: todo el piso de venta, incluido lo de "todos"
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Supervisor' AND r.modo = 'pos500'
  AND p.codigo IN ('panel','vender','clientes','clientes_editar','productos','almacen',
                   'caducidad','comprobantes','comprobantes_todos','cuadre','cuadre_todos',
                   'reportes','facturas_anular','acceso_pos500');

-- POS-500 — Cajero: vende y cuadra LO SUYO
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Cajero' AND r.modo = 'pos500'
  AND p.codigo IN ('vender','clientes','comprobantes','cuadre','acceso_pos500');

-- POS-500 — Vendedor: vende y administra clientes; no cuadra caja
INSERT INTO rol_permiso (rol_id, permiso_id)
SELECT r.id, p.id FROM rol r CROSS JOIN permiso p
WHERE r.nombre = 'Vendedor' AND r.modo = 'pos500'
  AND p.codigo IN ('vender','clientes','clientes_editar','comprobantes','acceso_pos500');


-- =============================================================
-- PUNTO DE VENTA (POS-500, integrado a la suite el 2026-07-30 — ver 024)
--
-- Van en ESTA base, con prefijo pos_, para que el negocio tenga UN solo
-- respaldo. El cliente del mostrador es otra cosa que el de prestamos:
-- cedula opcional y sin apellido, porque en retail casi nadie se registra.
-- =============================================================

-- -------------------------------------------------------------
-- pos_cliente: cliente del mostrador. Cédula OPCIONAL, sin apellido.
-- -------------------------------------------------------------
CREATE TABLE pos_cliente (
  id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  cedula     VARCHAR(20)  NULL,
  nombre     VARCHAR(150) NOT NULL,
  telefono   VARCHAR(20)  NULL,
  direccion  VARCHAR(250) NULL,
  notas      TEXT         NULL,
  created_at DATETIME     NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at DATETIME     NULL,
  deleted_at DATETIME     NULL,                  -- soft delete
  PRIMARY KEY (id),
  UNIQUE KEY uq_pos_cliente_cedula (cedula)      -- múltiples NULL permitidos
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pos_producto: inventario con caducidad (el semáforo se calcula, no se guarda)
-- -------------------------------------------------------------
CREATE TABLE pos_producto (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo          VARCHAR(50)   NULL,            -- código de barras / interno
  nombre          VARCHAR(150)  NOT NULL,
  precio          DECIMAL(15,2) NOT NULL,
  cantidad        INT           NOT NULL DEFAULT 0,
  descripcion     TEXT          NULL,
  fecha_caducidad DATE          NULL,
  created_at      DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  updated_at      DATETIME      NULL,
  deleted_at      DATETIME      NULL,            -- soft delete
  PRIMARY KEY (id),
  UNIQUE KEY uq_pos_producto_codigo (codigo),    -- múltiples NULL permitidos
  KEY ix_pos_producto_nombre (nombre),
  KEY ix_pos_producto_caducidad (fecha_caducidad)
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pos_factura: NUNCA se elimina, solo se anula. Totales persistidos + la tasa
-- de ITBIS aplicada (el histórico no cambia si mañana cambia la tasa).
-- cliente_id NULL = consumidor final.
-- usuario_id: AHORA sí con clave foránea, porque el usuario vive en esta misma
-- base. Era justo lo que fallaba con la base separada.
-- -------------------------------------------------------------
CREATE TABLE pos_factura (
  id                BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  numero_factura    VARCHAR(30)   NOT NULL,
  cliente_id        BIGINT UNSIGNED NULL,
  usuario_id        BIGINT UNSIGNED NOT NULL,
  fecha_emision     DATETIME      NOT NULL,      -- UTC
  subtotal          DECIMAL(15,2) NOT NULL,
  itbis_tasa        DECIMAL(5,2)  NOT NULL,      -- 18.00 al día de hoy
  itbis             DECIMAL(15,2) NOT NULL,
  total             DECIMAL(15,2) NOT NULL,
  metodo_pago       ENUM('efectivo','tarjeta','transferencia','mixto') NOT NULL,
  efectivo_recibido DECIMAL(15,2) NULL,          -- solo efectivo/mixto
  cambio            DECIMAL(15,2) NULL,
  estado            ENUM('emitida','anulada') NOT NULL DEFAULT 'emitida',
  anulada_at        DATETIME      NULL,
  anulada_motivo    VARCHAR(250)  NULL,
  created_at        DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  PRIMARY KEY (id),
  UNIQUE KEY uq_pos_factura_numero (numero_factura),
  KEY ix_pos_factura_fecha (fecha_emision),
  KEY ix_pos_factura_usuario (usuario_id, fecha_emision),
  CONSTRAINT fk_pos_factura_cliente FOREIGN KEY (cliente_id)
    REFERENCES pos_cliente (id) ON DELETE RESTRICT,
  CONSTRAINT fk_pos_factura_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pos_detalle: líneas de factura (RESTRICT: las facturas no se borran)
-- -------------------------------------------------------------
CREATE TABLE pos_detalle (
  id              BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  factura_id      BIGINT UNSIGNED NOT NULL,
  producto_id     BIGINT UNSIGNED NOT NULL,
  cantidad        INT           NOT NULL,
  precio_unitario DECIMAL(15,2) NOT NULL,        -- precio al momento de la venta
  subtotal        DECIMAL(15,2) NOT NULL,
  PRIMARY KEY (id),
  KEY ix_pos_detalle_factura (factura_id),
  KEY ix_pos_detalle_producto (producto_id),
  CONSTRAINT fk_pos_detalle_factura FOREIGN KEY (factura_id)
    REFERENCES pos_factura (id) ON DELETE RESTRICT,
  CONSTRAINT fk_pos_detalle_producto FOREIGN KEY (producto_id)
    REFERENCES pos_producto (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pos_cuadre_caja: cierre por cajero y día de negocio; inmutable tras crearse
-- -------------------------------------------------------------
CREATE TABLE pos_cuadre_caja (
  id                     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  usuario_id             BIGINT UNSIGNED NOT NULL,
  fecha                  DATE          NOT NULL,  -- día de negocio (UTC-4)
  total_facturas         INT           NOT NULL,
  total_vendido          DECIMAL(15,2) NOT NULL,
  tiempo_activo_segundos INT           NOT NULL DEFAULT 0,
  created_at             DATETIME      NOT NULL DEFAULT (UTC_TIMESTAMP()),
  PRIMARY KEY (id),
  UNIQUE KEY uq_pos_cuadre_usuario_fecha (usuario_id, fecha),
  CONSTRAINT fk_pos_cuadre_usuario FOREIGN KEY (usuario_id)
    REFERENCES usuario (id) ON DELETE RESTRICT
) ENGINE=InnoDB;

-- -------------------------------------------------------------
-- pos_configuracion: UNA sola fila (id fijo = 1). ITBIS, moneda, numeración de
-- facturas y lo que sale en el ticket.
-- -------------------------------------------------------------
CREATE TABLE pos_configuracion (
  id                       TINYINT UNSIGNED NOT NULL,
  nombre_negocio           VARCHAR(150)  NOT NULL DEFAULT 'Mi Negocio',
  rnc                      VARCHAR(20)   NULL,
  direccion                VARCHAR(250)  NULL,
  telefono                 VARCHAR(20)   NULL,
  email                    VARCHAR(150)  NULL,
  logo_ruta                VARCHAR(500)  NULL,
  itbis_activo             TINYINT(1)    NOT NULL DEFAULT 1,
  itbis_tasa               DECIMAL(5,2)  NOT NULL DEFAULT 18.00,
  redondeo                 ENUM('centavo','peso','arriba') NOT NULL DEFAULT 'centavo',
  moneda_simbolo           VARCHAR(10)   NOT NULL DEFAULT 'RD$',
  formato_miles            ENUM('coma','punto') NOT NULL DEFAULT 'coma',
  factura_prefijo          VARCHAR(10)   NOT NULL DEFAULT 'F-',
  factura_siguiente        BIGINT UNSIGNED NOT NULL DEFAULT 1,  -- SELECT ... FOR UPDATE al emitir
  factura_formato          ENUM('simple','con_anio') NOT NULL DEFAULT 'simple',
  mostrar_cliente_en_venta TINYINT(1)    NOT NULL DEFAULT 1,
  updated_at               DATETIME      NULL,
  PRIMARY KEY (id),
  CONSTRAINT ck_pos_config_unica CHECK (id = 1)
) ENGINE=InnoDB;

INSERT INTO pos_configuracion (id) VALUES (1);

-- =============================================================
-- TRIGGERS: sincronizan usuario_permiso con el rol (patrón POS-400/POS-500).
--
-- OJO: los marcadores "DELIMITER $$" y el separador "$$" NO son decoración
-- ni sirven solo para mysql.exe. El protocolo de MySQL rechaza DELIMITER,
-- asi que VerificadorBaseDatos.ObtenerBloquesEjecutables() parte esta zona
-- y manda cada trigger como comando independiente. No reformatear a mano.
-- =============================================================

DELIMITER $$

CREATE TRIGGER trg_usuario_after_insert
AFTER INSERT ON usuario
FOR EACH ROW
BEGIN
  IF NEW.rol_id IS NOT NULL THEN
    INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
    SELECT NEW.id, rp.permiso_id
    FROM rol_permiso rp
    WHERE rp.rol_id = NEW.rol_id;
  END IF;
END$$

CREATE TRIGGER trg_usuario_after_update
AFTER UPDATE ON usuario
FOR EACH ROW
BEGIN
  IF (OLD.rol_id IS NULL AND NEW.rol_id IS NOT NULL)
     OR (OLD.rol_id IS NOT NULL AND NEW.rol_id IS NULL)
     OR (OLD.rol_id <> NEW.rol_id) THEN
    DELETE FROM usuario_permiso WHERE usuario_id = NEW.id;
    IF NEW.rol_id IS NOT NULL THEN
      INSERT IGNORE INTO usuario_permiso (usuario_id, permiso_id)
      SELECT NEW.id, rp.permiso_id
      FROM rol_permiso rp
      WHERE rp.rol_id = NEW.rol_id;
    END IF;
  END IF;
END$$

DELIMITER ;

-- =============================================================
-- CUENTA DE RESPALDO DEL DESARROLLADOR (020)
--
-- Va DESPUES de los triggers a proposito: el trigger de INSERT es el que le
-- copia los permisos del rol. Si esta fila naciera antes, la cuenta quedaria
-- sin ni un permiso.
--
-- El wizard de primer arranque IGUAL aparece: la app pregunta por usuarios del
-- negocio y esta cuenta (rol Programador) no cuenta para eso.
-- La contrasena va solo como hash BCrypt cost 12; en claro esta unicamente en
-- el MD privado del desarrollador. Ver 020_usuario_programador.sql para el
-- detalle y las advertencias.
-- =============================================================
INSERT INTO usuario (username, password_hash, nombre, apellido, rol_id, activo)
SELECT 'Yub',
       '$2a$12$JVW4T7UnLXu.n6k2f13N6.vQj3jShwGDVBegNh8HqTrC80yQZjie6',
       'Yuber', 'Santana',
       (SELECT id FROM rol WHERE nombre = 'Programador' AND modo IS NULL LIMIT 1),
       1
FROM DUAL
WHERE EXISTS (SELECT 1 FROM rol WHERE nombre = 'Programador' AND modo IS NULL);
