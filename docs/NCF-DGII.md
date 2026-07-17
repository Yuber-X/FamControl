# Comprobante Fiscal (NCF) — qué hace falta

> Pedido del cliente: que las facturas/recibos lleven un ID de comprobante
> fiscal. Este documento explica los pasos, porque **la parte que falta NO es
> de programación** sino de trámite ante la DGII.

## Lo importante primero

**No se "legaliza la app". Se legaliza el NEGOCIO ante la DGII.** La aplicación
solo *consume* números de comprobante que la DGII le asigna a la empresa. Sin el
trámite fiscal hecho, cualquier NCF que imprima la app sería inválido.

Por eso FAControl **hoy no emite NCF**: hacerlo sin el registro daría documentos
sin valor fiscal y podría meter al cliente en problemas con la DGII.

## Pasos que debe hacer el CLIENTE (Familia Almonte) — trámite, no código

1. **Tener RNC activo.** La empresa debe estar registrada en el RNC (Registro
   Nacional del Contribuyente) y al día con sus obligaciones.
2. **Solicitar autorización de NCF** en la Oficina Virtual de la DGII
   (dgii.gov.do → Oficina Virtual → Comprobantes Fiscales). La DGII asigna
   **rangos** de números por tipo de comprobante y una fecha de vencimiento.
3. **Elegir los tipos de comprobante** que usará. Los más comunes:
   | Tipo | Código | Para qué |
   |---|---|---|
   | Crédito Fiscal | B01 | Ventas a otras empresas con RNC (dan derecho a ITBIS) |
   | Consumidor Final | B02 | Ventas al público sin RNC |
   | Nota de Crédito | B04 | Devoluciones / anulaciones |
   | Régimen Especial | B14 | Zonas francas, etc. |
   | Gubernamental | B15 | Ventas al Estado |
   Para préstamos personales lo habitual es **B02 (Consumidor Final)** en el
   recibo de cobro, o directamente ningún NCF si los recibos de préstamo no se
   consideran ventas gravadas — **esto lo debe confirmar el contador del cliente.**
4. **Reportar el uso** (formatos 606/607/608) según lo exija la DGII.

> ⚠️ El tipo de comprobante y si los recibos de préstamo requieren NCF depende
> del régimen fiscal del negocio. **Que lo confirme el contador antes de activar
> nada.** No es una decisión técnica.

## Lo que aportaría la programación (SI deciden avanzar)

Una vez el cliente tenga los rangos autorizados, del lado de la app hace falta:

1. Una tabla `configuracion_ncf` con: tipo de comprobante, prefijo, número
   siguiente, número final del rango y fecha de vencimiento.
2. **Reserva atómica del NCF** al emitir el recibo: `SELECT ... FOR UPDATE`
   sobre el número siguiente, incrementar en la misma transacción, para que dos
   cobros simultáneos nunca tomen el mismo NCF.
3. El NCF impreso en el recibo/factura.
4. Aviso cuando el rango esté por agotarse o vencido.

**Buena noticia:** este mecanismo ya está resuelto y probado en POS-500 (la
numeración atómica de facturas con `FOR UPDATE` es idéntica). Portarlo a
FAControl sería trabajo acotado, NO investigación desde cero. Se estima en
un bloque de trabajo una vez el cliente entregue sus rangos de NCF.

## Resumen para decirle al cliente

- El NCF depende de un **trámite en la DGII**, no de la app.
- Primero: RNC al día + solicitar rangos de comprobante en la Oficina Virtual.
- Que el **contador confirme** qué tipo de comprobante corresponde a un
  préstamo (o si no lleva).
- Cuando tengan los rangos, agregar el NCF a la app es trabajo acotado
  (el motor ya existe en POS-500).
