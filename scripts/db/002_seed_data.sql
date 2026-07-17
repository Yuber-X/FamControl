-- =============================================================
-- FAControl — Datos de prueba (solo Dev)
-- Script: 002_seed_data.sql
-- NO ejecutar en la máquina del cliente: el usuario real se crea
-- desde el wizard de primer arranque de la aplicación.
-- =============================================================

USE facontrol_db;

-- Clientes de prueba (nombres ficticios, cédulas de formato válido pero inventadas)
INSERT INTO cliente (cedula, nombre, apellido, telefono, direccion, email) VALUES
  ('001-1234567-8', 'Ramón Antonio', 'Peña',      '809-555-0142', 'Los Alcarrizos',  NULL),
  ('402-2345678-9', 'María Altagracia', 'Jiménez','829-555-0198', 'Santiago',        NULL),
  ('001-3456789-0', 'José Manuel', 'Rodríguez',   '809-555-0210', 'Sto. Dgo. Este',  NULL),
  ('031-4567890-1', 'Yolanda Mercedes', 'Then',   '849-555-0177', 'La Vega',         NULL),
  ('001-5678901-2', 'Francisco Alberto', 'Ureña', '809-555-0155', 'San Cristóbal',   NULL);
