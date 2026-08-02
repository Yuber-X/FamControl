# Guía de instalación — FAControl

**Familia Almonte Auto Import SRL**
Versión del programa: 1.9.1 · Fecha de esta guía: 2 de agosto de 2026

> Esta guía es para **quien instala el programa** en la computadora del
> negocio. Está escrita paso por paso: si seguís el orden, no te podés perder.
>
> **Tiempo:** entre 25 y 40 minutos. No se descarga nada: todo viene adentro.

---

> ### 📷 Cómo usar los espacios para imágenes
>
> Igual que en el manual: cada bloque `📷 IMAGEN NN` dice qué pantalla
> fotografiar. Guardá la captura con ese número y pegala justo debajo.
>
> **Total de imágenes en esta guía: 14** (numeradas de la I-01 a la I-14 para
> no confundirlas con las del manual).

---

## Índice

1. [Antes de empezar](#1-antes-de-empezar)
2. [Qué trae el instalador](#2-qué-trae-el-instalador)
3. [Paso a paso de la instalación](#3-paso-a-paso-de-la-instalación)
4. [Configurar MySQL](#4-configurar-mysql)
5. [Configurar AnyDesk](#5-configurar-anydesk)
6. [Configurar Google Drive](#6-configurar-google-drive)
7. [Primer arranque de FAControl](#7-primer-arranque-de-facontrol)
8. [Cargar la cartera de préstamos](#8-cargar-la-cartera-de-préstamos)
9. [Dejar el respaldo automático andando](#9-dejar-el-respaldo-automático-andando)
10. [Actualizar a una versión nueva](#10-actualizar-a-una-versión-nueva)
11. [Desinstalar](#11-desinstalar)
12. [Lista de verificación final](#12-lista-de-verificación-final)

---

## 1. Antes de empezar

### Lo que necesita la computadora

| Cosa | Mínimo |
|---|---|
| Windows | 10 (versión 2004) u 11, de **64 bits** |
| Espacio libre en disco | 5 GB |
| Memoria (RAM) | 4 GB (8 GB va mucho mejor) |
| Internet | Solo para configurar Google Drive. MySQL ya viene adentro |

> ⚠️ **Windows de 32 bits no sirve.** Para saber cuál tenés: clic derecho en
> **Este equipo** → **Propiedades** → mirá donde dice *Tipo de sistema*.

### Lo que tenés que tener a mano

- [ ] El archivo **`FAControl_Setup_1.9.1.exe`**
- [ ] Los **códigos de activación** de las oficinas que compró el cliente
- [ ] Una **contraseña para MySQL** ya pensada y anotada
- [ ] La **cuenta de Google** del negocio (para Google Drive)

> 🔴 **La contraseña de MySQL es la más importante de toda la instalación.**
> Anotala en papel y dejásela al dueño. Si se pierde, el programa deja de
> poder abrir su propia base de datos.

---

## 2. Qué trae el instalador

El instalador es **uno solo** y adentro viene todo. No hay que descargar nada
por separado:

| Programa | Para qué sirve | ¿Obligatorio? |
|---|---|---|
| **FAControl** | El programa en sí | ✅ Sí |
| **MySQL Server** | Donde se guarda toda la información | ✅ Sí |
| **AnyDesk** | Para dar soporte a distancia | ⚪ Recomendado |
| **Google Drive** | Para que los respaldos suban solos a internet | ⚪ Recomendado |

> 💡 **No hace falta instalar .NET.** El programa lo trae adentro.

---

## 3. Paso a paso de la instalación

### 3.1. Ejecutar el instalador

Clic derecho sobre `FAControl_Setup_1.9.1.exe` → **Ejecutar como
administrador**.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-01 — Menú de clic derecho con "Ejecutar como      │
│     administrador" señalado                                    │
└────────────────────────────────────────────────────────────────┘
```

> ⚠️ Si Windows muestra una pantalla azul que dice **"Windows protegió tu
> PC"**, hacé clic en **Más información** y después en **Ejecutar de todas
> formas**. Sale porque el instalador no tiene firma digital comprada, no
> porque tenga algo malo.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-02 — Aviso "Windows protegió tu PC" con el enlace │
│     "Más información" señalado                                 │
└────────────────────────────────────────────────────────────────┘
```

### 3.2. Elegir la carpeta

Dejá la que propone (`C:\Program Files\FAControl`). **Siguiente.**

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-03 — Pantalla de carpeta de destino               │
└────────────────────────────────────────────────────────────────┘
```

### 3.3. Elegir qué instalar ⭐

**Esta es la pantalla más importante.** Acá se marca qué programas se instalan
además de FAControl:

- ☑️ **Instalar MySQL Server** — dejala marcada **siempre**, salvo que la
  computadora ya tenga MySQL andando
- ☑️ **Instalar AnyDesk** — marcala si querés poder dar soporte a distancia
- ☑️ **Instalar Google Drive** — marcala si el cliente va a usar respaldo en
  la nube
- ☑️ **Crear acceso directo en el escritorio** — marcala: el dueño la va a
  buscar ahí

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-04 — Pantalla con las casillas de los programas   │
│     a instalar, todas marcadas                                 │
└────────────────────────────────────────────────────────────────┘
```

> ⚠️ **Si la computadora ya tiene MySQL instalado, desmarcá esa casilla.**
> Instalarlo dos veces genera un lío que después hay que desarmar a mano.

### 3.4. Instalar

**Instalar** y a esperar. El instalador copia FAControl y después va abriendo
**uno por uno** los instaladores de los programas que marcaste.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-05 — Barra de progreso de la instalación          │
└────────────────────────────────────────────────────────────────┘
```

> 💡 **No cierres ninguna ventana que aparezca.** Cada una es un programa
> instalándose. Seguí las instrucciones de las secciones 4, 5 y 6 según
> vayan apareciendo.

---

## 4. Configurar MySQL

Es el paso más largo. **Prestá atención acá**, el resto es fácil.

### 4.1. Tipo de instalación

Aparece la pantalla **Choosing a Setup Type** con varias opciones.

Elegí **Server only** (solo el servidor). **Next.**

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-06 — Pantalla de MySQL con "Server only"          │
│     seleccionado                                               │
└────────────────────────────────────────────────────────────────┘
```

> 💡 **Por qué "Server only" y no las otras.** El paquete trae también MySQL
> Workbench, que es un programa para editar la base de datos a mano. En la
> computadora del negocio **no lo queremos**: desde ahí se pueden borrar
> préstamos y pagos sin que FAControl lo registre en el historial, que es
> justamente lo que el programa evita. Si el técnico lo necesita para
> diagnosticar algo, puede agregarlo después con la opción **Custom** del
> mismo instalador.

### 4.2. Instalación

**Execute** y esperá. **No descarga nada de internet**: los archivos ya vienen
adentro, así que tarda uno o dos minutos.

> ⚠️ Si en vez de esto ves una pantalla que dice **Download** o un título que
> dice **"Adding Community"**, es que **esta computadora ya tenía MySQL
> instalado de antes**. Pará y leé el recuadro rojo del final de la sección 4.4
> antes de seguir.

### 4.3. Configuración del servidor

Van varias pantallas seguidas. En todas **dejá lo que viene puesto** salvo
donde diga lo contrario:

| Pantalla | Qué elegir |
|---|---|
| **Type and Networking** | Config Type: **Development Computer** · Puerto: **3306** |
| **Authentication Method** | **Use Strong Password Encryption** (la primera opción) |
| **Accounts and Roles** | 🔴 **Acá va la contraseña de root** — ver abajo |
| **Windows Service** | Nombre del servicio: **MySQL80** · ☑️ Start at System Startup |
| **Apply Configuration** | **Execute** y esperar a que todo quede en verde |

> 🔴 **No te saltees el ☑️ Start at System Startup.** Sin esa casilla, MySQL
> queda en arranque *Manual*: la primera vez todo funciona, pero **al primer
> reinicio de la computadora FAControl abre con "No se pudo conectar con
> MySQL"** y el dueño va a pensar que el programa se rompió. Es el error más
> fácil de cometer y el que más tarda en aparecer, porque para entonces el
> técnico ya se fue.
>
> Para verificarlo después, con una consola **como administrador**:
> ```
> sc qc MySQL80
> ```
> Tiene que decir `START_TYPE : 2 AUTO_START`. Si dice `3 DEMAND_START`, se
> corrige con:
> ```
> sc config MySQL80 start= auto
> ```

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-07 — Pantalla "Type and Networking" con           │
│     Development Computer y puerto 3306                         │
└────────────────────────────────────────────────────────────────┘
```

### 4.4. 🔴 La contraseña de root

En la pantalla **Accounts and Roles** te pide la contraseña del usuario
`root`, que es el dueño de la base de datos.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-08 — Pantalla "Accounts and Roles" con el campo   │
│     de contraseña de root                                      │
└────────────────────────────────────────────────────────────────┘
```

**Escribí la contraseña que anotaste al principio.** Después:

1. **Anotala en papel** y dejásela al dueño
2. **Guardala también** en el teléfono o el correo del dueño

**Elegí una contraseña de solo letras y números**, tipo `FAControl2026`. Los
símbolos traen dos problemas conocidos:

- el punto y coma `;` **parte la cadena de conexión** de FAControl y el
  programa no abre aunque la contraseña sea correcta;
- con el teclado en inglés (que es como suele venir configurado) varios
  símbolos **salen en otra tecla**: escribís uno y se guarda otro.

> 🔴 **Si esta contraseña se pierde, FAControl no puede abrir su base de
> datos.** Anotala **en el momento**, antes de seguir a la pantalla siguiente.

#### 🔴 Si la pantalla "Accounts and Roles" nunca apareció

Significa que **MySQL ya estaba instalado en esa computadora** y el asistente
no vuelve a preguntar la contraseña: se queda con la que se puso la primera
vez, que nadie sabe cuál es. FAControl va a abrir con el error
*"MySQL rechazó el usuario o la contraseña configurados"*.

No reinstales MySQL — reinstalar **no borra la contraseña vieja** y volvés al
mismo punto. Se arregla así:

1. Buscá el archivo **`reset_password_root_mysql.bat`**. Viene junto al
   instalador, y también queda instalado en
   `C:\Program Files\FAControl\scripts\soporte\`
2. Clic derecho → **Ejecutar como administrador**
3. Escribí la contraseña nueva (letras y números)
4. Cuando diga `[OK]`, seguí con el paso 4.6

No borra ninguna base de datos: lo único que cambia es la contraseña de root.

### 4.5. Terminar

**Next** hasta el final y **Finish**. La ventana de MySQL se cierra sola y el
instalador de FAControl sigue con lo que falte.

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-09 — Pantalla final de MySQL, todo en verde       │
└────────────────────────────────────────────────────────────────┘
```

### 4.6. Avisarle la contraseña a FAControl

FAControl viene configurado para conectarse con una contraseña de fábrica.
**Si pusiste otra** (que es lo correcto), hay que decírselo:

1. Andá a `C:\Program Files\FAControl`
2. Abrí el archivo **`FAControl.App.dll.config`** con el Bloc de notas
   *(clic derecho → Abrir con → Bloc de notas)*
3. Buscá la línea que dice `Server=localhost;...;Pwd=...`
4. Cambiá lo que está después de `Pwd=` por tu contraseña
5. Guardá con **Ctrl+S**

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-10 — El archivo .config abierto en el Bloc de     │
│     notas, con la línea de la contraseña señalada              │
└────────────────────────────────────────────────────────────────┘
```

> ⚠️ Si el Bloc de notas no te deja guardar, es que la carpeta pide permisos
> de administrador: cerralo y abrilo con clic derecho → **Ejecutar como
> administrador**, y desde ahí abrí el archivo.

---

## 5. Configurar AnyDesk

Cuando aparezca la ventana de AnyDesk:

1. Instalalo con las opciones que vienen por defecto
2. Cuando abra, **anotá el número de 9 dígitos** que muestra en grande
3. **Pasale ese número al desarrollador**: con eso puede entrar a la
   computadora para dar soporte

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-11 — Ventana de AnyDesk mostrando el número de    │
│     dirección (los 9 dígitos)                                  │
└────────────────────────────────────────────────────────────────┘
```

> 💡 **Nadie puede entrar sin permiso.** Cada vez que alguien intenta
> conectarse, la computadora pregunta primero y hay que aceptar.

---

## 6. Configurar Google Drive

Cuando aparezca la ventana de Google Drive:

1. Instalalo con las opciones por defecto
2. **Iniciá sesión con la cuenta de Google del negocio**
3. Cuando termine, en el explorador de archivos aparece una unidad nueva
   (normalmente **G:**)
4. Adentro creá una carpeta llamada **`Respaldos FAControl`**

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-12 — Explorador de archivos mostrando la unidad   │
│     de Google Drive con la carpeta "Respaldos FAControl"       │
└────────────────────────────────────────────────────────────────┘
```

> ⚠️ **Usá la cuenta del NEGOCIO, no la personal de un empleado.** Si esa
> persona se va, los respaldos se van con ella.

---

## 7. Primer arranque de FAControl

1. Abrí FAControl desde el acceso directo del escritorio
2. Te pregunta si quiere **crear la base de datos** → **Sí**
3. Creá la **cuenta del administrador** (usuario, nombre y contraseña de 8
   caracteres como mínimo)
4. En el launcher, **activá las oficinas** con los códigos

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-13 — Launcher de FAControl con las oficinas ya    │
│     activadas                                                  │
└────────────────────────────────────────────────────────────────┘
```

> 📖 Todo esto está explicado con más detalle en el **manual de usuario**
> (`MANUAL.md`), secciones 3 y 4.

**Probá que todo funciona:** entrá a una oficina, cargá un cliente de prueba
y borralo. Si eso anda, la instalación está bien.

---

## 8. Cargar la cartera de préstamos

> Este paso es **solo para Familia Almonte**. En otra instalación, salteálo.

En el `.rar` viene el archivo **`FAControl_CarteraReal_v2_2026-08-02.sql`**: son
los **10 clientes y 10 préstamos** que el dueño entregó en papel, ya
transcriptos, con sus 113 cuotas calculadas. Cargarlo evita tener que
escribirlos a mano uno por uno.

**Se corre DESPUÉS del paso anterior**, con la cuenta del administrador ya
creada. Abrí **CMD como administrador** (no PowerShell) y escribí:

```
cd "C:\Program Files\MySQL\MySQL Server 8.0\bin"
mysql.exe -u root -p facontrol_db < "C:\ruta\donde\pusiste\FAControl_CarteraReal_v2_2026-08-02.sql"
```

Te pide la contraseña de MySQL (la del paso 4.4). Al terminar muestra una
tabla que tiene que decir exactamente esto:

| clientes | prestamos | cuotas | cuotas_no_pendientes | cobros | capital_colocado |
|---|---|---|---|---|---|
| 10 | 10 | 113 | 0 | 0 | 4,500,000.00 |

> ⚠️ **En PowerShell no funciona.** El símbolo `<` está reservado y da el error
> *"The '<' operator is reserved for future use"*. Usá CMD. Si preferís
> PowerShell, la forma equivalente es:
> ```
> Get-Content "ruta\archivo.sql" | mysql.exe -u root -p facontrol_db
> ```

**Verificá en la app:** entrá a PrestControl → **Clientes**. Tienen que
aparecer los 10. En **Préstamos**, los 10 en verde (al día, sin cobros
todavía).

### 8.1. Registrar los pagos que Wendy ya hizo

La cartera se carga **sin ningún cobro**, salvo un caso que hay que completar a
mano: **Wendy Yocasta** ya abonó 15,000 a capital y su saldo real es **285,000**,
no 300,000.

Entrá a **Cobros**, buscá su préstamo (`P-0007`) y registrá estos tres pagos, en
este orden:

| Fecha | Monto | Qué es |
|---|---|---|
| 23/04/2026 | 7,500 | Interés del mes |
| 25/05/2026 | 15,000 | 7,500 de interés + 7,500 a capital |
| 25/06/2026 | 15,000 | 7,500 de interés + 7,500 a capital |

Al terminar, su saldo tiene que quedar en **285,000**.

> 💡 **Por qué no viene cargado en el archivo.** El préstamo se dejó en 300,000
> a propósito: es el capital que sostiene los 7,500 de interés que ella paga
> (2.5% de 300,000). Si se cargara directo por 285,000, el 2.5% daría 7,125 y
> le cambiaría la cuota. Registrando los pagos, el saldo baja solo y todo queda
> en su lugar.

### 8.2. Completar la cédula de Wendy

Su ficha viene con la cédula en **`PENDIENTE-001`**, porque el listado en papel
no la trae. **No es una cédula real, es un marcador.**

Pedísela al dueño y cargala desde **Clientes → Wendy Yocasta → Editar**. Hay que
hacerlo **antes de imprimirle cualquier pagaré o intimación de pago**: son
documentos legales y ahí va la cédula.

> 💡 El archivo tiene un freno: si la base **ya tiene préstamos**, corta con un
> error y no toca nada. Está pensado para correrse una sola vez, en la
> instalación nueva. Si ves el error
> `Table '...abortado_ya_hay_prestamos...' doesn't exist`, es eso: la cartera
> ya estaba cargada.

---

## 9. Dejar el respaldo automático andando

**No te vayas sin hacer esto.** Es lo que salva al negocio si la computadora
se rompe.

1. Entrá a cualquier oficina
2. **Configuración → Respaldo**
3. Marcá **Respaldo automático**
4. En **Carpeta**, elegí la de Google Drive: `G:\Mi unidad\Respaldos FAControl`
5. Cada cuántos días: **1** (todos los días)
6. Apretá **Respaldar ahora** para probar que funciona

```
┌────────────────────────────────────────────────────────────────┐
│  📷 IMAGEN I-14 — Configuración → Respaldo, con el respaldo    │
│     automático activado y la carpeta de Google Drive elegida   │
└────────────────────────────────────────────────────────────────┘
```

**Verificá** que el archivo aparezca en la carpeta de Google Drive y que
tenga el ícono de "sincronizado" (una nube o una tilde verde).

---

## 10. Actualizar a una versión nueva

Para actualizar FAControl:

1. **Hacé un respaldo primero** (Configuración → Respaldo → Respaldar ahora)
2. Cerrá FAControl
3. Ejecutá el instalador nuevo **como administrador**
4. **Desmarcá** las casillas de MySQL, AnyDesk y Google Drive: ya están
   instalados
5. Instalar

**No se pierde nada:** la base de datos, la configuración, la activación y
los expedientes quedan intactos. El instalador solo reemplaza el programa.

> 💡 Si la versión nueva trae cambios en la base de datos, FAControl los
> aplica solo al abrir. No hay que correr nada a mano.

---

## 11. Desinstalar

Panel de control → Programas → **FAControl** → Desinstalar.

**Qué se borra:** el programa y sus registros de funcionamiento (logs).

**Qué NO se borra** (a propósito):

- La base de datos con toda la información
- La activación de las oficinas
- La configuración
- La carpeta `expedientes` con los documentos del cliente

> 💡 Para borrar **también** la base de datos hay que desinstalar MySQL por
> separado. **Hacé un respaldo antes**: eso no tiene vuelta atrás.

---

## 12. Lista de verificación final

Antes de irte de la casa del cliente, repasá:

- [ ] FAControl abre desde el acceso directo del escritorio
- [ ] La base de datos se creó (no aparece ningún error al abrir)
- [ ] La cuenta del administrador entra bien
- [ ] Las oficinas compradas están activadas y se abren
- [ ] `sc qc MySQL80` dice **AUTO_START** (si no, el primer reinicio rompe todo)
- [ ] La cartera de préstamos quedó cargada: 10 clientes, 10 préstamos *(solo Familia Almonte)*
- [ ] Los 3 pagos de Wendy Yocasta registrados: su saldo dice **285,000** *(paso 8.1)*
- [ ] Pedida la cédula de Wendy Yocasta para reemplazar `PENDIENTE-001` *(paso 8.2)*
- [ ] Se puede crear un cliente de prueba y borrarlo
- [ ] La impresora imprime un recibo de prueba
- [ ] El lector de código de barras funciona *(si hay POS-500)*
- [ ] El respaldo automático está activado y apuntando a Google Drive
- [ ] Se hizo un respaldo manual de prueba y llegó a la nube
- [ ] La **contraseña de MySQL** quedó anotada y el dueño la tiene
- [ ] La **contraseña del administrador** quedó anotada y el dueño la tiene
- [ ] AnyDesk está instalado y el número quedó anotado
- [ ] El dueño tiene el **manual de usuario** impreso o en el escritorio

---

**Soporte:** Yuber Santana — **849-438-0242**

*FAControl · Familia Almonte Auto Import SRL*
