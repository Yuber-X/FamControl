# Intimación de pago y "mandamiento de pago" — qué es qué en RD

> El cliente pidió un "mandamiento de pago" imprimible para clientes que se
> niegan a pagar. Este doc aclara la realidad legal en República Dominicana y
> qué construye la app.

## La aclaración importante

**El "mandamiento de pago" NO lo emite el acreedor ni un software.** En el
procedimiento dominicano, el mandamiento de pago es un acto que **notifica un
alguacil** (ministerial), normalmente después de que un abogado inició el
proceso. Un PDF que imprima FAControl y diga "mandamiento de pago" **no tendría
valor legal** y podría confundir al cliente o al deudor.

Lo que el acreedor (Familia Almonte) **SÍ puede emitir por su cuenta** es una
**intimación de pago**: una comunicación formal, previa a lo judicial, donde
se le exige al deudor pagar en un plazo, dejando constancia. Es el paso lógico
antes de pasarle el caso a un abogado, y es 100% apropiado que la app lo genere.

## Lo que construye la app: INTIMACIÓN DE PAGO

FAControl genera un documento imprimible de **intimación de pago** con todos los
datos que un abogado necesitaría para proceder:

- **Acreedor:** negocio, prestamista, dirección, teléfono, RNC.
- **Deudor:** nombre, cédula.
- **La deuda:** préstamo, monto original, total adeudado, cuotas vencidas
  (con fechas y montos), días de atraso.
- **La intimación:** requerimiento formal de pagar en un plazo (configurable),
  y la advertencia de que, de no hacerlo, se procederá por la vía legal
  correspondiente conforme al pagaré firmado (que ya autoriza afectar bienes
  "habidos y por haber sin formalidad judicial").
- Fecha y espacio para firma.

Se llama **"Intimación de pago"** a propósito, no "mandamiento": es honesto
sobre lo que es y no expone al cliente a usar un término que no le corresponde.

## Recomendación para el cliente

1. Usar la **intimación de pago** de FAControl para el primer requerimiento formal.
2. Si el deudor sigue sin pagar, **entregar la intimación + el pagaré + el
   historial de pagos a un abogado**, que iniciará el proceso y pedirá al
   alguacil el mandamiento de pago real.
3. **Que un abogado revise el texto** de la intimación una vez, para adaptarlo
   a su práctica. El texto que trae la app es un punto de partida razonable,
   no asesoría legal.

## Nota técnica

La intimación reusa la misma infraestructura que el pagaré (FlowDocument +
vista previa + impresión paginada). Vive junto al préstamo, para clientes con
cuotas vencidas.
