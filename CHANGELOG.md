# Changelog — FAControl

Formato: [Keep a Changelog](https://keepachangelog.com/es/1.0.0/). Fechas en hora de República Dominicana.

## [2.1.1] — 2026-09-05 · La actualización que decía "terminó" sin actualizar

Reporte de Verónica: *"ya descargue la actualización, le di a extraer aquí y
luego le di clic dos veces... cuando abro la aplicación está todo igual que
antes"*.

### Qué pasaba

La aplicación estaba abierta cuando corrió el actualizador. Con FAControl en
ejecución sus DLL quedan bloqueados y Windows no los puede reemplazar: en vez de
fallar, **difiere los archivos al próximo reinicio**. El asistente igual muestra
"la actualización terminó", el usuario nunca reinicia, y la versión nueva queda
en el limbo — sin ningún error a la vista.

El actualizador traía `CloseApplications`, que se apoya en el Restart Manager de
Windows, pero ese mecanismo no siempre alcanza: no ve procesos de otra sesión
cuando el instalador se eleva con otra cuenta, y a una ventana modal abierta no
la cierra.

### Corregido

- **El actualizador comprueba que la copia haya entrado.** Al terminar lee la
  versión del `FAControl.App.exe` que quedó en disco y la compara con la que
  traía. Si no coincide, lo dice con todas las letras y explica qué hacer
  (cerrar el programa y volver a ejecutar). Deja de ser un fallo silencioso.

- **Mutex de instancia** (`Global\FAControl.App.Instancia`). La aplicación lo
  levanta al arrancar y los dos instaladores lo declaran como `AppMutex`: Setup
  detecta que FAControl está abierto **antes** de copiar nada y pide cerrarlo.

  Esto protege de la PRÓXIMA actualización en adelante: la versión instalada hoy
  todavía no tiene el mutex, así que para esta ronda lo que avisa es la
  comprobación de arriba.

- **La versión, a la vista.** La pantalla de inicio muestra abajo "Versión
  2.1.1". Antes había que abrir Ayuda para saberla, y por eso nadie —ni el
  cliente ni el soporte— podía confirmar si una actualización había entrado.

- Los mensajes del asistente ahora piden cerrar FAControl **antes** de continuar
  y dicen dónde comprobar la versión al terminar.

### Nota para quien toque los .iss

Ninguna línea del `[Code]` puede EMPEZAR con `#13`: el preprocesador de Inno lee
un `#` al principio de línea como una directiva y aborta la compilación con
"Unknown preprocessor directive". Los saltos de línea van pegados al texto
anterior.

## [2.1.0] — 2026-09-04 · Barrido de errores y publicación

Repaso dirigido a lo que el cliente pidió mirar: **las impresiones, los archivos
y los cálculos**.

### Corregido

- **El cierre de caja en hoja Carta se recortaba en silencio.** Se arma como una
  pila de filas —una por cajero— y se mandaba con `PrintVisual`, que imprime lo
  que entra en la hoja y descarta el resto sin error ni aviso. Con tres o cuatro
  cajeros se perdía el total del día, que es justo lo que se archiva.

  Se agregó `VisualPaginado`, que reparte cualquier visual alto en varias hojas
  usando un VisualBrush con Viewbox absoluto (no reparenta nada, cosa importante
  porque el visual ya cuelga de la ventana de vista previa). Solo se activa en
  hoja suelta: en la térmica de 80mm el rollo es continuo y paginar sería el
  error, porque cortaría el ticket en pedazos.

- **Un mensaje de error mentía al cajero.** Al cobrar con abono a capital, si el
  cobro base ya se había llevado todo el capital pendiente, el sistema decía
  *"el abono (333.33) excede el capital pendiente del préstamo (1,000.00)"* —
  que no se entiende, porque 333.33 no excede a 1,000. El tope real no es el
  capital del préstamo sino el que queda **después** del cobro. Ahora el mensaje
  dice cuánto queda de verdad, o avisa que no queda nada para abonar.

  Lo encontró la auditoría nueva de reparto de pagos.

- **El acta no apocopaba los números terminados en uno.** Decía *"uno (01) mes"*
  y *"veintiuno (21) cuotas"*. En español se dice "un mes", "veintiún días",
  "treinta y una cuotas", y en un papel que se firma ante notario eso se lee como
  un descuido. `NumeroALetras` ahora sabe el género de la palabra que sigue, y
  la modalidad concuerda en número ("una cuota mensual", no "una cuotas
  mensuales").

### Agregado

- **Auditoría de invariantes del reparto de pagos** (`AuditoriaRepartoDePagosTests`).
  El reparto ya había producido dos errores de plata que ninguna prueba de
  ejemplo agarró. Esta recorre cientos de combinaciones de tabla y monto —los
  cuatro métodos, tasas de 0% a 35%, plazos de 1 a 24, y montos que caen en los
  bordes: un centavo, media cuota, una cuota exacta, la deuda entera— y verifica
  lo que nunca puede romperse: que lo repartido sume exactamente lo pagado, que
  no haya importes negativos, que no se cobre de más a una cuota, que el interés
  aplicado no pase el pendiente, que las cuotas se cobren en orden, y que pagar
  en dos veces termine igual que pagar de una.

- **Pruebas del migrador para 044 y 045.** Son las que le van a correr solas al
  cliente al abrir la versión nueva; el riesgo no es que fallen acá, es que
  fallen en su PC con la base cargada. Se prueban contra una base puesta como la
  suya —sin las columnas del acta y con la garantía en VARCHAR(255)— y se
  verifica además que se puedan repetir sin romper nada, porque un arranque
  interrumpido las deja a medias.

### Verificado sin hallazgos

- **Redondeo del dinero**: las 30 llamadas a `Math.Round` usan
  `MidpointRounding.AwayFromZero`. No hay un solo `float` ni `double` en montos.
- **Temporales**: los cinco archivos temporales que genera la app (PDF del
  expediente, PNG de la impresión térmica) se borran en `finally`.
- **Símbolos de la interfaz**: solo tres caracteres fuera del ASCII en toda la
  aplicación, y los tres se dibujan bien con las fuentes que trae Windows.

### Publicación

Versión **2.1.0**: `FAControl_Setup_2.1.0.exe` (instalación completa, con MySQL
y los demás prerequisitos) y `FAControl_Update_2.1.0.exe` (solo la aplicación,
para la PC que ya la tiene). El actualizador no toca la base, la contraseña, la
licencia, los ajustes ni los expedientes escaneados; el esquema lo pone al día
la propia aplicación al arrancar.


## [2.1.0] — 2026-09-04 · El acta que no cambia, y el barrido de idioma

### Corregido

- **El Launcher no abría maximizado.** Regresión introducida el mismo día:
  `VentanaAjustable` le ponía un `MaxHeight` a cada ventana, y WPF respeta ese
  máximo **también al maximizar**, así que el Launcher quedaba del tamaño del
  tope en vez de a pantalla completa. Ahora el tope se quita mientras la ventana
  está maximizada y vuelve al restaurarla. Afectaba igual al MainWindow.

- **Un icono salía como cuadrito vacío en la PC del cliente.** El botón de
  cerrar sesión usaba el carácter Unicode `⏻` (U+23FB) **sin declarar fuente**,
  así que caía en Segoe UI, que no lo tiene. Los otros dos botones de esa barra
  nunca fallaron justamente porque sí declaran `Segoe MDL2 Assets`. Ahora usa el
  glifo `E7E8` de esa misma fuente.

  Se barrió todo el XAML buscando el mismo defecto: en toda la aplicación hay
  solo tres símbolos fuera del ASCII, y los otros dos (`✓` en un texto de
  Configuración y `−` en el contador del POS) sí existen en Segoe UI.

- **Ninguna ventana puede quedar más grande que la pantalla.** Veintidós
  ventanas se abrían con alto fijo de 780–860px o atado al contenido y sin poder
  redimensionar. En un monitor grande se ven bien; en una laptop de 768px —que
  es lo que hay en el mostrador— crecían por debajo del borde, con los botones
  de guardar fuera de vista y sin forma de achicarlas. `VentanaAjustable` las
  recorta al área de trabajo real; siete formularios además pasaron a tener
  scroll y a poder redimensionarse.

### Agregado

- **El pagaré notarial queda congelado con el préstamo** (migración `045`,
  tabla `prestamo_acta`).

  Hasta ahora las PARTES del acta —el notario, quien firma por la empresa y los
  dos testigos— se leían de la configuración **en el momento de imprimir**. Si
  el año que viene cambia el notario, reimprimir un contrato firmado hoy sacaba
  un papel **distinto al que el deudor firmó**: mismo préstamo, otro notario,
  otros testigos. Para un documento con fuerza ejecutoria eso no es un detalle.

  Ahora se guarda la copia completa, con la ocupación, la nacionalidad y el
  **sexo** de cada parte (el acta declina en género y eso tampoco se puede
  perder). Entra en la misma transacción que el préstamo: o van los dos o no va
  ninguno. Los préstamos anteriores a este cambio siguen usando la
  configuración vigente — de esos no hay copia y no se puede inventar una.

- **Todos los campos del acta en Nuevo Préstamo**, en ocho bloques con rejilla
  de tres columnas. El notario, el representante, los testigos, el asiento
  social y las condiciones vienen **precargados desde Configuración**; cambiarlos
  ahí vale solo para ese préstamo.

  Y un interruptor, apagado por defecto: **"Guardar estos datos en Configuración
  (reemplazan a los actuales)"**. Lo normal es corregir un dato para un contrato
  puntual; prenderlo es decir "de ahora en más, estos son los datos del negocio".

- **El acta se corrige desde Préstamos → Detalle → Editar.** Va fuera del bloque
  de montos a propósito: son datos de un papel, no del dinero, así que se pueden
  arreglar **aunque el préstamo ya tenga cobros**. Lo que se guarda reemplaza la
  copia congelada, que es la que se usa al reimprimir, y queda anotado en el
  historial — pero solo si de verdad cambió algo, comparando parte por parte.

- **Casilla moderna** (`Casilla.Moderna`): cuadro redondeado con palomita
  vectorial, para las listas de opciones. Convive con `Switch.Moderno` y no lo
  reemplaza: el interruptor prende o apaga un modo, la casilla marca opciones de
  una lista. Veinte interruptores seguidos en la grilla de permisos serían
  ilegibles y ocuparían el triple de ancho.

### Cambiado

- **No quedan checkboxes con el estilo de Windows.** Los 24 que quedaban pasaron
  a interruptor moderno, salvo las tres listas de permisos por pantalla, que
  usan la casilla nueva.

- **El botón "Guardar e imprimir" pasa a "Crear e imprimir"**, también en los
  comentarios del código.

- **Botones de Nuevo Préstamo reorganizados**: "Crear préstamo" y "Crear e
  imprimir" comparten el ancho en la primera fila, "Ver contratos" toma el ancho
  completo abajo. Los tres del panel lateral reparten el ancho en partes iguales,
  con el texto más grande, centrado y partiendo de línea en vez de recortarse.

- **El idioma de la aplicación pasa de rioplatense a dominicano**: 256 cambios
  de voseo a tuteo (`tenés`→`tienes`, `elegí`→`elige`, `ingresá`→`ingresa`,
  `acá`→`aquí`…). Se hizo en dos pasadas: la segunda usó un patrón amplio
  —cualquier palabra terminada en vocal acentuada— y apareció otra docena que la
  lista fija no tenía. El sustantivo **pagaré** y los futuros normales
  (`pagará`, `tomará`, `devolverá`) quedaron intactos.


## [2.1.0] — 2026-09-03 · Los tres contratos del préstamo

Verónica (2026-08-26): *"Este es el contrato notarial, los dueños lo necesitan
subido en el sistema, de las dos formas automático y editable"*. Jean Carlo
(2026-08-27), sobre si eran varios: *"Vamos hacer ese solo"*.

Ahora un préstamo emite **tres documentos**: el pagaré de siempre, el acta
notarial de la plantilla, y el acta con la tabla de cuotas atrás.

### Agregado

- **Pagaré notarial.** El acta de la plantilla del cliente, armada con los datos
  del préstamo: monto, cuotas, tasa y fechas escritos en letras y en cifra como
  los escribe un notario, las siete cláusulas, la garantía, los testigos y las
  cinco firmas.

  El texto sigue a la plantilla salvo por las fallas evidentes, que se
  corrigieron: numeraba **dos cláusulas como "QUINTO"**, dejaba una frase
  cortada a la mitad (*"EL DEUDOR autoriza al SEXTO:"*) y traía erratas de tipeo
  (DOCIENTOS, taza, AMNBITO, DECIGNACION, ESPANCION, ALUZING, Hemanos, el Tes).
  El fondo jurídico no se tocó: las cláusulas dicen lo mismo, en el mismo orden,
  con la misma referencia al art. 545 del Código de Procedimiento Civil.

- **Concordancia de género.** El acta está declinada de punta a punta
  (dominicano/a, domiciliado/a, el señor / la señora, EL DEUDOR / LA DEUDORA).
  Se agregó el sexo del deudor para resolverla; sin eso el documento salía mal
  escrito la mitad de las veces, delante del notario del cliente.

- **`NumeroALetras`**: números, montos, porcentajes y fechas escritos como los
  escribe un notario dominicano. Escrito a mano en vez de traer una dependencia:
  las reglas del español son pocas y estables, y una librería genérica igual
  habría que envolverla para el formato notarial. 43 pruebas, con los casos
  tomados literalmente de la plantilla.

- **Los tres documentos, en las tres pantallas.**
  - **Contratos** (el almacén): el botón *"Pagaré"* de cada fila pasó a llamarse
    **"Contratos"** y abre una ventana con los tres, cada uno con su vista previa,
    su PDF y su impresión.
  - **Préstamos → Detalle**: botón **"Contratos"** con lo mismo, más una tarjeta
    nueva que muestra lo que se cargó del acta (acto, folio, municipio, cómo se
    describe al deudor, condiciones y Registro de Títulos). La tarjeta se
    esconde entera si no hay nada cargado.
  - **Nuevo Préstamo**: tres botones arriba de la vista previa lateral cambian
    el panel por el documento correspondiente; apretar el mismo otra vez vuelve
    a la tabla de amortización.

- **Tildes para elegir qué se imprime.** En Nuevo Préstamo hay tres casillas: lo
  tildado se imprime, lo destildado no. La elección se recuerda **por PC**, no
  por negocio: quién imprime qué depende de la impresora que tenga esa terminal
  al lado.

- **Datos del acta en Nuevo Préstamo**, en una sección plegable y **todos
  opcionales**: acto, folio, fecha, municipio, y cómo se describe al deudor
  (sexo, nacionalidad, estado civil, ocupación), más las condiciones del contrato
  (cuotas en atraso que vencen el plazo, días de gracia, mora, Registro de
  Títulos). Lo que quede vacío sale como una raya para llenar a mano, que es
  como se trabaja con un notario; bloquear la impresión por un campo vacío sería
  peor que imprimir el acta incompleta.

  Van en el préstamo y **no en el cliente** a propósito: son la foto del contrato
  al firmarlo. Si el deudor se casa el año que viene, el acta ya firmada tiene
  que seguir diciendo "soltero" — la misma regla por la que la factura congela el
  precio de catálogo.

- **Configuración → Pagaré notarial**: el asiento social, el municipio, el
  notario (con su matrícula del Colegio), quién firma por la empresa y los dos
  testigos. Se cargan una sola vez: cambian una vez cada varios años, no por
  contrato. También las condiciones por defecto, que cada préstamo puede pisar.

- **Aviso de lo que falta** al abrir el acta: dice qué campos quedaron en blanco
  sin impedir imprimir, y recuerda que el notario y los testigos se cargan en
  Configuración.

### Corregido

- **El pagaré nunca se archivaba.** `PagareWindow` sabía dejar una copia en el
  expediente, pero las dos pantallas que la abrían —el almacén de contratos y
  Nuevo Préstamo— nunca le pasaban el expediente, así que el archivado salía
  temprano y no guardaba nada. Estaba así desde el 2026-07-30, cuando se anunció
  que *"lo que se imprime queda guardado en el expediente del cliente"*.

  Ahora el expediente viaja siempre y **los tres documentos quedan archivados al
  imprimirlos**, en la ficha del préstamo correspondiente.

- **La garantía se truncaba.** `prestamo.garantia` era `VARCHAR(255)` y la
  descripción legal del inmueble del modelo del cliente (solar, superficie,
  designación catastral, ubicación y la mejora con techo, piso y habitaciones)
  mide casi 400 caracteres: MySQL la cortaba en silencio o rechazaba el INSERT
  según el `sql_mode`. Pasa a `TEXT` (migración `044`).

### Cambiado

- **"Crear préstamo" ya no imprime.** Hasta el 2026-09-02 el botón abría además
  la vista previa del pagaré. Ahora guarda y nada más; imprimir es una decisión
  aparte, con su propio botón ("Guardar e imprimir") y sus tildes. El botón
  "Ver pagaré" pasó a "Ver contratos" y muestra los tres.

- Si al imprimir varios falla uno, **los demás igual salen**: los errores se
  juntan y se avisan una sola vez al final, nombrando cuáles no se pudieron.

### Migración

`044_contrato_notarial.sql` — idempotente, se aplica sola al arrancar. Agrega
las doce columnas del acta a `prestamo` y pasa `garantia` a `TEXT`.

### Pendiente para el cliente

El acta habla de una **mora del 20%**, pero FAControl no tiene ningún concepto de
recargo por atraso: hoy el número se imprime en el contrato y nadie lo calcula.
Si la van a cobrar de verdad, es una funcionalidad aparte.


## [2.1.0] — 2026-09-03 · El comprobante fiscal se aprende solo

Pedido del cliente 2026-09-03. La idea de fondo: dejaron de numerar desde el
talonario local y numeran desde el Facturador Gratuito de la DGII, así que la
app tenía que seguirlos a ellos en vez de obligarlos a corregir Configuración
a mano después de cada cobro.

### Agregado

- **Marcador con el próximo comprobante** en todas las cajas de NCF: cobro de
  cuota, préstamo nuevo, cobro de alquiler, abono a plazo y "Asignar" en el
  detalle del préstamo. Se ve en gris el número exacto que va a salir
  (`B0200000046`), no un texto genérico.

  **Si la estancia no tiene secuencia configurada no se muestra marcador en
  ninguna caja**, que era la parte explícita del pedido. Tampoco se muestra si
  la secuencia está apagada, vencida o agotada: un marcador con un número que
  la app no va a poder entregar es peor que ninguno.

  La pieza es reusable y no sabe nada de NCF: propiedad adjunta
  `Marcador.Texto`, pintada por el template de `Input.Texto`. Se hizo así, y no
  con un TextBlock suelto en cada pantalla, porque el marcador tiene que
  alinearse exacto con el texto real y eso solo lo garantiza el template.

- **El switch dice el número.** Antes decía "Tomar el siguiente de la secuencia
  configurada"; ahora dice **"Usar el comprobante B0200000046"**. Cuando no hay
  secuencia se apaga, se deshabilita y explica dónde configurarla — antes se
  podía prender y la operación fallaba recién al guardar, con un error de base
  de datos en la cara del cajero.

- **El NCF digitado a mano queda de predeterminado.** Si se escribe un
  comprobante en un cobro, un préstamo, un alquiler o un abono a plazo y la
  operación sale bien, esa serie pasa a ser la activa y la secuencia continúa a
  partir de ese número. Vale por los cinco caminos y se aplica de nuevo cada vez
  que se cambia, incluso volviendo a una serie anterior (retoma su propia
  numeración, no la de la otra).

  La adopción corre **después del commit y nunca propaga**: el cobro es un hecho
  financiero, mover la numeración predeterminada es una comodidad. Si lo segundo
  falla, lo primero sigue siendo válido y el usuario no ve ningún error.

### Cambiado

- **La casilla de Configuración → Comprobante fiscal ahora limpia todo al
  encenderse**, no solo el ejemplo de fábrica. Antes, reactivar una secuencia
  guardada la respetaba para no hacerle perder al usuario en qué número iba.
  Lo que cambió es de dónde sale ese número: ahora se recupera solo desde el
  primer comprobante que se digite, así que lo peligroso pasó a ser lo
  contrario — reactivar la casilla y quedarse sin notarlo con los números de un
  talonario viejo o de otra estancia.

- **Un e-NCF externo ahora SÍ mueve la secuencia local.** Era exactamente lo
  contrario hasta el 2026-09-02, y era deliberado: ese número lo emitió el
  portal de la DGII, no la app. El test que afirmaba el contrato viejo
  (`El_comprobante_pegado_a_mano_va_al_recibo_sin_tocar_la_secuencia`) se
  reescribió en vez de borrarse, para que quede asentado que el cambio fue
  pedido y no un descuido.

### Nota sobre una desviación deliberada

El pedido dice que el NCF digitado se adopta siempre. Se cumple **salvo un
caso**: dentro de la MISMA serie, la secuencia no retrocede. Escribir un número
más viejo que el actual volvería a entregar comprobantes ya consumidos, que la
DGII prohíbe reusar y que `uq_prestamo_ncf` / `uq_pago_ncf` rechazarían más
tarde con un error de base de datos. Si la serie **cambia** sí se adopta tal
cual, porque un talonario nuevo trae su propia numeración y no pisa nada.
Está probado en `NcfPredeterminadoTests.MismaSerie_MasAtras_NoRetrocede` y es
fácil de revertir si el cliente prefiere lo literal.

### Sin migración

No hubo cambios de esquema: todo se apoya en `ncf_secuencia`, que existe desde
`012_ncf.sql` y quedó por modo en `030_ncf_por_modo.sql`.


## [2.1.0] — 2026-08-27 · Comprobante fiscal por cobro y arreglos de impresión

### Corregido

- **El capital abonado se guardaba mal.** `cuota.monto_pagado` era un solo
  acumulador y el reparto interés/capital se DEDUCÍA con la regla "primero
  interés". Esa regla vale para un cobro normal, pero un abono a capital NO
  paga interés: de cada abono se comía una cuota de interés. En el ejemplo del
  cliente, tras abonar 200,000 el capital pendiente quedaba en 820,000 en vez
  de 800,000, y el error se acumulaba con cada abono siguiente.

  Ahora se guarda en vez de deducirse (`cuota.capital_pagado`, migración `043`).
  El backfill es exacto: `pago.monto_capital` lleva el reparto verdadero desde
  el primer día, así que la columna nueva es la suma de lo que ya existía.

- **El saldo restante del recibo restaba el cobro dos veces** en un caso
  introducido durante esta misma ronda: el bucle de cobro pasó a actualizar las
  cuotas en memoria (las necesita el recálculo del préstamo abierto), pero
  `saldoAntes` se leía DESPUÉS del bucle. Se movió a antes, que es donde el
  nombre dice la verdad. Lo cazó `FlujoPrestamoPagoTests`.

- **Las facturas de un préstamo salían todas con el mismo comprobante fiscal.**
  Reporte de Verónica (2026-08-26): *"las facturas de ese préstamos todas salen
  con es NCF y eso se debe cambiar con cada factura"*.

  El NCF vivía solo en `prestamo`, y `PagoService` copiaba ese número al recibo
  de cada cobro (`Ncf: prestamo.Ncf`). Un préstamo de 24 cuotas entregaba 24
  facturas con UN comprobante repetido — ante la DGII cada factura ampara el
  suyo, así que el libro de ventas no cuadraba.

  El comprobante pasó a la operación que de verdad factura: el cobro
  (migración `041`). Un cobro que toca varias cuotas guarda el NCF en su fila
  principal, porque fiscalmente es UN documento aunque genere N recibos; así
  `uq_pago_ncf` protege de repetir un número entre cobros, igual que
  `uq_prestamo_ncf`. Un cobro sin comprobante ya **no hereda** el del préstamo.

- **El recibo decía "Abonado" en una cuota pagada completa.** Reporte del
  cliente (2026-08-27): *"dice abonado y pone el total de interés y capital
  pagado en una cuota normal"*. "Abonado" es un pago PARCIAL en el habla
  dominicana, así que la palabra contradecía el hecho. Ahora dice "Pagado:" si
  la cuota quedó saldada y "Abonado (parcial):" si no. El dato ya existía
  (`AplicacionPago.QuedaPagada`); simplemente no llegaba al papel.

- **Todo PDF se guardaba en papel de 80mm.** `ImpresoraRecibos.GuardarPdf`
  fijaba el ancho a mano, pero lo llaman tres ventanas con dos anchos: el
  recibo (302 DIU = 80mm) y la factura de venta y la ficha de vehículo (816
  DIU = carta). Guardar una factura daba una tira de 8cm con el contenido de
  una hoja carta encogido adentro. Ahora el tamaño se **deriva** del visual.
  De paso: título propio por documento (antes todos decían "Recibo de pago"),
  medición defensiva si el visual llega sin layout, y fondo blanco explícito.

- **La hoja del préstamo se recortaba al imprimir.** `PrestamoImpresionWindow`
  usaba `PrintVisual`, que no pagina, sobre un visual carta con la tabla de
  amortización completa: en un préstamo largo la cola de la tabla se perdía sin
  aviso. Es el mismo defecto que fue el BLOCKER del pagaré el 2026-07-17, que
  entonces se arregló solo para el pagaré. Nuevo `PrestamoDocumentFactory`
  (FlowDocument, pagina solo); en pantalla sigue el visual con scroll.

### Agregado

- **El interés se recalcula sobre el capital rebajado** en el préstamo abierto
  (pedido del cliente 2026-08-27): *"si un cliente toma RD$1,000,000 [...] en
  noviembre realiza un abono de RD$200,000 al capital [...] A partir del
  siguiente mes, los intereses deben calcularse únicamente sobre los RD$800,000
  restantes"*. Antes el deudor seguía pagando 20,000 de interés cuando le
  correspondían 16,000 — 4,000 de más por mes.

  Alcance acotado a propósito (decisión de Yuber 2026-08-27):
  * **Solo `SoloInteres`.** En francés y cuota fija la tabla se pacta al firmar;
    reescribirla cambiaría un contrato ya acordado con el cliente.
  * **Solo si hubo abono a capital.** Un cobro normal no reescribe nada.
  * **Solo cuotas NO vencidas y NO pagadas.** El interés de una cuota vencida se
    devengó sobre plata que el deudor efectivamente tenía; bajarlo hacia atrás
    le perdonaría interés ya ganado. Queda anotado en la auditoría del cobro.

- **Comprobante fiscal manual en los tres puntos de cobro** (pedido 2026-08-24):
  "Registrar pago" de PrestControl, cobro de alquiler y abono a plazo de una
  venta. En cada uno se pega el e-NCF del Facturador Gratuito de la DGII, o se
  prende el switch y se toma el siguiente de la secuencia de esa estancia.
  Migraciones `041` y `042`.

  La reserva vive **dentro de la transacción del cobro**: usa `FOR UPDATE`, así
  que dos cajas cobrando a la vez no se llevan el mismo número, y si el cobro
  falla el rollback devuelve el comprobante sin consumir.

- **`Switch.Moderno`**: el "checkbox moderno" que pidió el cliente. El CheckBox
  nativo usa el chrome de Windows —el mismo look que ya se cambió en los
  ComboBox—. Sigue siendo un CheckBox, así que ningún binding cambia.

- Columna **NCF** en las grillas de cobros de alquiler y de abonos: DealControl
  no imprime recibo individual del cobro, y sin la columna el número quedaría
  guardado pero invisible.

## [2.0.2] — 2026-08-20 · La ronda de la prueba de Verónica

### Corregido

- **El pagaré no salía al crear un préstamo**, y en su lugar aparecía *"No se
  puede establecer la propiedad Owner en un elemento Window que se ha cerrado"*.
  La causa no estaba en el pagaré: los ViewModels son **singleton** y el shell
  se **rehace** en cada "Cerrar sesión" / "Cambiar usuario". Las vistas de los
  shells viejos nunca se soltaban del ViewModel, así que al pedir el pagaré
  atendía primero una vista muerta, que intentaba abrir su ventana colgando de
  un shell ya cerrado. La excepción cortaba la lista y la vista buena —la que
  estaba en pantalla— nunca llegaba a mostrar el papel.

  Dos arreglos, no uno:
  1. Las vistas **se sueltan del ViewModel al salir de pantalla** (`Unloaded`)
     y se vuelven a enganchar al entrar. Deja de acumularse basura por cada
     inicio de sesión.
  2. Ninguna ventana se abre colgando de otra que ya se cerró
     (`VentanaDuena`). Vale para las 20 ventanas de la aplicación, no solo para
     el pagaré: el recibo, la factura, la intimación, el cierre de caja, el
     ticket del POS, los avisos automáticos y los diálogos de confirmación
     tenían todos la misma puerta abierta.

  Se llegaba con: entrar → **Cambiar de usuario** → crear un préstamo.

- **Corregir un préstamo de "interés fijo primero, capital después" le cambiaba
  la tabla al cliente.** La ventana de corrección no mostraba desde qué cuota se
  cobra el capital, así que al guardar se recalculaba con la cuota **sugerida**
  en vez de la **pactada**: un préstamo pactado con capital desde la 4 volvía a
  salir desde la 5, con otras cuotas que las que el cliente firmó. Ahora el dato
  está en la ventana, con lo que se pactó, y se puede corregir.

### Cambiado

- **El ícono es el de Familia Almonte.** Era una "P" morada heredada de la
  plantilla del proyecto anterior. Ahora es el monograma **FA** sobre el navy
  de la marca, generado del **mismo logo vectorial** que usa la aplicación
  (`scripts/marca/generar_icono.py`), en los nueve tamaños que pide Windows.
  Se ve en la barra de tareas, en el escritorio, en el instalador y en
  "Agregar o quitar programas".

### Agregado

- **`docs/CORREO-GMAIL.md`**: la guía paso a paso del correo automático, y va
  instalada con la aplicación. Existe por un motivo concreto: al ir a generar
  la contraseña de aplicación, Google contesta *"la configuración que buscas no
  está disponible para tu cuenta"* y no explica nada. Lo que falta es
  **prender la verificación en 2 pasos** — sin eso la página de contraseñas de
  aplicación no existe. La ayuda de Configuración ahora empieza por ese paso y
  lleva el enlace directo.

- **Barrido de invariantes de los cálculos del POS-500**
  (`AuditoriaCalculosPosTests`), hermano del que ya existía para la
  amortización. Recorre cientos de combinaciones de canasta, tasa de ITBIS y
  modo de redondeo y verifica lo que nunca puede romperse: el subtotal es
  exactamente la suma de las líneas, el ITBIS sale del subtotal de una sola vez
  (nunca acumulado por línea), ningún importe pasa de 2 decimales, el redondeo
  no mueve el total más de un peso y "hacia arriba" nunca cobra de menos.
  También cubre el cambio, la comisión del vendedor, el número de factura y el
  semáforo de caducidad. **No encontró errores**: queda como red para los
  cambios que vengan.

- **`scripts/calidad/verificar_recursos_xaml.py`**, traído de MED-100. WPF
  resuelve `StaticResource`/`DynamicResource` en tiempo de EJECUCIÓN: una clave
  mal escrita compila sin una advertencia y revienta recién cuando alguien abre
  esa pantalla. Acá el riesgo es mayor que en MED-100 porque las tres estancias
  pisan claves con sus propias paletas. 90 claves usadas, todas definidas.

### Sin cambios (a propósito)

- **"Interés fijo primero, capital después" sigue preguntando "¿desde qué cuota
  se cobra el capital?"**. Vino el reporte de que poniendo 6 el sistema mostraba
  "cuotas 1 a 5", y se llegó a dar vuelta la pregunta; pero era una confusión de
  uso, no un error: el campo ya permite escribir la cuota exacta, y para seis
  meses de puro interés el número es 7. Se deja como estaba.

### Notas para el técnico

- **Qué mandar:** `FAControl_Update_2.0.2.exe` a la PC que ya tiene FAControl.
  El instalador completo solo para una PC nueva.
- El ícono nuevo puede tardar en aparecer en el escritorio: Windows cachea los
  íconos. Si queda el viejo, `ie4uinit.exe -show` lo refresca.

## [2.0.1] — 2026-08-07 · Dos errores de centavos en la amortización

Salieron de una auditoría de los cálculos: un barrido de más de 2,600
combinaciones de monto, tasa, plazo y modalidad contra los cuatro métodos,
verificando las reglas que nunca pueden romperse (el capital devuelto es
exactamente el prestado, el saldo termina en cero y nunca sube, ningún importe
es negativo). Los dos errores estaban desde antes de esta ronda.

### Corregido

- **Interés negativo en la última cuota (interés fijo dominicano).** El interés
  total se redondeaba aparte del interés por cuota y la última absorbía la
  diferencia; cuando el interés por cuota era de centavos y redondeaba para
  arriba, lo acumulado superaba al total y la última cuota salía en negativo.
  Pasaba con **préstamos diarios chicos**, que son los de todos los días:
  RD$500 al 1% a 60 días daba −0.03; RD$700 al 0.5% a 100 días, −0.21.
  Ahora el interés es el mismo en todas las cuotas —que es la definición del
  interés simple, y lo que el prestamista le dice al cliente— y solo el capital
  absorbe el redondeo en la última.
- **Saldo negativo en el sistema francés.** La cuota se redondea para arriba, así
  que con tasas altas y plazos largos el préstamo se saldaba antes de la última
  cuota y el cálculo seguía restando capital: el saldo se iba a negativo
  (RD$1,000 al 10% × 100 cuotas llegaba a −135.69) y la última cuota terminaba
  con capital negativo, como si hubiera que devolverle plata al cliente. Ahora
  el abono nunca puede pasar del saldo que queda.

### Notas

- **No afecta a los préstamos ya cargados**: sus cuotas están guardadas y no se
  recalculan. El cambio rige para los préstamos nuevos.
- En los casos extremos del francés (tasa alta y plazo muy largo) la deuda queda
  saldada antes de la última cuota y las que sobran salen en cero. Es correcto:
  ya no hay nada que cobrar. Si aparece eso, el método que corresponde a ese
  préstamo es el abierto, no el francés.

## [2.0.0] — 2026-08-06 · Se actualiza sin reinstalar

Es 2.0.0 y no 1.9.3 por una sola razón: **a partir de esta versión FAControl se
actualiza solo**. Es el último release que hay que instalar entero.

### Agregado

- **Actualizador** (`FAControl_Update_2.0.0.exe`, ~63 MB contra los ~886 MB del
  instalador completo). Reemplaza solo el programa: no trae MySQL ni AnyDesk ni
  Google Drive, no pregunta carpeta y **no toca la base de datos**. Conserva la
  contraseña configurada, la licencia, los ajustes y los expedientes escaneados.
  Si FAControl no está instalado en el equipo, se niega a correr y manda a usar
  el instalador completo.
- **La aplicación pone la base al día sola al arrancar** (`MigradorEsquema`). Las
  migraciones viajan dentro del ejecutable y se aplican las que falten, anotadas
  en la misma tabla `esquema_migracion` que usa `aplicar.ps1`. Ya no hace falta
  correr PowerShell ni saber la contraseña de MySQL en la PC del cliente.
  Las migraciones históricas (hasta la 039) nunca se ejecutan, solo se anotan:
  varias no son repetibles y correrlas rompería la base en vez de arreglarla.
- **Método de cálculo "interés fijo primero, capital después"** (040). Las
  primeras cuotas son de puro interés y, desde la cuota que se elija, cada una
  lleva además un abono a capital fijo; el interés pasa a calcularse sobre el
  saldo y la cuota va bajando. Con **modo automático** (un tercio del plazo) y
  **modo manual** (el usuario escribe la cuota exacta). Reproduce, número por
  número, la tabla de amortización que mandó el cliente: 150,000 al 5% mensual,
  18 cuotas con 6 de interés fijo.
- **Historial de buena conducta en la ficha del cliente**. Cuántos préstamos
  saldó, qué porcentaje de cuotas pagó en fecha, el promedio y el peor atraso, y
  una calificación (Excelente / Buen pagador / Irregular / Riesgoso). Todo sale
  de lo que ya está en la base: no hay nada que cargar a mano.
- **Alquileres: tarifa por día y por mes**. Se escribe una y la otra se completa
  sola (mes = 30 días). Muchos alquileres se pactan hablando de "tanto al mes" y
  la cuenta se venía haciendo a mano.
- **Aviso cuando el respaldo automático falla**. Antes el error solo iba al log:
  podía estar fallando meses y en Configuración se seguía viendo la fecha del
  último respaldo bueno, que se lee igual que "todavía no toca".

### Corregido

- El respaldo automático dejaba de avisar sus fallos (ver arriba). Se anota el
  motivo y se muestra en Configuración hasta que un respaldo salga bien.

### Notas para el técnico

- **Qué mandar:** `FAControl_Update_2.0.0.exe` a la PC que ya tiene FAControl;
  `FAControl_Setup_2.0.0.exe` solo a una PC nueva.
- Al abrir FAControl la primera vez después de actualizar, el arranque puede
  tardar unos segundos más: está aplicando la migración 040.
- Si la migración fallara, la aplicación **no abre** y lo dice con el motivo. Los
  datos no se tocan y la versión anterior sigue sirviendo.

## [1.8.0] — 2026-07-30 · Contratos de PrestControl con expediente

### Agregado

- **El expediente digital ahora sirve a los dos módulos** (026). Nació atado a la
  venta del dealer; ahora un expediente cuelga de una venta **o** de un préstamo.
  La tabla pasó a llamarse `documento` y lleva las dos claves, de las cuales va
  exactamente una — con clave foránea de verdad en ambos casos, que es lo que
  evita expedientes huérfanos.
- **Contratos de PrestControl tiene su expediente**, con la misma lógica que
  DealControl: subir varios papeles de una vez, verlos en la lista, doble clic
  para abrir con la aplicación de Windows que corresponda, y bajar todo en un ZIP.
  Re-ubicar y eliminar siguen siendo solo del Admin.
- **El pagaré y la intimación se archivan solos al imprimirlos**, en el
  expediente del cliente correspondiente. Si el archivado falla no se molesta al
  usuario: el papel ya salió, que era lo que pidió; el fallo queda en el log.

### Notas

- Los documentos que ya existían **conservan su ruta** y se abren igual: la ruta
  se guarda por documento, no se recalcula. Los nuevos van a `ventas/<id>/` o
  `prestamos/<id>/`, lo que además evita que la venta 5 y el préstamo 5
  compartan carpeta.
- Re-ubicar un papel solo ofrece destinos **del mismo módulo**: un documento de
  una venta no se manda al expediente de un préstamo.

## [1.7.1] — 2026-07-30 · Correcciones tras la primera prueba del POS

### Impresión del ticket

- **Sección "Impresión del ticket" en Configuración del POS**, que había quedado
  fuera al portar el punto de venta: elegir la impresora del mostrador, ver o no
  el ticket antes de imprimir, copias, y encabezado y pie del papel.
- Eso explica el **"me mandó a guardar un PDF en vez de imprimir"**: sin
  impresora elegida la app usa la predeterminada de Windows, que en muchas PC es
  "Microsoft Print to PDF". Eligiendo la del mostrador, el ticket sale derecho.

### Instalador

- **1.7.1 con los tres prerequisitos adentro** (MySQL, AnyDesk y Google Drive):
  282 MB. Se ofrecen como casillas antes de abrir FAControl y se pueden
  desmarcar. Van con su ventana visible a propósito — MySQL pide la contraseña
  de root y esa se elige con el cliente delante.

### Corregido

- **No se podía vender.** Fallaba con "foreign key constraint fails
  `fk_factura_usuario`": la base separada del punto de venta conservaba su
  propia tabla `usuario` vacía, del POS-500 independiente, y la factura apuntaba
  ahí en vez de a los usuarios de la suite. Se resolvió unificando las bases.
- **Panel y Reportes no aparecían en el punto de venta** aunque las pantallas
  estaban: las dos condiciones del sidebar no contemplaban el modo nuevo.

### Cambiado

- **Una sola base de datos.** Las tablas del punto de venta pasan a `facontrol_db`
  con prefijo `pos_` (024). El motivo lo puso el cliente: **dos respaldos
  confunden al usuario** — veía dos `.sql` en la carpeta y no sabía cuál era el
  bueno. Ahora hay un solo archivo y no se puede restaurar media empresa. De paso
  la factura vuelve a tener una clave foránea real contra el usuario que la emitió.
- **El Historial distingue por módulo** (025): cada línea guarda en qué estancia
  se hizo, el historial arranca filtrado por la estancia donde estás parado, y
  desde el filtro se puede abrir a todos los módulos. Las líneas anteriores a
  este cambio se muestran con guion: la auditoría no se reescribe.
- **Detalle del préstamo**: el comprobante fiscal se movió a la derecha y la nota
  usa todo el ancho, así deja de dominar la pantalla. Sin notas, esa sección ni
  aparece.

### Agregado

- **Roles y permisos del POS-500 en la pantalla de Usuarios**, con su rol por
  estancia y sus permisos por pantalla, igual que PrestControl y DealControl.
- La acción **anular** entró al filtro de acciones del Historial, y las tablas
  del punto de venta al de entidades.

## [1.7.0] — 2026-07-30 · POS-500 integrado a la suite

> Decisión con Yuber (2026-07-30): el punto de venta pasa a ser un modo más de
> FAControl —mismo login, mismos usuarios y permisos— con sus DATOS en una base
> aparte (`pos500_db`), porque se vende por separado. Esta entrega cubre los
> cimientos y las capas de datos, negocio e impresión; faltan las pantallas.

### Hecho

- **Modo POS-500** en el launcher, con su color y su clave de base `pos500`. Se
  habilita con el código 5, que ya existía: la clave que guardaba coincide con la
  del modo, así que activar es literalmente digitar el código.
- **Base propia `pos500_db`**, creada sola la primera vez que se entra al modo.
  Lleva SOLO las tablas del punto de venta; usuarios, roles, permisos, sesiones y
  auditoría siguen en `facontrol_db`, compartidos por toda la suite.
- **022**: permisos del punto de venta (vender, productos, almacén, caducidad,
  comprobantes y comprobantes de todos, cuadre y cuadre de todos, anular
  facturas, acceso al modo) y roles **Supervisor / Cajero / Vendedor** propios
  del POS, todo dentro de la base compartida.
- **023**: la auditoría acepta la acción **anular**. Una factura emitida no se
  edita ni se borra nunca, se anula, y eso tiene que poder buscarse solo en el
  Historial.
- **Capas de datos, negocio e impresión portadas**: productos, clientes del
  mostrador, facturación con ITBIS, cuadre de caja, analítica, exportación y los
  tickets de 80mm.
- **La auditoría del POS entra en la MISMA transacción de la venta** aunque viva
  en otra base: se califica el esquema en el INSERT, y como la transacción es de
  la conexión y no de la base, la venta y su línea de historial se guardan o se
  pierden juntas. Verificado con una prueba que fuerza el fallo por falta de
  stock y comprueba que no queda ni factura ni rastro.

### Pantallas

- Las **nueve pantallas del punto de venta dentro del shell de la suite**: Panel,
  Vender, Clientes, Productos, Almacén, Caducidad, Buscar comprobante, Cuadre de
  caja y Reportes — con el mismo sidebar, el mismo tema claro/oscuro y el mismo
  tamaño de texto que el resto. Panel, Clientes y Reportes comparten página con
  los otros modos y se resuelven según la estancia activa, igual que el panel del
  dealer: nunca se mezclan los datos de un módulo con los de otro.
- **Un solo login**: el cajero entra una vez y el POS usa ese usuario. Ya no hay
  usuarios del punto de venta aparte.
- El sidebar del POS **se arma con los permisos** del empleado: un Cajero ve
  vender, clientes, sus comprobantes y su cuadre; un Supervisor ve todo el piso
  de venta, incluidos los comprobantes y la caja de los demás.
- **Tickets y cierres de caja**: la venta imprime directo (con un cliente
  esperando no se pregunta nada) y cae en vista previa si la impresora falla; la
  reimpresión desde Comprobantes siempre muestra la vista previa, y el cierre de
  caja también. La factura ya está guardada antes de imprimir: nada de lo que
  pase con el papel la afecta.
- La base del punto de venta **se crea sola** la primera vez que se entra al modo.
- **El respaldo automático saca las dos bases**: la de la suite y la del punto de
  venta. Si el cliente no tiene el POS, ese respaldo simplemente no se hace y
  queda anotado en el log — no rompe el principal.
- **Instalador 1.7.0**.

## [1.6.0] — 2026-07-30 · Licencia por módulo, préstamo abierto y la cartera real del cliente

### FAControl (la suite)

- **Siete códigos** en lugar de cuatro, con **activación por módulo** (pedido del cliente 2026-07-29): prueba de 14 días, activación total, PrestControl, DealControl, POS-500, respaldar y limpiar todo, eliminar todo. Los códigos por módulo **solo se piden cuando termina la prueba**: durante los 14 días la suite está abierta completa, y lo que se compre durante la prueba sigue valiendo después. En el launcher, un módulo sin código muestra **REQUIERE CÓDIGO** y dice cuál falta.
- **Eliminar todo** (código 7) es nuevo y no respalda nada: borra base, expedientes, ajustes y licencia para retirar la instalación de una PC. Exige escribir `ELIMINAR` y dos confirmaciones. La marca de inicio de prueba del registro **no** se borra: eliminar no sirve para estirar los 14 días.
- **Cuenta de respaldo del desarrollador** sembrada con el esquema (`Yub`, rol Programador). La instalación **sigue pidiendo** las credenciales del primer Admin como si no existiera, y ningún Admin la ve ni la puede tocar. Cada login suyo queda con Warning en el log. Reemplaza al viejo código de recuperación de acceso, que se retiró: dos puertas traseras para lo mismo era una de más.
- **Arranque directo**: casilla en la pantalla de inicio para que la app abra siempre el mismo módulo, sin pasar por el launcher. Se apaga desde Configuración → Apariencia de cualquier módulo. Solo aplica al arranque: al cerrar sesión vuelve a aparecer el launcher, para poder cambiar de estancia.
- **Botón de ayuda** con el teléfono del desarrollador (849-438-0242), en el launcher y en el sidebar de cada módulo: copiar el número, abrir WhatsApp y qué contar al escribir.
- **AutoControl salió de la suite**: DealControl ya hace sus operaciones. Su lugar en el launcher lo ocupa **POS-500** como producto a la venta (código 5 registra la compra). El valor sigue en la base para no migrar los datos históricos, pero ya no se entra ni se asignan roles de esa estancia.

### PrestControl

- **Préstamo abierto (solo interés)** — método de amortización nuevo. El cliente paga **solo el interés** cada período y el capital queda abierto hasta que decida saldarlo: N cuotas de puro interés y el capital completo en la última, que es una proyección y no un vencimiento pactado. **7 de los 10 préstamos reales del cliente son así** y ninguno de los dos métodos anteriores los representaba sin inventar abonos a capital.
- **Comprobante fiscal con la autorización real de la DGII** (constancia del 29/07/2026, autorización 6005407803): B01 Factura de Crédito Fiscal, del B0100000001 al B0100000015, vence 31/12/2027. Probado de punta a punta contra MySQL: los 15 salen en orden y sin repetir, el 16 se bloquea en vez de inventarse un número, un rollback no quema el comprobante y una autorización vencida no emite.
- **La cartera real cargada**: 10 clientes con sus préstamos, cargados con los mismos servicios de la app (códigos, cuotas y auditoría idénticos a haberlos tecleado). Las inconsistencias del listado quedaron escritas en las notas de cada préstamo y en un informe aparte; los pagos ya hechos **no** se cargaron a ojo.

### Verificación

- **Aislamiento entre estancias probado contra MySQL**: los clientes de un módulo no aparecen en el otro, la misma cédula puede existir en los dos, y lo único compartido es lo que pidió el cliente — usuarios, roles por módulo y permisos.
- **Instalador 1.6.0** con prerequisitos opcionales (MySQL, AnyDesk, Google Drive): si los instaladores están en `installer\prerequisitos\`, el asistente ofrece instalarlos antes de abrir FAControl; si no están, compila igual.

## [1.5.0] — 2026-07-27 · Códigos del producto, expediente digital y primer instalador de la suite

### FAControl (los tres modos)

- **Cuatro códigos digitables en el launcher**, con botón propio en el pie:
  1. **prueba de 2 semanas** — 14 días de uso completo; al pasarse, la app se bloquea;
  2. **activación total** — habilita el producto en firme;
  3. **recuperar acceso** — para cuando el cliente perdió todas las contraseñas: le pone contraseña nueva a una cuenta (o la crea) con acceso total, **sin tocar un solo dato del negocio**;
  4. **restablecer todo** — deja el sistema como recién instalado, con **respaldo obligatorio antes** y doble confirmación; si el respaldo falla, no se borra nada.
  El estado de la licencia vive en `licencia.json` **firmado**, con la marca del inicio de prueba también en el registro de Windows: borrar el archivo no reinicia los 14 días. Los códigos solo viajan **hasheados** en el binario (están en un MD privado del desarrollador, fuera del repositorio).
- **Rol Programador**: autoridad total sobre todo el sistema, **invisible e intocable para el Admin** — no aparece en la lista de usuarios, no se puede editar, desactivar ni cambiarle la contraseña. Solo otro Programador puede crearlo o asignarlo, y la única vía para la primera cuenta es el código 3.

### DealControl

- **Grids**: las columnas se miden por su contenido y la tabla se desplaza de lado cuando no caben — se acabaron las columnas que cortaban la información. El alto de fila queda fijo y cada tabla tiene su propio scroll.
- **Panel en modo noche**: "Últimos movimientos" pasó a ser una tabla del tema; antes eran textos negros sobre fondo oscuro.
- **Ficha de cliente propia del dealer**: cambian las métricas de PrestControl por las que sí significan algo acá — **Total transferido** (compras + alquileres), **Total cobrado**, **Saldo pendiente**, **Vehículos comprados/alquilados** y **Plazos vencidos** — y el grid muestra **sus vehículos** (compra o alquiler, con matrícula, chasis, color, estado y pendiente) con botón **Ver ficha** al detalle del vehículo.
- **Gráficos**: en el panel, **ventas vs alquiler de los últimos 6 meses** y **torta del inventario** por estado; en reportes, **de dónde vino el dinero** y **monto vendido por vendedor**.
- **Expediente digital del contrato** (Financiamiento de la venta): subir **varios archivos a la vez**, verlos en **lista** o en **cuadrícula** con su ícono, y **descargar todo en un ZIP** para migrar cuando haga falta. Doble clic sobre un documento abre el "ver automático": **Abrir** con la app de Windows que corresponda (Word, Excel, visor de fotos…), **Guardar** una copia, **Re-ubicar** en otro contrato y **Eliminar** — los dos últimos, **solo Admin**. Se aceptan PDF, Word, Excel, comprimidos e imágenes (incluidas las de iPhone). Los expedientes entran en el **respaldo automático**, en su propio ZIP.
- **Factura firmada**: botón para **reemplazar la factura del sistema por la escaneada y firmada**, que queda guardada en el expediente y se puede volver a abrir desde ahí. La factura generada se sigue pudiendo imprimir.
- La **cantidad de documentos** del expediente de contratos ahora suma los archivos reales, no solo los que emite la app.

### Cambios técnicos
- Migraciones `017_rol_programador.sql` y `018_expediente_documentos.sql`, idempotentes, con espejo en `001` y aplicadas a `facontrol_db`.
- El archivo del expediente vive en **disco** (`<app>\expedientes\<venta>\`) y la base solo guarda su ficha: un BLOB por cada foto de cédula haría lento e inmanejable el respaldo.
- **Lista blanca de extensiones** en el expediente: nada de `.exe`, `.bat` ni `.lnk`, porque los documentos se abren con doble clic.
- `SesionActual.EsAdmin` incluye al Programador; el blindaje real vive en `UsuarioService` (la UI solo oculta lo que el servicio ya rechaza).
- **Primer instalador de la suite completa**: `FAControl_Setup_1.5.0.exe` (Inno Setup 6, español, self-contained win-x64 — no requiere .NET en la PC del cliente). Incluye todas las migraciones para actualizar una base existente y **excluye** el script de rollback y los seeds de prueba. Al desinstalar se conservan la licencia, los ajustes, los expedientes y la base.
- Build sin warnings; **178 tests verdes** (165 unitarios + 13 de integración).

## [1.4.0] — 2026-07-25 · Comprobante fiscal, préstamos antiguos y DealControl completo

### PrestControl

- **Pagaré**: la tasa de interés ahora aparece en el texto principal ("…con una tasa de interés del 10% mensual…"), y la cláusula de datos crediticios usa el **nombre de la empresa** en lugar de "Púrpura Datos".
- **Papelería con la marca**: el recibo 80mm y el estado de préstamo llevan el **logo FA, el nombre del negocio, el RNC y el teléfono**. Nueva sección **Datos del negocio** en Configuración (nombre, RNC, teléfono, dueño, ciudad, correo) — antes solo se podían cambiar editando el archivo de ajustes.
- **Comprobante fiscal (NCF)**: cada préstamo puede llevar su comprobante. Dos caminos, los dos soportados: **registrar** el e-NCF generado en el Facturador Gratuito de la DGII, o **asignar** el siguiente de una secuencia local configurada (prefijo B02/E32, próxima, fin de rango y vencimiento). La reserva es atómica dentro de la transacción del préstamo, así que un error no consume el número, y la app avisa cuando la secuencia está por agotarse o vencida. El comprobante sale impreso en el recibo.
- **Reportes**: dos KPIs nuevos — **Total prestado** (capital colocado en el período) y **Proyección a ganar** (interés que falta por cobrar de los préstamos activos).
- **Permisos por pantalla de vuelta**: el Admin vuelve a tener los **checkboxes por pantalla**, conviviendo con los roles por modo — el rol elegido precarga los permisos y el Admin ajusta fino. Cada estancia muestra solo SUS permisos; nunca se mezclan entre modos.
- **Préstamos antiguos**: al crear un préstamo con fecha atrasada, el wizard lo **detecta y pregunta** si el cliente está al día o cuántas cuotas ya pagó. Las cuotas saldadas nacen pagadas con **recibos históricos fechados en su vencimiento**, así los reportes las ubican en su mes real. Esto reemplaza el flujo que limitaba el abono al cargar un cliente antiguo.

### DealControl

- **Panel principal propio**: inventario disponible, inversión, ventas del mes con su ganancia, alquileres e ingresos, y últimos movimientos. **Cero datos de PrestControl**.
- **Inventario ampliado**: nueva **matrícula** (certificado DGII, distinta de la placa) y columnas de año, chasis, color y matrícula en el grid.
- **Ficha del vehículo**: datos completos + **quién lo compró** (venta al contado o crédito de AutoControl) + **historial de reparaciones** con costo, todo imprimible en carta y exportable a PDF.
- **Vendedor restringido**: no ve costo total ni ganancia, ni en el inventario ni en la ficha — solo marca, modelo, chasis, año, color, precio y nota de condición. Sí puede vender.
- **Facturación**: ver e imprimir la factura de una venta, con la marca del negocio, los datos del cliente y del vehículo, el total, la forma de pago y las firmas de cliente, vendedor y gerencia.
- **Financiamiento por plazos**: la venta se pacta **al contado, por plazos** (inicial + N pagos sin interés, como financia un dealer) **o como separación/apartado**. La pantalla de plazos muestra **total por pagar, lo pendiente, la cantidad de plazos y lo pagado**, con cobro por plazo (recibo propio RV-000001), historial de abonos y semáforo de atrasos.
- **Documentos**: **carta de compromiso** con el calendario pactado y **recibo de separación** con la cláusula de los días de derecho (15 por default). Una separación reserva el vehículo en vez de darlo por vendido, y la app avisa cuando el plazo está por vencer.
- **Contratos del dealer**: expediente por venta con cliente, **quién vendió**, cantidad de documentos, matrícula del auto y estado de los plazos (pagados / atrasados / pendiente), con "ver detalles" al financiamiento completo.
- **Reportes propios**: ganancia del período, monto vendido, ingresos por alquiler, pendiente de cobro, inventario, y **comisiones por vendedor** (el % lo define el negocio en Configuración).

### Cambios técnicos
- Migraciones `012_ncf.sql` (comprobante fiscal), `013_permisos_por_pantalla.sql`, `014_panel_deal.sql`, `015_vehiculo_ficha.sql` y `016_venta_plazos.sql` — todas idempotentes, con espejo en `001` y aplicadas a `facontrol_db`.
- `usuario_modo_permiso` guarda el set marcado por modo; `usuario_permiso` sigue siendo la unión efectiva que lee el login (semántica intacta). El permiso `acceso_<modo>` nunca es un checkbox: se quita eligiendo "Sin acceso".
- Reserva atómica (`SELECT … FOR UPDATE`) para el NCF y para los recibos de plazos, siempre dentro de la transacción de la operación que consume el número.
- `LogoFa` centraliza el monograma vectorial que comparten pagaré, recibo, ficha y factura.
- **FIX**: editar inventario exigía el permiso viejo `vehiculos_editar` y vender también — el Encargado no podía editar y el Vendedor no podía vender. Ahora usan `inventario_editar` y `ventas`.
- Los tests de integración que tocan `SesionActual` (estático global) corren en una colección serializada: el paralelismo de xUnit hacía que una clase le cerrara la sesión a la otra.
- Build Release sin warnings; **146 tests verdes** (133 unitarios + 13 de integración).

## [1.3.5] — 2026-07-19 · Roles por modo (permisos diferenciados por estancia)

### Added
- **Roles por modo**: al dar acceso a un empleado, el Admin ya no marca permisos sueltos — elige **un rol por cada estancia** (PrestControl / DealControl / AutoControl) o lo hace **Administrador** (acceso total a los tres). Cada rol trae sus propios permisos. "Sin acceso" en un modo = ese empleado no entra ahí.
- **Roles propios de Dealer/Auto**, distintos a PrestControl y con **permisos con nombres propios**: `inventario`, `inventario_editar`, `ventas`, `alquileres`, `gastos`. Roles nuevos **Encargado** (gestión completa: puede editar inventario y registrar gastos) y **Vendedor** (opera ventas/alquileres/inventario, sin editar inventario ni gastos) para `dealercontrol` y `autocontrol`.
- **Equivalencia de nivel entre modos**: si Jessi es Cobradora en PrestControl y se le da acceso a DealControl como Vendedora, entra a Dealer con los permisos de Vendedora — el rol y su nivel se respetan por estancia, no se arrastra el de PrestControl.
- El **rol mostrado en la barra** ahora es el del modo en el que se entró (Cobrador en Prest, Encargado en Dealer, etc.), no un rol global.

### Cambios técnicos
- Tabla `usuario_modo_rol (usuario_id, modo, rol_id)` guarda la elección por estancia; `usuario_permiso` sigue siendo la **unión efectiva** (el login no cambia de semántica). `GuardarRolesPorModoAsync` recalcula la unión de forma atómica en la misma transacción.
- `rol.modo` etiqueta cada rol a su estancia; clave única `(nombre, modo)` para permitir "Encargado" en Dealer y en Auto sin chocar. Admin = `usuario.rol_id` = rol Admin global (todos los permisos).
- Migración `011_roles_por_modo.sql` (idempotente) + espejo en `001`: columna `modo`, roles y permisos nuevos, `rol_permiso` de cada rol, y migración de usuarios existentes a `usuario_modo_rol` recalculando su unión.
- Build Release sin warnings; **123 tests verdes**. Migración 011 aplicada a `facontrol_db` y verificada; smoke por SQL + harness del camino de escritura OK (unión correcta, Admin = 22/22 permisos, rol por modo bien mostrado).

## [1.3.4] — 2026-07-18 · Pulido final de grids

### Fixed
- **Préstamos**: columna "Cliente" con ancho fijo → el grid se DESPLAZA horizontalmente (y vertical) mostrando todos los datos completos en vez de comprimir/cortar.
- **Detalle de préstamo**: los botones de acción se movieron a una fila DEBAJO del "← Préstamos" + código/cliente/estado (en todos los tamaños).
- **Reportes**: área de gráfico + desglose con **scroll vertical** (bajar a ver "Ganancia por semana"); "Desglose por semana" con columna fija → **scroll horizontal**.

## [1.3.3] — 2026-07-18 · Ajustes de UX, recordatorios manuales y chrome

### Fixed
- **Scroll global solo en Grande**: en Pequeño/Mediano vuelve el comportamiento original (sin scroll, se sentía como zoom); el scroll solo aparece en Grande, que es cuando el contenido excede la pantalla.
- **Cobros**: dígitos de "Deuda pendiente", "Próxima cuota" y "Liquidar hoy por" reducidos (eran algo grandes en Pequeño).

### Added
- **Guardar PDF** en la vista previa de la intimación de pago y del estado de préstamo (y reportes), con zoom.
- **Recordatorios manuales**: botón "Enviar recordatorios" en Préstamos (masivo a todos los clientes con cuota por vencer/vencida de la estancia) y "Enviar recordatorio" en el detalle del préstamo (individual, al correo de ese cliente).
- **Historial → Ver ficha**: botón por fila que abre el detalle con la DESCRIPCIÓN completa (en el grid nunca entra entera) y los demás campos.
- **Sin botones de Windows** (minimizar/maximizar/cerrar) en todas las ventanas MENOS el login: evita que el cliente cierre con la X por error. Se usa "Cerrar sesión" del sidebar.

## [1.3.2] — 2026-07-18 · Responsive (texto Grande), formato de campos y correo

### Fixed
- **Responsive (texto Grande)**: el shell ahora envuelve el contenido en un ScrollViewer, así el contenido magnificado se puede DESPLAZAR en vez de recortarse en pantallas chicas (laptops 1366). Ajustes de ancho en columnas (Modalidad, Próx. venc.) y `MinWidth` en columnas de dinero (Cobros). Reportes: `MinHeight` en el gráfico/desglose para que no colapse a solo títulos.
- **Ficha de cliente**: columnas Tasa y Cuotas centradas; "Próx. venc." más ancha.

### Added
- **Cédula y teléfono con auto-formato** en Nuevo/Editar cliente: los guiones se insertan solos al escribir (cédula 000-0000000-0, teléfono 000-000-0000). La cédula respeta pasaportes (si hay letras, no se toca).
- **Correo**: indicador "✓ Contraseña guardada" (la PasswordBox se ve vacía al volver pero la clave sigue guardada); guía con los pasos exactos para generar el App Password (Nombre → Crear → 16 letras). Diagnóstico confirmado: la app funciona; solo faltaba generar el App Password (2FA requerido; se confirmó con las 3 cuentas de prueba).

### Datos de prueba
- `seed_alertas_prueba.sql`: correos personales reemplazados por `@example.com`.

## [1.3.1] — 2026-07-18 · Correcciones de UX y pagaré

### Fixed
- **Sidebar**: "Préstamos"/"Nuevo préstamo" ya no necesitan doble click para marcarse (GroupName único por ítem: el gemelo colapsado de AutoControl ya no roba el check).
- **Grid de préstamos**: columnas "Tasa" y "Cuotas" centradas (antes el dato iba a la izquierda con el título centrado).
- **Correo**: se quitan los espacios del app password al pegarlo; mensaje de error accionable cuando Gmail rechaza credenciales (explica que hace falta App Password de 16 caracteres + 2FA); enlace directo a generar el App Password.

### Changed
- **Reportes**: un solo botón "Imprimir" gobernado por los combos (cliente → su reporte; solo usuario → sus cobros generales; sin filtro → global). Botones de acción alineados con los combos/datepickers.
- **Pagaré**: zoom funcional (−/+ 50–300%), botón **Guardar PDF** (multipágina en hoja carta), y **encabezado de marca con el logo FA vectorial** (badge navy + monograma, no imagen pegada) con regla dorada.
- **Clientes**: nota bajo el correo — opcional, pero sin él no se envían recordatorios (el correo del cliente se pone acá, no en Configuración).

### Datos de prueba (Dev)
- `scripts/db/seed_alertas_prueba.sql`: 3 clientes con cuotas por vencer, vencidas y en mora para probar el semáforo, las alertas del panel y los recordatorios (idempotente).

## [1.3.0] — 2026-07-18 · Aislamiento por estancia + acceso por modo

### Apariencia (paletas por modo)
- **El ítem seleccionado del sidebar toma el color del modo activo**: dorado en PrestControl, verde en AutoControl, azul en DealControl (antes era índigo en los tres). Brushes dedicados `Brush.SidebarSel.*` que `MostrarModo` sobreescribe en caliente en `Window.Resources`; no toca botones ni el resto del acento.
- **Modo noche por MODO**: cada estancia recuerda su propio tema y **DealControl arranca en modo noche** por defecto; el resto en claro. Editable en Configuración (persiste por modo, por PC). `AjustesLocales.TemaOscuroDe/FijarTemaOscuro`; el tema se aplica al elegir el modo en el launcher.
- **DealerControl renombrado a "DealControl"** (nombre visible en launcher, shell y título). El identificador de código (`ModoApp.DealerControl`) y los valores de BD (`ambito='dealercontrol'`, permiso `acceso_dealercontrol`) se mantienen para no romper datos.

### Added
- **Acceso por modo (permisos)**: tres permisos nuevos `acceso_prestcontrol` / `acceso_dealercontrol` / `acceso_autocontrol`. El Admin decide desde la pantalla de Usuarios a qué estancias entra cada empleado; el Admin siempre entra a las tres. La **puerta se aplica en el login**: aunque la contraseña sea correcta, si el usuario no tiene el acceso del modo elegido no se abre sesión y ve un mensaje claro (no cuenta como intento fallido). `SesionActual.PuedeAccederModo` / `SesionActual.Modo`.
- **Clientes en los tres modos**, cada estancia con los suyos: Dealer y Auto ahora gestionan sus propios clientes sin depender de PrestControl.

### Cambios técnicos (aislamiento de datos — decisión Yuber 2026-07-18)
- **3 dominios aislados de clientes** (`cliente.ambito` ENUM prestcontrol/dealercontrol/autocontrol). Toda lectura de clientes se scopea al modo activo (`SesionActual.Modo`): un cliente de PrestControl ya NO aparece al vender/alquilar/financiar vehículos, ni viceversa. Cédula **única por ámbito** (`UNIQUE(ambito, cedula)`), no global: la misma persona puede tener ficha independiente en dos estancias.
- **Aislamiento PrestControl ↔ AutoControl** (ambos usan `prestamo`): Cobros, Almacén de contratos, Panel y Reportes se filtran por `vehiculo_id` según el modo (`SesionActual.SoloVehicularesDelModo`) — los créditos vehiculares no se mezclan con los préstamos personales en ninguna lista, KPI ni reporte.
- Migración `010_ambitos.sql` (idempotente) + espejo en `001`: columna `ambito`, índice único compuesto, permisos de acceso, defaults por rol (Admin/Supervisor = 3 modos, Cobrador = solo PrestControl) y backfill de usuarios/clientes existentes a PrestControl.
- Build Release sin warnings; **123 tests verdes** (114 unitarios + 9 integración). Migración 010 aplicada a `facontrol_db` y verificada; smoke de aislamiento OK (misma cédula en dos ámbitos permitida, duplicada en el mismo ámbito rechazada). Corregida aserción obsoleta del conteo de contadores (2 → 5, desde Tier 5).

## [1.2.0] — 2026-07-17 · Tier 5 — DealerControl y AutoControl (suite de 3 modos)

### Added
- **Suite multimodo**: el shell ahora es *mode-aware*. El launcher abre uno de tres modos y el sidebar/landing se adapta. **DealerControl** y **AutoControl** habilitados.
- **Dominio de vehículo** (`vehiculo`, schema 001 + migración 008): el vehículo como ACTIVO que nace en Dealer; código `V-0001`, soft delete, costo vs precio (costo total y ganancia calculados), estados disponible/reservado/vendido/alquilado/baja. Permisos `vehiculos`/`vehiculos_editar`.
- **DealerControl — Inventario**: alta/edición/baja de vehículos con vista previa de costo total y ganancia.
- **DealerControl — Venta al contado** (`venta_vehiculo`, código `VC-0001`): venta atómica (marca el vehículo `vendido` + auditoría en una transacción), con cliente, precio y método de pago.
- **DealerControl — Rent a car** (`alquiler`, código `AL-0001`): alquiler con cálculo automático de días y total; devolución/cancelación que libera el vehículo. Atómico.
- **DealerControl — Gestión de importación** (`vehiculo_gasto`): ledger de gastos (aduana, flete, etc.) cuya suma se refleja en el costo del vehículo.
- **AutoControl — Crédito vehicular**: un crédito vehicular es un `prestamo` con `vehiculo_id` (el vehículo en garantía), reutilizando amortización, cuotas, cobros, pagaré y reportes. Al financiar, el vehículo pasa a `vendido` en la misma transacción; la garantía se autocompleta con el vehículo. La lista y el wizard de préstamos se filtran/adaptan por modo (picker de vehículo disponible).

### Cambios técnicos
- Migración 009: `prestamo.vehiculo_id`, tablas `venta_vehiculo`/`alquiler`/`vehiculo_gasto`, contadores `venta`/`alquiler`. Reordenada `vehiculo` antes de `prestamo` en 001 por la FK. 001 validado contra BD desechable.
- 13 tests nuevos (114 verdes). Flujos atómicos verificados end-to-end contra `facontrol_db`.

## [1.1.0] — 2026-07-17 · Tier 4 — Reportes individuales, contratos, recordatorios Gmail e intimación de pago

### Added
- **Intimación de pago** imprimible por préstamo (`IntimacionDocumentFactory` + `IntimacionImpresa`): requerimiento formal PREVIO a la vía judicial que emite el acreedor para las cuotas vencidas, con encabezado del negocio, datos del deudor, tabla de cuotas vencidas, saldo, plazo configurable (`AjustesLocales.PlazoIntimacionDias`, default 15 días) y firma. Botón en el detalle del préstamo → vista previa imprimible. Se aclara en `docs/INTIMACION-Y-MANDAMIENTO.md` que NO es el "mandamiento de pago" (acto de alguacil): la app genera la intimación, que es lo que el acreedor sí puede emitir por su cuenta.
- **Recordatorios de cobro por Gmail** (`RecordatorioService` + `EmailService`, SMTP `smtp.gmail.com:587` STARTTLS): correo por cliente con cuotas próximas a vencer + resumen al dueño, envío manual o automático una vez al día. Contraseña de aplicación de Gmail cifrada con DPAPI (`Secreto`, por usuario/PC). Destinatario: cliente + dueño. WhatsApp documentado y diferido (`docs/WHATSAPP.md`).
- **Reporte por cliente** (individual y global) imprimible: cobros, capital recuperado y saldo pendiente por cliente, con filtro por usuario cobrador y por cliente.
- **Almacén de contratos**: lista con vista previa lateral del pagaré (`ContratoService` + `ContratosView`).
- Datos del negocio configurables (nombre, prestamista, ciudad, teléfono, email, RNC) para encabezados de pagaré e intimación; doc explicativo de NCF/DGII (`docs/NCF-DGII.md`).
- Respaldo automático cada N días/meses (a carpeta local o sincronizada a la nube).

### Fixed
- **Filtro por usuario en Reportes mostraba todo en 0**: los pagos previos a la columna `pago.created_by` la tenían en `NULL`. Migración `007_pago_created_by.sql` con backfill desde auditoría por `fecha_pago`. Los pagos nuevos ya guardan quién cobró.
- **Responsive**: los textos y dígitos ya no se recortan al cambiar el tamaño de letra en Configuración (columnas de fecha ensanchadas a 185px, tarjetas de métricas envueltas en `Viewbox`, `MinWidth` en columnas estrella).

## [1.0.1] — 2026-07-11 · Arranque robusto sin base de datos + auto-aprovisionamiento

### Fixed
- **La app crasheaba al instante en una PC con MySQL pero sin la base de datos creada** (excepción sin capturar en el `Loaded` del login). Ahora el arranque diagnostica la conexión ANTES de mostrar ventanas y responde con mensajes claros: servicio MySQL detenido, credenciales rechazadas o base de datos inexistente.

### Added
- **Auto-aprovisionamiento del primer arranque**: si el servidor responde pero la BD no existe, la app ofrece crearla con un clic (schema completo embebido en el ensamblado — misma fuente que `scripts/db/001_create_schema.sql`). Verificado end-to-end: crear BD desde cero → wizard de cuenta → sesión operativa. Si el usuario MySQL configurado no tiene permiso CREATE (el dedicado `facontrol`, por diseño), el mensaje dirige al INSTALL.md.
- `VerificadorBaseDatos` (Data) con diagnóstico `Lista/FaltaBaseDatos/CredencialesInvalidas/SinServidor` + 5 tests de integración nuevos (76 tests en total).

### Seguridad
- `App.config` versionado ahora lleva placeholders (`CAMBIAR_USUARIO`/`CAMBIAR_PASSWORD`): las credenciales locales de Dev ya no viven en el repositorio público (protegidas además con `git update-index --skip-worktree`).

## [1.0.0] — 2026-07-10 · Fase 7 (Empaquetado y entrega) — TODAS LAS FASES COMPLETAS

### Added
- **Instalador** `FAControl_Setup_1.0.0.exe` (Inno Setup 6, español, 60 MB): app publicada **self-contained win-x64** (el cliente NO necesita instalar .NET), acceso directo, scripts de BD y documentación incluidos. El App.config no se pisa en actualizaciones; permisos de escritura para logs/ajustes.
- **`scripts/db/003_crear_usuario_dedicado.sql`**: usuario MySQL `facontrol` con permisos mínimos (sin DELETE/DROP — la app usa soft deletes) para no correr como root donde el cliente.
- **`docs/INSTALL.md`**: guía de instalación técnica paso a paso (MySQL, BD, usuario dedicado, config, checklist post-instalación, migración de PC, problemas comunes).
- **`docs/MANUAL.md`**: manual del usuario final pantalla por pantalla, en lenguaje no técnico (pedido de Yuber), con la rutina de respaldo destacada y preguntas frecuentes.
- **Ícono de la aplicación** (`Assets/facontrol.ico`): cuadrado redondeado indigo con la "P" del logo, 7 tamaños (16–256px). Se ve en el Explorador, la barra de tareas, las ventanas y el instalador — adiós al ícono de "app desconocida".

## [0.5.0] — 2026-07-10 · Fase 6 (Reportes, Historial, Configuración) + Notificador de vencimientos

### Added
- **Reporte "Ingresos por período"** (fiel al mockup): rango de fechas con atajos (Este mes / Mes pasado / Trimestre / Año), KPIs — Ganancia (interés cobrado, card indigo), Capital recuperado, Total cobrado, Cuotas cobradas "X de Y programadas" —, gráfico de barras apiladas por semana (interés+capital) y desglose semanal con fila de totales. Botón Exportar Excel.
- **Historial**: visor de solo lectura de la auditoría con filtros por fecha, entidad y acción (límite 300, aviso para afinar filtros).
- **Configuración**:
  - Cambio de contraseña (actual + nueva + confirmación, errores inline).
  - **Tamaño de texto Pequeño/Mediano/Grande** (pedido de Yuber): escala toda la UI (1.0/1.12/1.25) al instante y persiste en `ajustes.json`.
  - **Respaldo/restauración** de la BD con mysqldump/mysql (búsqueda automática del binario, contraseña por MYSQL_PWD, doble confirmación al restaurar).
  - **Exportación a Excel** (ClosedXML 0.105, MIT): libro .xlsx con hojas Clientes/Préstamos/Cuotas/Pagos/Auditoría; manual y **automática** al abrir la app (cada N días configurable, carpeta elegible, activable).
- **Notificador de vencimientos** (pedido del cliente, estilo POS-400): al iniciar sesión —y al cambiar el día de negocio con la app abierta— avisa qué clientes se pasaron de su fecha de pago (lista en rojo con cuotas, monto y fecha). Botón OK + checkbox "No volver a preguntar por este cliente" (persistente, individual). Activable y con "restablecer silenciados" en Configuración.

### Decisiones
- El mockup de Reportes define UN reporte (Ingresos por período); se implementó ese. Los "6 tipos" del plan original quedan abiertos a definirse con el cliente (BLOCKERS.md).
- La migración de PC recomendada es Respaldar/Restaurar (.sql, conserva ids y relaciones); el Excel es de consulta. Importar desde Excel se descartó por riesgo de integridad (BLOCKERS.md).

## [0.4.0] — 2026-07-10 · Fase 5 (Dashboard) + ajustes finos de UI

### Added
- **Panel de control** (pantalla de inicio real): 4 KPIs — Capital colocado (saldo por cobrar de activos), Cobros del mes con **delta vs mes anterior** (↑/↓ %), Clientes activos, Morosidad en RD$ y % del capital.
- **Panel de alertas de cobro** (60%): cuotas vencidas, en mora o que vencen en ≤ 7 días, con pill de semáforo y botón **Cobrar** que navega directo a Cobros con el préstamo preseleccionado.
- **Gráfico de cobros diarios** del mes en curso (LiveChartsCore 2.0.5, barras indigo redondeadas, días sin cobros en 0).
- **Últimos movimientos**: los 10 pagos más recientes.
- `DashboardRepository`/`DashboardService`: agregados en una sola pasada; límites de mes calculados en hora de negocio RD (UTC-4) y convertidos a UTC.

### Changed (ajustes finos pedidos por Yuber)
- Encabezados "Estado", "Nombre" (Clientes) y "Cliente" (Cobros) alineados a la izquierda; el resto sigue centrado.
- Columnas de acciones más anchas: "Ver detalle" y "Ver ficha" ya no se cortan.
- ComboBox: texto centrado verticalmente en toda la app.
- Formularios de Nuevo préstamo y Registrar pago: aire de 12px entre los inputs y el scrollbar.
- App y Views ahora apuntan a `net8.0-windows10.0.19041.0` (asset moderno de SkiaSharp, sin warnings NU1701).

## [0.3.1] — 2026-07-10 · Pulido de UI (10 observaciones de Yuber)

### Added
- **Totales del grid de Préstamos**: 4 cards (capital prestado, por cobrar en activos, total cobrado, préstamos activos) que se recalculan con cada búsqueda/filtro.
- **Filtros rápidos**: por estado en Préstamos (Todos/Activos/Pagados/Cancelados) y por situación en Clientes (con/sin préstamos activos, con saldo pendiente).
- **Iconos en el sidebar** con glifos nativos Segoe MDL2 Assets (sin dependencias).

### Changed
- El **sidebar refleja la navegación interna**: crear un préstamo te lleva al detalle y ahora marca "Préstamos" (antes quedaba en "Nuevo préstamo").
- **ScrollBars delgados (6px)** en toda la app, con thumb redondeado y hover — discretos pero funcionales.
- **Encabezados de tabla centrados** y **scroll horizontal** en las tablas cuando las columnas no caben (el usuario también puede redimensionar columnas).
- La ventana principal abre **maximizada** por defecto.
- Botón de cerrar sesión ahora es **circular** con hover indigo.

### Pendiente registrado
- Documentación final detallada y fácil de entender → Fase 7. Filtros del Historial → Fase 6.

## [0.3.0] — 2026-07-10 · Fase 2 (Clientes) + ajustes de UI

### Added
- **Módulo Clientes completo**: lista con búsqueda (nombre/cédula/teléfono) y agregados por SQL, ficha con 5 métricas (total prestado, cobrado, saldo, préstamos activos, cuotas vencidas) + datos de contacto + sus préstamos, formulario nuevo/editar con validación inline.
- **`ClienteService`**: normalización de cédula dominicana (11 dígitos → `000-0000000-0`; pasaportes se aceptan tal cual), unicidad de cédula amigable, soft delete **bloqueado si hay préstamos activos**, auditoría de crear/modificar/eliminar.
- Flujo ficha → "+ Nuevo préstamo" con el cliente preseleccionado en el wizard.
- Tests: 8 unitarios de normalización de cédula + 2 de integración (CRUD protegido y métricas).

### Fixed
- **LoginWindow**: `SizeToContent="Height"` — en el wizard de primer arranque el botón "Crear cuenta" quedaba cortado por la altura fija (reporte de Yuber).

### Notas
- Pedidos nuevos registrados para Fase 6 (Configuración): tamaño de texto (Pequeño/Mediano/Grande) y exportar/importar datos a Excel con export automático programable. Ver TODO.md.

## [0.2.0] — 2026-07-10 · Fase 3 completa (Préstamos) + Fase 4 (Cobros)

### Added
- **Persistencia de préstamos**: `PrestamoRepository`, `ClienteRepository` (lectura), `PagoRepository`, `ContadorRepository` (correlativos atómicos con `SELECT ... FOR UPDATE`).
- **`PrestamoService`**: creación atómica (contador → prestamo → N cuotas → auditoría en UNA transacción, código P-0001) y cancelación (cuotas impagas → `'cancelada'`, jamás se borran).
- **`PagoService`**: los 4 escenarios de cobro — pago exacto, abono parcial (primero interés, luego capital), adelanto en cascada y liquidación anticipada (cuotas futuras pagan solo capital; el interés futuro se exonera). Todo cobro es una transacción con cuotas bloqueadas (`FOR UPDATE`) y `numero_recibo` R-000001 atómico.
- **UI Préstamos**: lista con búsqueda y agregados en SQL, detalle con métricas + tabla de cuotas con semáforo (indicador rojo lateral en vencidas), wizard "Nuevo préstamo" con vista previa de amortización EN VIVO.
- **UI Cobros**: selector de préstamo activo, cuotas pendientes, atajos (cuota completa / liquidar), preview de cómo se distribuye el monto antes de confirmar, historial de pagos recientes.
- **Recibo 80mm** (patrón imagen del POS-400): el mismo visual se muestra, se imprime (`PrintVisual`) y se exporta a PDF (PdfSharp 6.1.1, rasterizado a 192 DPI).
- **Navegación por páginas**: ContentControl + DataTemplates (ViewModel → View); flujos lista → detalle → cobros cableados por eventos.
- **`IDialogService`** inyectable (confirmaciones/errores testeables; MessageBox solo en la capa App).
- Estilos DESIGN.md: tablas (header uppercase, filas 44px, hover, selección indigo), pills de semáforo y de estado de préstamo, botones Destructivo/Terciario.
- **Tests**: 17 nuevos de distribución de pagos (57 unitarios en total) + 2 de integración contra `facontrol_test` (BD real recreada por corrida: flujo completo crear → abonar → adelantar → liquidar, y cancelación).

### Decisiones
- **Liquidación anticipada** exonera el interés de cuotas futuras (solo se cobra su capital pendiente). Corregible con el cliente — ver BLOCKERS.md.
- **Recibos multi-cuota**: `pago.numero_recibo` es UNIQUE por fila; un cobro que afecta varias cuotas genera un recibo por cuota y el impreso agrupa la operación bajo el primer número.
- PdfSharp 6.1.1 para el PDF del recibo (el mismo visual WPF rasterizado — papel y archivo idénticos).

## [0.1.0] — 2026-07-10 · Fase 1 (Cimientos) + Fase 3 núcleo (Amortización)

### Added
- Solución .NET 8 con arquitectura por capas: App / Views / ViewModels / Models / Services / Data / Common / Printing + 2 proyectos de tests (xUnit + FluentAssertions 7).
- Esquema MySQL `facontrol_db` (8 tablas: usuario, sesion, cliente, prestamo, cuota, pago, auditoria, contador) con scripts de creación, seed Dev y rollback.
- `AmortizacionService`: interés simple dominicano (default) y sistema francés, con redondeo AwayFromZero y ajuste en última cuota. 100% decimal, sin double.
- `CuotaEstadoCalculator`: semáforo de cobros (al día / por vencer / vencida / en mora / pagada / cancelada) con 100% de ramas testeadas.
- 40 tests unitarios verdes (incluye validación exacta contra el mockup: 75,000 al 5% × 12 → cuota 8,461.91).
- Autenticación mono-usuario: wizard de primer arranque, login BCrypt cost 12, rate-limiting (5 intentos → bloqueo 5 min), registro de sesiones, cambio de contraseña.
- `AuditoriaService` con variante transaccional para operaciones multi-paso.
- Shell WPF: LoginWindow + MainWindow con sidebar de 240px (8 secciones) según DESIGN.md (paleta indigo, tipografía, estilos de botones/inputs/cards, converters de moneda RD$ y fecha).
- Serilog a archivo rotativo diario en `logs/`.
- Documentación: `docs/AMORTIZATION.md`, `PTV300-PATTERNS.md`, `TODO.md`, `BLOCKERS.md`.

### Decisiones
- Tasa de interés se ingresa MENSUAL y se convierte por período (÷2 quincenal, ÷4 semanal, ÷30 diaria) — ver AMORTIZATION.md §1.
- ENUM `cuota.estado` amplía la spec con `'cancelada'` (exigido por la regla de cancelación de préstamos).
- FluentAssertions fijado en 7.2.0 (la v8 requiere licencia comercial de pago).
