# AMORTIZATION.md — Matemática de amortización de FAControl

> Implementado en `FAControl.Services.AmortizacionService`. Tests en `tests/FAControl.Services.Tests/AmortizacionServiceTests.cs` (40 tests, todos verdes).

## 1. Convención de tasa (DECISIÓN — corregible con el cliente)

**Decisión 2026-07-10:** la tasa se ingresa siempre como **porcentaje MENSUAL** (coincide con el mockup "Tasa de interés mensual (%)" y con la práctica de cotización de los prestamistas dominicanos). Se convierte a tasa por período de pago con factores comerciales simples:

| Modalidad | Conversión | Ejemplo (10% mensual) |
|---|---|---|
| Mensual | ÷ 1 | 10% por cuota |
| Quincenal | ÷ 2 | 5% por cuota |
| Semanal | ÷ 4 | 2.5% por cuota |
| Diaria | ÷ 30 | 0.333…% por cuota |

> Investigación 2026-07-10: la banca formal dominicana usa el sistema francés con tasas anuales; para prestamistas informales no hay convención documentada públicamente, por lo que se adoptó la más esperable (mensual). Si el cliente opera distinto, solo hay que ajustar `AmortizacionService.TasaPorPeriodo`.

## 2. Método "cuota fija" — interés simple dominicano (DEFAULT)

El interés de cada cuota se calcula sobre el **capital original**, nunca sobre el saldo. Es el modelo habitual del prestamista independiente en RD.

```
i = tasa por período (fracción)
n = número de cuotas
P = capital prestado

interésPorCuota = P × i
capitalPorCuota = P / n
cuota           = capitalPorCuota + interésPorCuota
interésTotal    = P × i × n
```

**Ejemplo:** RD$ 10,000 al 10% mensual, 12 cuotas mensuales →
interés RD$ 1,000/mes fijo + capital RD$ 833.33 → cuota RD$ 1,833.33 (última RD$ 1,833.37 por ajuste). Interés total RD$ 12,000 (el cliente paga 22,000 en total).

## 3. Método francés (alternativo)

Cuota constante; el interés se calcula sobre el **saldo insoluto**, decrece con cada pago mientras el capital amortizado crece.

```
cuota = P × [i(1+i)^n] / [(1+i)^n − 1]        (si i = 0 → cuota = P/n)
interés_k = saldo_k × i
capital_k = cuota − interés_k
```

**Validado contra el mockup:** RD$ 75,000 al 5% mensual, 12 cuotas → cuota RD$ 8,461.91, primer interés RD$ 3,750.00, primer capital RD$ 4,711.91 (coincide exacto con el diseño aprobado).

La potencia `(1+i)^n` se calcula con multiplicación iterativa de `decimal` — nunca `Math.Pow` (double) para no perder precisión.

## 4. Reglas de redondeo

1. Todo redondeo usa `Math.Round(valor, 2, MidpointRounding.AwayFromZero)` (estándar comercial RD).
2. Los componentes de cada cuota se redondean a 2 decimales.
3. **La última cuota absorbe la diferencia** para que se cumpla siempre:
   - Σ capital de las cuotas = capital original (al centavo)
   - Σ interés = interés total calculado una sola vez
4. Nunca se redondea en pasos intermedios de la fórmula.

## 5. Fechas de vencimiento

La cuota 1 vence en la **fecha del primer pago** elegida al crear el préstamo.

| Modalidad | Avance |
|---|---|
| Diaria | +1 día |
| Semanal | +7 días |
| Quincenal | +15 días |
| Mensual | +1 mes calendario (`AddMonths`, el fin de mes no se desborda: 31-ene → 28-feb → 31-mar) |

## 6. Semáforo de cobros (`CuotaEstadoCalculator`)

Calculado en tiempo real, nunca persistido:

| Estado | Regla |
|---|---|
| Pagada | abonos ≥ monto total de la cuota |
| Al día | vence a más de 7 días |
| Por vencer | vence hoy o en ≤ 7 días |
| Vencida | venció hace 1–15 días |
| En mora | venció hace más de 15 días |
| Cancelada | el préstamo fue cancelado |

Cobertura de tests: 100% de las ramas (requisito del proyecto).

## 7. Distribución de cobros (`PagoService`)

Todo abono se distribuye entre las cuotas impagas **en orden**, y dentro de cada
cuota aplica **primero a interés, luego a capital** (práctica contable estándar).
Del acumulado `monto_pagado` de la cuota se deriva sin columnas extra:

```
interésPendiente = max(0, interés − montoPagado)
capitalPendiente = capital − max(0, montoPagado − interés)
```

| Escenario | Comportamiento |
|---|---|
| Pago exacto | Cubre el saldo de la próxima cuota; queda `pagada` |
| Abono parcial | Aplica a interés primero; la cuota conserva su estado |
| Adelanto | El excedente cae en cascada a las cuotas siguientes |
| Liquidación anticipada | Vencidas/vigentes se cobran completas; las **futuras pagan solo su capital pendiente** y su interés se exonera (decisión corregible — BLOCKERS.md #5) |

El cobro completo es UNA transacción: cuotas bloqueadas con `SELECT ... FOR UPDATE`,
`numero_recibo` atómico desde `contador`, actualización de cuotas, estado del
préstamo (`pagado` si todo quedó cubierto) y auditoría. Rollback ante cualquier error.

Un monto que exceda la deuda pendiente se rechaza (no hay "cambio"); para saldar
el préstamo se usa la liquidación anticipada.
