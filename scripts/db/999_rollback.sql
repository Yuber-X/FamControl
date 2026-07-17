-- =============================================================
-- FAControl — Rollback del esquema inicial
-- Script: 999_rollback.sql
-- ⚠️ DESTRUCTIVO: elimina la base de datos completa.
-- Solo para entorno Dev. JAMÁS ejecutar en la máquina del cliente.
-- =============================================================

DROP DATABASE IF EXISTS facontrol_db;
