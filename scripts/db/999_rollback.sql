-- =============================================================
-- FAControl — Rollback del esquema inicial
-- Script: 999_rollback.sql
-- ⚠️ DESTRUCTIVO: elimina la base de datos completa.
-- Solo para entorno Dev. JAMÁS ejecutar en la máquina del cliente.
-- =============================================================

-- Fuerza UTF-8: mysql.exe asume la codificacion de la consola y corrompe los acentos.
SET NAMES utf8mb4;
DROP DATABASE IF EXISTS facontrol_db;
