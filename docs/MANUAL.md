# Manual de usuario — FAControl

**Familia Almonte Auto Import SRL**
Versión del programa: 1.9.2 · Fecha de este manual: 2 de agosto de 2026

---

> ### 📷 Cómo usar los espacios para imágenes
>
> A lo largo del manual vas a encontrar bloques como este:
>
> ```
> ┌──────────────────────────────────────────┐
> │  📷 IMAGEN 00 — Descripción de la foto   │
> └──────────────────────────────────────────┘
> ```
>
> Cada uno lleva un **número** y dice **exactamente qué pantalla** hay que
> fotografiar. Guardá la captura con ese número (`imagen-00.png`) y pegala
> justo debajo del bloque.
>
> **Total de imágenes: 43.**

---

## Índice

1. [Qué es FAControl](#1-qué-es-facontrol)
2. [Cómo se instala](#2-cómo-se-instala)
3. [La primera vez que abrís el programa](#3-la-primera-vez-que-abrís-el-programa)
4. [El launcher: elegir en qué vas a trabajar](#4-el-launcher-elegir-en-qué-vas-a-trabajar)
5. [Entrar con tu usuario](#5-entrar-con-tu-usuario)
6. [Cómo está armada la pantalla](#6-cómo-está-armada-la-pantalla)
7. [PrestControl — préstamos](#7-prestcontrol--préstamos)
8. [DealControl — vehículos, ventas y alquileres](#8-dealcontrol--vehículos-ventas-y-alquileres)
9. [POS-500 — punto de venta](#9-pos-500--punto-de-venta)
10. [Pantallas que están en los tres modos](#10-pantallas-que-están-en-los-tres-modos)
11. [Respaldos: no perder la información](#11-respaldos-no-perder-la-información)
12. [Si algo sale mal](#12-si-algo-sale-mal)
13. [Palabras que vas a leer seguido](#13-palabras-que-vas-a-leer-seguido)

---

## 1. Qué es FAControl

FAControl es **un solo programa con tres oficinas adentro**. Cada oficina
sirve para un negocio distinto:

| | Se llama | Sirve para |
|---|---|---|
| 🟡 | **PrestControl** | Prestar dinero y cobrar las cuotas |
| 🔵 | **DealControl** | Vender y alquilar vehículos |
| 🟢 | **POS-500** | Vender en el mostrador (colmado, tienda) |

**Lo más importante que tenés que entender:**

> Las tres oficinas **NO comparten información**. Un cliente que cargaste en
> PrestControl **no aparece** en DealControl. Un producto del POS-500 **no
> existe** en las otras dos. Es a propósito: son tres negocios distintos.
>
> Lo único que sí se comparte son **los usuarios** (las personas que entran al
> programa) y **qué puede hacer cada uno**.

Todo funciona **en esta computadora**. No hace falta internet para trabajar
(solo para instalar y para subir los respaldos a la nube, si querés).

---

## 2. Cómo se instala

La instalación está explicada paso por paso en el otro documento:
**`INSTALL.md` — Guía de instalación**. Ahí está todo: qué programas hacen
falta, en qué orden se instalan y qué poner en cada pantalla.

Este manual arranca **después** de que el programa ya está instalado.

---

## 3. La primera vez que abrís el programa

### 3.1. Se crea la base de datos

La primera vez, FAControl pregunta si quiere crear su base de datos, que es
donde se guarda toda la información. **Respondé que Sí.** Tarda unos segundos.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 01 — Mensaje "La base de datos todavía no existe    │
│     en este equipo. ¿Quieres crearla ahora?"                   │
└────────────────────────────────────────────────────────────────┘
```

Cuando termina aparece **"Base de datos creada correctamente"**.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 02 — Mensaje "Base de datos creada correctamente"   │
└────────────────────────────────────────────────────────────────┘
```

### 3.2. Se crea la primera cuenta

Después el programa pide crear la **primera cuenta**, que va a ser la del
dueño (administrador). Te pide:

- **Usuario** — el nombre corto con el que vas a entrar (por ejemplo `admin`)
- **Nombre** — tu nombre completo, el que se ve arriba a la izquierda
- **Contraseña** — mínimo 8 caracteres, escrita **dos veces** para confirmar

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 03 — Pantalla de "Crear cuenta inicial" con los     │
│     campos usuario, nombre, contraseña y confirmación          │
└────────────────────────────────────────────────────────────────┘
```

> ⚠️ **Anotá esa contraseña en un lugar seguro.** No hay recuperación por
> correo ni por teléfono. Si se pierde, hay que llamar al desarrollador.

### 3.3. Se activan las oficinas

Cada oficina se **activa con un código** que entrega el desarrollador. Una
oficina sin activar se ve en gris y no se puede abrir.

Para activarla, en el launcher apretá el botón de **activación** y escribí el
código.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 04 — Ventana de activación pidiendo el código       │
└────────────────────────────────────────────────────────────────┘
```

---

## 4. El launcher: elegir en qué vas a trabajar

Cada vez que abrís FAControl aparece primero el **launcher**: la pantalla que
te deja elegir en cuál de las tres oficinas vas a trabajar.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 05 — Launcher completo, con las tres tarjetas       │
│     (PrestControl dorado, DealControl azul, POS-500 verde)     │
└────────────────────────────────────────────────────────────────┘
```

**Hacé clic en la tarjeta** de la oficina donde vas a trabajar. Eso es todo.

> 💡 **Para cambiar de oficina** más tarde: cerrá sesión (arriba a la
> izquierda, en tu nombre) y volvés al launcher.

---

## 5. Entrar con tu usuario

Después de elegir la oficina, el programa pide tu usuario y tu contraseña.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 06 — Pantalla de inicio de sesión (login)           │
└────────────────────────────────────────────────────────────────┘
```

**Si te equivocás 5 veces seguidas**, el programa bloquea el ingreso durante
**5 minutos**. Es a propósito: evita que alguien pruebe contraseñas hasta
acertar. Esperá y volvé a intentar.

### El aviso de pagos vencidos

Apenas entrás, si algún cliente **se pasó de su fecha de pago**, aparece un
aviso con el nombre, cuánto debe y desde cuándo. Cerralo con **OK**; si sigue
habiendo atrasos, vuelve a salir la próxima vez que entres.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 07 — Ventana de aviso de pagos vencidos             │
└────────────────────────────────────────────────────────────────┘
```

---

## 6. Cómo está armada la pantalla

Todas las oficinas se ven igual por dentro. Siempre hay tres partes:

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 08 — Pantalla principal completa, señalando con     │
│     flechas: (A) barra lateral, (B) tu usuario, (C) contenido  │
└────────────────────────────────────────────────────────────────┘
```

**(A) La barra lateral (izquierda).** El menú. Cada botón lleva a una
pantalla distinta. **Los botones que no ves es porque no tenés permiso** para
esa pantalla — no es un error.

**(B) Tu usuario (arriba a la izquierda).** Tu nombre y tu rol. Desde ahí
también se cierra la sesión.

**(C) El contenido (el resto).** La pantalla en la que estás trabajando.

### El modo oscuro

FAControl tiene modo claro y modo oscuro. Se cambia en **Configuración →
Apariencia**. Elegí el que te canse menos la vista.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 09 — La misma pantalla en modo claro y en modo      │
│     oscuro, una al lado de la otra                             │
└────────────────────────────────────────────────────────────────┘
```

---

## 7. PrestControl — préstamos

La oficina de prestar dinero.

### 7.1. Panel

Lo primero que ves. Es el resumen del negocio **de un vistazo**:

- **Capital colocado** — cuánta plata tenés prestada en la calle
- **Cobros del mes** — cuánto entró este mes
- **Clientes activos** — cuántas personas te deben
- **Morosidad** — cuánto está vencido y sin pagar

Abajo hay un gráfico con los cobros del mes y la lista de **a quién hay que
cobrarle hoy**.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 10 — Panel de PrestControl completo, con las 4      │
│     tarjetas de arriba y el gráfico                            │
└────────────────────────────────────────────────────────────────┘
```

### 7.2. Clientes

La lista de las personas a las que les prestás.

**Para agregar uno nuevo:** botón **Nuevo cliente**, arriba a la derecha. Lo
obligatorio es la **cédula**, el **nombre** y el **apellido**. El teléfono no
es obligatorio, pero conviene ponerlo: es con lo que después los llamás.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 11 — Lista de clientes con el buscador arriba       │
└────────────────────────────────────────────────────────────────┘
```

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 12 — Formulario de cliente nuevo, vacío             │
└────────────────────────────────────────────────────────────────┘
```

**Para ver la ficha de un cliente:** doble clic sobre su fila. Ahí ves todos
sus préstamos, lo que debe y sus documentos.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 13 — Ficha de un cliente con sus préstamos          │
└────────────────────────────────────────────────────────────────┘
```

> ⚠️ La cédula **no se puede repetir**. Si el programa avisa que ya existe,
> es que esa persona ya está cargada: buscala en la lista en vez de crearla
> de nuevo.

### 7.3. Nuevo préstamo

Acá se arma un préstamo. Se llena en orden:

1. **Elegí el cliente** (si no está, hay que crearlo primero)
2. **Monto** — cuánta plata le prestás
3. **Tasa de interés** — el porcentaje por período
4. **Cantidad de cuotas** — en cuántas veces lo va a pagar
5. **Modalidad** — diaria, semanal, quincenal o mensual
6. **Fecha de la primera cuota**

A medida que escribís, **abajo aparece la tabla de cuotas** con lo que va a
pagar en cada una. Revisala con el cliente **antes** de guardar.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 14 — Pantalla de nuevo préstamo con la tabla de     │
│     cuotas calculada abajo                                     │
└────────────────────────────────────────────────────────────────┘
```

Al guardar, el programa ofrece **imprimir el pagaré** (el papel que firma el
cliente).

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 15 — Vista previa del pagaré, listo para imprimir   │
└────────────────────────────────────────────────────────────────┘
```

### 7.4. Préstamos

La lista de todos los préstamos. Cada fila tiene un **color** que dice cómo
va:

| Color | Qué significa |
|---|---|
| 🟢 Verde | Al día |
| 🟡 Amarillo | Vence en los próximos 7 días |
| 🟠 Naranja | Vencido hace 1 a 15 días |
| 🔴 Rojo | En mora — más de 15 días sin pagar |
| ⚪ Gris | Ya pagado o cancelado |

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 16 — Lista de préstamos con filas de varios colores │
└────────────────────────────────────────────────────────────────┘
```

Doble clic en una fila abre el **detalle**: todas las cuotas, cuáles están
pagadas y cuánto falta.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 17 — Detalle de un préstamo con su tabla de cuotas  │
└────────────────────────────────────────────────────────────────┘
```

### 7.5. Cobros

**La pantalla que más vas a usar.** Acá se anota la plata que entra.

1. **Buscá el cliente** o el préstamo
2. **Elegí la cuota** que está pagando
3. **Escribí cuánto paga** — puede ser la cuota completa o una parte
4. **Elegí cómo paga** — efectivo, transferencia, cheque u otro
5. Apretá **Registrar pago**

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 18 — Pantalla de cobros con un pago cargado         │
└────────────────────────────────────────────────────────────────┘
```

El programa **imprime el recibo** solo. Ese papel es del cliente.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 19 — Recibo de pago (vista previa o impreso)        │
└────────────────────────────────────────────────────────────────┘
```

> ⚠️ **Un pago registrado no se borra nunca.** Si te equivocaste, no busques
> el botón de eliminar: no existe, y es a propósito. La plata ya se movió y
> el recibo ya está en la mano del cliente. Llamá al desarrollador.

**Dos botones que ahorran tiempo:**

- **Cuota completa** — pone el monto exacto de la cuota, para no tipearlo
- **Liquidar préstamo** — el cliente quiere pagar todo hoy y cerrar

### 7.6. Contratos

El **archivo digital**: los papeles de cada préstamo guardados en la
computadora.

Cada fila tiene dos botones:

- **Archivos** — entra a los documentos de ese contrato. Ahí subís fotos de
  la cédula, el contrato firmado, lo que haga falta.
- **Pagaré** — vuelve a abrir el pagaré para verlo o imprimirlo otra vez.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 20 — Pantalla de Contratos con los botones          │
│     Archivos y Pagaré en las filas                             │
└────────────────────────────────────────────────────────────────┘
```

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 21 — Expediente de un contrato con archivos subidos │
└────────────────────────────────────────────────────────────────┘
```

---

## 8. DealControl — vehículos, ventas y alquileres

La oficina del dealer.

### 8.1. Panel

El resumen: cuántos vehículos tenés, cuántos vendidos, cuántos alquilados y
cuánta plata te deben.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 22 — Panel de DealControl completo                  │
└────────────────────────────────────────────────────────────────┘
```

### 8.2. Vehículos

El inventario. Cada vehículo tiene un **estado**:

| Estado | Qué significa |
|---|---|
| **Disponible** | Está en el lote, se puede vender o alquilar |
| **Reservado** | Alguien lo separó con un adelanto |
| **Vendido** | Ya se vendió |
| **Alquilado** | Está afuera, alquilado a un cliente |
| **Baja** | Ya no es parte del inventario |

**El estado lo maneja el programa solo.** Cuando vendés pasa a Vendido;
cuando el alquiler se cierra vuelve a Disponible. No hay que tocarlo a mano.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 23 — Lista de vehículos con los distintos estados   │
└────────────────────────────────────────────────────────────────┘
```

**Para agregar uno:** botón **Nuevo vehículo**. Cargá marca, modelo, año,
color, chasis (VIN), placa y **matrícula**. Después lo que te costó y a
cuánto lo vas a vender.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 24 — Formulario de vehículo nuevo                   │
└────────────────────────────────────────────────────────────────┘
```

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 25 — Ficha de un vehículo con sus fotos y gastos    │
└────────────────────────────────────────────────────────────────┘
```

### 8.3. Importación / gastos

Acá se anota todo lo que gastaste en traer y preparar un vehículo: aduana,
flete, pintura, mecánica.

**Para qué sirve:** el programa suma esos gastos al costo del vehículo, así
sabés **cuánto ganaste de verdad** cuando lo vendas. Sin esto, la ganancia
que ves es mentira.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 26 — Pantalla de gastos de importación de un        │
│     vehículo, con varios gastos cargados                       │
└────────────────────────────────────────────────────────────────┘
```

### 8.4. Ventas al contado

El cliente paga todo de una vez y se lleva el vehículo. Elegís el vehículo,
el cliente, el precio y el método de pago. El vehículo pasa solo a
**Vendido**.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 27 — Pantalla de venta al contado                   │
└────────────────────────────────────────────────────────────────┘
```

### 8.5. Ventas financiadas

El cliente paga en **plazos**. Es la venta más común y la más delicada.

Al crearla ponés:

- **Precio** del vehículo
- **Inicial** — cuánto deja hoy
- **Cantidad de plazos** y cada cuántos días
- **Fecha del primer plazo**

El programa arma el plan de pagos y lo muestra antes de guardar.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 28 — Nueva venta financiada con el plan de plazos   │
└────────────────────────────────────────────────────────────────┘
```

Después, cada vez que el cliente paga, entrás al **detalle de la venta** y
registrás el cobro. Ahí ves cuánto lleva pagado y cuánto falta.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 29 — Detalle de una venta financiada: plazos,       │
│     cobros y lo que falta                                      │
└────────────────────────────────────────────────────────────────┘
```

**Dos botones importantes:**

- **Editar** — corrige la venta si te equivocaste al cargarla. Puede incluso
  cambiar la cantidad de plazos: la plata ya cobrada se reparte sola en el
  plan nuevo y **los recibos ya entregados no cambian**.
- **Cancelar** — el cliente devolvió el vehículo y se deshace la venta.
  **Se apaga solo cuando la venta ya está saldada**: una venta cobrada por
  completo no se cancela.

### 8.6. Alquileres (rent a car)

Alquilar un vehículo por días.

Al crear el alquiler ponés el vehículo, el cliente, **desde cuándo hasta
cuándo** y la **tarifa por día**. El programa calcula el total.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 30 — Pantalla de nuevo alquiler                     │
└────────────────────────────────────────────────────────────────┘
```

En el **detalle del alquiler** está todo lo demás:

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 31 — Detalle de un alquiler activo, con los botones │
│     Renovar, Editar y Cerrar arriba                            │
└────────────────────────────────────────────────────────────────┘
```

**Los tres botones:**

**🔵 Renovar** — el cliente sigue con el auto más días. Ponés la fecha nueva y
la tarifa (la misma o una nueva). El vehículo **no** vuelve al inventario.

> 💡 Si le cambiás la tarifa, **los días que ya usó siguen al precio viejo**.
> El precio nuevo rige de ahí en adelante. Es lo justo, y el programa lo hace
> solo.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 32 — Ventana de "Renovar alquiler" con la fecha     │
│     nueva y la tarifa                                          │
└────────────────────────────────────────────────────────────────┘
```

**⚪ Editar** — corrige errores de tipeo. Solo mientras el alquiler está
abierto y **solo si todavía no se renovó**.

**🔴 Cerrar alquiler** — el contrato termina. El programa pregunta **cuál de
las dos cosas pasó**:

| Opción | Cuándo se usa | Qué pasa con la plata |
|---|---|---|
| **Devuelto** | El cliente usó el auto y lo trajo | Es plata ganada. Si trajo tarde, se cobran los días de más |
| **Cancelado** | El alquiler no llegó a pasar | No es ingreso. Lo que cobraste queda a favor del cliente |

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 33 — Ventana de cierre preguntando Devuelto o       │
│     Cancelado                                                  │
└────────────────────────────────────────────────────────────────┘
```

En los dos casos el vehículo **vuelve a Disponible** solo.

**Si se pasó la fecha** y el auto no volvió, el detalle muestra un cartel
naranja con los días de atraso y la plata que corresponde de más. Ahí
decidís: lo cerrás o lo renovás.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 34 — Cartel de alquiler atrasado                    │
└────────────────────────────────────────────────────────────────┘
```

**Los cobros del alquiler** se registran en la misma pantalla, más abajo. Un
alquiler normalmente se paga en dos veces: un adelanto al retirar y el resto
al devolver.

---

## 9. POS-500 — punto de venta

La oficina del mostrador.

### 9.1. Vender

**La pantalla del día a día.** Así se hace una venta:

1. **Pasá el producto por el lector de código de barras**, o escribí el
   código y apretá Enter, o buscalo por nombre
2. Repetí con todos los productos
3. Si hace falta, cambiá la cantidad de alguna línea
4. Elegí **cómo paga**: efectivo, tarjeta, transferencia o mixto
5. Si es efectivo, escribí **con cuánto paga** y el programa dice **el
   vuelto**
6. Apretá **Cobrar**

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 35 — Pantalla de Vender con varios productos en el  │
│     carrito y el total abajo                                   │
└────────────────────────────────────────────────────────────────┘
```

La factura se **imprime sola**. El stock de cada producto **baja solo**.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 36 — Factura / ticket impreso                       │
└────────────────────────────────────────────────────────────────┘
```

> 💡 Si el negocio tiene activada la **comisión del vendedor**, al lado del
> subtotal ves cuánto vas ganando con esa venta.

### 9.2. Productos

El catálogo. Cada producto tiene código, nombre, **precio**, **cantidad en
stock** y, si aplica, **fecha de caducidad**.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 37 — Lista de productos                             │
└────────────────────────────────────────────────────────────────┘
```

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 38 — Formulario de producto nuevo                   │
└────────────────────────────────────────────────────────────────┘
```

### 9.3. Almacén

Para **entrar mercancía** cuando llega del suplidor, y para **corregir el
stock** cuando el conteo físico no coincide con lo que dice el programa.

Toda corrección pide un **motivo** (se rompió, se lo robaron, error de
conteo). Queda anotado en el historial: así después se sabe qué pasó.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 39 — Pantalla de Almacén con una entrada de         │
│     mercancía                                                  │
└────────────────────────────────────────────────────────────────┘
```

### 9.4. Caducidad

La lista de lo que **está por vencerse o ya se venció**. Miralo una vez por
semana: es plata que se pierde si no la sacás a tiempo.

El programa también puede **mandarte un correo** avisando. Se configura en
Configuración → Correo.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 40 — Pantalla de Caducidad con productos por vencer │
└────────────────────────────────────────────────────────────────┘
```

### 9.5. Buscar comprobante

Para encontrar una factura ya emitida: por número, por fecha o por cliente.
Desde acá se **reimprime** o se **anula**.

> ⚠️ Anular una factura **devuelve el stock** y deja el registro marcado como
> anulado. La factura **no se borra**: eso es ilegal.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 41 — Pantalla de Buscar comprobante                 │
└────────────────────────────────────────────────────────────────┘
```

### 9.6. Cuadre de caja

**Al final del día.** Muestra cuánto se vendió, cuántas facturas se hicieron
y cuánto debería haber en la caja.

Contás la plata física y la comparás con lo que dice el programa. Si sobra o
falta, ahí se ve.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 42 — Pantalla de Cuadre de caja                     │
└────────────────────────────────────────────────────────────────┘
```

---

## 10. Pantallas que están en los tres modos

### 10.1. Reportes

Informes con filtros de fecha. Se pueden **imprimir** o **guardar en PDF**.
Cada oficina tiene los suyos: préstamos y cobranza en PrestControl, ventas y
alquileres en DealControl, ventas y productos en POS-500.

### 10.2. Historial

**Todo lo que pasó en el programa, quién lo hizo y cuándo.** No se puede
borrar ni editar. Sirve para averiguar qué ocurrió: quién registró tal pago,
quién cambió tal precio.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN 43 — Pantalla de Historial con varios movimientos   │
└────────────────────────────────────────────────────────────────┘
```

### 10.3. Usuarios

Solo el administrador la ve. Acá se crean las cuentas de los empleados y se
marca **qué puede hacer cada uno**: hay quien solo cobra, quien solo vende,
quien puede todo.

> 💡 **Consejo:** dale a cada empleado su propia cuenta. Si todos usan la
> misma, el historial no sirve para nada.

### 10.4. Configuración

| Sección | Para qué |
|---|---|
| **Apariencia** | Modo claro/oscuro, tamaño del texto |
| **Contraseña** | Cambiar la tuya |
| **Comprobante fiscal (NCF)** | La secuencia autorizada por la DGII |
| **Respaldo** | Copias de seguridad, manuales y automáticas |
| **Exportar a Excel** | Bajar toda la información a una planilla |
| **Correo** | Para los avisos automáticos |
| **ITBIS y comisión** *(solo POS-500)* | El impuesto y la comisión del vendedor |

**Sobre el comprobante fiscal (NCF):** cuando marcás la casilla **"Usar
secuencia local de comprobantes"**, los campos se **borran solos**. Es a
propósito: lo que estaba escrito era solo un ejemplo y no hay que usarlo.
Escribí ahí la secuencia que **la DGII le autorizó a tu negocio**.

> ⚠️ Cada oficina lleva **su propia** secuencia. Lo que configures en
> DealControl no afecta a PrestControl ni al POS-500.

**Sobre exportar a Excel:** el archivo sale con el nombre de la oficina al
final (`FAControl_Export_2026-08-01 DealControl.xlsx`), así no se pisan entre
ellos si exportás las tres el mismo día.

---

## 11. Respaldos: no perder la información

> ### 🔴 Lo más importante de todo el manual
>
> Si la computadora se rompe, se moja o se la roban, **la información se va
> con ella**. El respaldo es lo único que la salva.

**Respaldo manual** — Configuración → Respaldo → **Respaldar ahora**. Elegís
dónde guardar el archivo. Hacelo **antes de cualquier cosa importante**.

**Respaldo automático** — en la misma pantalla se activa y se elige cada
cuántos días. Poné como carpeta destino la de **Google Drive**: así el
respaldo sube solo a internet y sobrevive aunque la computadora no.

**Restaurar** — el botón está al lado. Elegís el archivo de respaldo y el
programa vuelve a como estaba ese día.

> ⚠️ **Restaurar reemplaza todo lo actual.** Lo que hiciste después de ese
> respaldo se pierde. Usalo solo si de verdad hace falta.

---

## 12. Si algo sale mal

| Lo que ves | Qué hacer |
|---|---|
| **"No se pudo conectar con MySQL"** | El motor de la base de datos está apagado. Reiniciá la computadora. Si sigue igual: tecla Windows → escribí `services.msc` → buscá **MySQL80** → clic derecho → **Iniciar** |
| **"MySQL rechazó el usuario o la contraseña"** | La contraseña de MySQL no coincide con la configurada. Llamá al desarrollador |
| **Un botón del menú no aparece** | No tenés permiso para esa pantalla. Pediselo al administrador |
| **La impresora no imprime** | Revisá que esté encendida, con papel y elegida en Configuración |
| **El lector de código no funciona** | Probá en el Bloc de notas: si ahí tampoco escribe, es el lector, no el programa |
| **Me olvidé la contraseña** | El administrador la puede cambiar desde Usuarios. Si el que la olvidó es el administrador, llamá al desarrollador |
| **El programa se cerró solo** | Volvé a abrirlo: no se pierde nada, todo se guarda al momento. Si pasa seguido, avisá |

**Antes de llamar, tené a mano:**

1. **Qué estabas haciendo** exactamente cuando pasó
2. **Una foto del mensaje de error** (con el celular alcanza)
3. La carpeta `logs`, que está donde se instaló el programa

**Soporte:** Yuber Santana — **849-438-0242**

Si tenés **AnyDesk** instalado (viene con el instalador), abrilo y pasá el
número que muestra: con eso se puede entrar a la computadora a distancia y
resolverlo sin moverse de donde estás.

---

## 13. Palabras que vas a leer seguido

| Palabra | Qué quiere decir |
|---|---|
| **Cuota** | Cada uno de los pagos en que se divide un préstamo |
| **Plazo** | Lo mismo que cuota, pero en las ventas de vehículos |
| **Mora** | Una cuota que lleva más de 15 días sin pagarse |
| **Capital** | La plata que prestaste, sin contar el interés |
| **Saldar** | Terminar de pagar todo |
| **NCF** | Número de Comprobante Fiscal: la numeración que autoriza la DGII |
| **ITBIS** | El impuesto de las ventas (18% en República Dominicana) |
| **Cuadre** | Contar la plata de la caja y compararla con lo que dice el sistema |
| **Expediente** | La carpeta digital con los papeles de un cliente o contrato |
| **Anular** | Dejar sin efecto una factura, sin borrarla |

---

*FAControl · Familia Almonte Auto Import SRL · Desarrollado por Yuber Santana*
