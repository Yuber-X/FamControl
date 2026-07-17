-- =============================================================
-- FAControl — Modalidad "pago único"
-- Script: 006_pago_unico.sql
-- Pedido del cliente 2026-07-17: préstamo de UNA sola cuota
-- (capital + interés) en la fecha acordada.
-- MIGRACION para bases ya existentes. Idempotente.
-- =============================================================
SET NAMES utf8mb4;
USE facontrol_db;

ALTER TABLE prestamo
  MODIFY COLUMN modalidad
  ENUM('diaria','semanal','quincenal','mensual','pago_unico') NOT NULL;
