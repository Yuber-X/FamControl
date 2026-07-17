# BLOCKERS.md — FAControl

> Bloqueos y decisiones que requieren consulta. Actualizado: 2026-07-10.

## Resueltos

| Fecha | Tema | Resolución |
|---|---|---|
| 2026-07-10 | PTV-300 no estaba en el workspace | ✅ Clonado desde GitHub (repo público `Yuber-X/PTV-300`) junto con POS-400. Ver `PTV300-PATTERNS.md`. |
| 2026-07-10 | `metodo_amortizacion`: ¿qué es `cuota_fija` vs `frances`? | ✅ Yuber confirmó: `cuota_fija` = interés simple dominicano (sobre capital original) y es el DEFAULT. Francés queda implementado como alternativo. |
| 2026-07-10 | Convención de tasa por modalidad | ✅ Decisión (investigación sin convención documentada): tasa siempre MENSUAL, conversión ÷2 quincenal, ÷4 semanal, ÷30 diaria. **Corregible** — confirmar con el cliente real. |
| 2026-07-10 | Nombre PrestaControl vs FAControl | ✅ FAControl en todo (UI, namespaces, `facontrol_db`). |

## Abiertos (no bloquean, decidir a futuro)

1. **ENUM `cuota.estado`:** el CLAUDE.md define `('pendiente','pagada','vencida','en_mora')` pero la regla §8.4 exige marcar cuotas como canceladas al cancelar un préstamo. Se agregó `'cancelada'` al ENUM del schema. Confirmar que está bien.
2. **Credenciales MySQL del cliente final:** Dev usa root local. En la instalación (Fase 7) se debe crear un usuario MySQL dedicado `facontrol` con permisos solo sobre `facontrol_db`.
3. **Comportamiento de logout:** hoy "Cerrar sesión" cierra la aplicación. ¿Debería volver a la pantalla de login? (UX menor, decidir en Fase 6).
4. **Pagos compensatorios negativos:** la regla "nunca modificar un pago" implica corregir errores con un pago negativo + nota obligatoria. La lógica del modelo lo soporta (montos con signo); la UI de captura queda para una fase posterior.
5. **Liquidación anticipada (decisión 2026-07-10, corregible):** las cuotas vencidas/vigentes se cobran completas y las futuras pagan SOLO su capital pendiente (el interés futuro se exonera). Es la práctica más común entre prestamistas RD, pero hay quienes cobran un % del interés futuro — confirmar con el cliente real. Implementado en `PagoService.DistribuirLiquidacion`.
6. **Recibos en cobros multi-cuota (decisión 2026-07-10):** `pago.numero_recibo` es UNIQUE por fila, así que un adelanto/liquidación que toca N cuotas consume N números de recibo; el recibo impreso agrupa la operación bajo el primero. Alternativa (un solo número por operación) requeriría tabla `recibo` separada — evaluar si molesta en la práctica.
7. **Reportes (decisión 2026-07-10):** el mockup real define UN solo reporte ("Ingresos por período") y se implementó fiel. El plan original mencionaba "6 tipos" sin mockups — definir con el cliente si necesita más (ej. cartera por cliente, morosidad histórica, proyección de cobros).
8. **Importar desde Excel (decisión 2026-07-10):** se descartó. Reconstruir relaciones (ids, FKs, estados) desde un .xlsx editable es una fuente de corrupción de datos. La migración de PC se cubre con Respaldar/Restaurar (.sql exacto); el Excel queda como exportación de consulta. Si el cliente insiste, evaluar un formato de intercambio cerrado.
