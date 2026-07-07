# BARCADE — Game Design Document Técnico

*Party-arcade de bar · 4 jugadores · 1 palanca \+ 1 botón por jugador* *Versión 2.0 — Especificación técnica de diseño e implementación*

---

## Índice

**Parte I — Diseño**

1. Visión, pilares y restricciones de diseño  
2. El bucle de juego y la máquina de estados de sesión  
3. Contrato de control universal e InputSnapshot  
4. Especificación técnica de las 9 mecánicas núcleo  
5. El juego de tablero: sistemas, economía y fórmulas

**Parte II — Sistemas** 6\. Sistema de puntuación: matemática de varianza y catch-up 7\. Fases especiales: especificación cooperativa y asimétrica 8\. UI/HUD: especificación de legibilidad y feedback 9\. Ritmo, dificultad y estructura temporal de sesión

**Parte III — Ingeniería** 10\. Arquitectura de software y separación núcleo/motor 11\. Modelo de datos: MicrogameDefinition y pools de contenido 12\. Contenido remoto: pipeline de Addressables en dos capas 13\. Determinismo, RNG con semilla y reproducibilidad 14\. Presupuesto de rendimiento (hardware objetivo N100) 15\. Telemetría y métricas de piloto 16\. Estrategia de testing 17\. Roadmap de implementación y criterios de aceptación

**Apéndices** A. Glosario · B. Tabla maestra de parámetros · C. Eventos de telemetría · D. Anexo de implementación

---

# PARTE I — DISEÑO

## 1\. Visión, pilares y restricciones de diseño

### 1.1 Declaración de producto

Barcade es un gabinete arcade de mesa para bares: 4 puestos, cada uno con una palanca de 8 direcciones y un botón de acción. La sesión encadena microjuegos de 3–5 segundos sobre un meta-juego de tablero ligero, en partidas de 15–20 minutos. El objetivo de producto no es la profundidad de juego: es **actuar como rompehielos social** — que un grupo de desconocidos esté compitiendo y gritando en menos de 60 segundos.

### 1.2 Pilares (criterios de decisión de diseño)

Cada decisión de diseño se valida contra estos cinco pilares. Ante un conflicto, el orden de prioridad es el listado:

| \# | Pilar | Criterio operativo |
| :---- | :---- | :---- |
| P1 | **Barrera de activación mínima** | Tiempo de "me acerco" a "estoy jugando" \< 60 s. Cero pantallas de configuración. Cero texto instructivo de más de 1 palabra. |
| P2 | **Socializar sin darse cuenta** | Toda mecánica debe producir al menos un vector de interacción cara a cara: griterío, acusación, celebración, negociación o traición. Si una mecánica se juega en silencio, se rediseña o se corta. |
| P3 | **Esperanza matemática** | Varianza alta por ronda, convergencia a habilidad por sesión. Nadie debe poder calcular que ya perdió antes del Game Over (ver §6). |
| P4 | **Legibilidad de bar** | Todo elemento crítico legible a 1.5 m, con luz baja, en \< 300 ms de fijación visual. Codificación por color primario y forma, nunca solo por texto. |
| P5 | **Caos social, no artificial** | El balance emergente lo produce el grupo (arsenal anti-líder, coaliciones). El código provee herramientas, no regala victorias explícitas. |

### 1.3 Restricciones duras (no negociables)

- **Input:** exactamente 1 palanca digital de 8 direcciones \+ 1 botón por jugador. Los encoders USB reportan como gamepad HID estándar (D-pad \+ botón 1). No hay ejes analógicos.  
- **Jugadores:** 4 puestos físicos. El juego debe ser jugable y divertido con 2, 3 o 4 jugadores activos (los puestos vacíos se rellenan con bots de nivel "novato ruidoso" o se excluyen de la ronda, configurable).  
- **Motor:** **Unity 3D** (build Windows standalone, URP). El juego se renderiza en una escena 3D real: modelos de baja poligonización, cámara de perspectiva/ortográfica según fase, iluminación simple. No es un proyecto 2D con sprites: las mecánicas se presentan en 3D ligero (la rama Esquiva-3D del repo — cubo que esquiva, suelo que colapsa, salto — es la referencia visual del estilo). La *simulación* en Barcade.Core sigue siendo lógica pura en espacio normalizado; la capa 3D es presentación (§10.4).  
- **Hardware objetivo:** mini-PC Intel N100 (4 núcleos E-core, sin GPU dedicada), 16 GB RAM, Windows 11, monitor 1080p60. Ver presupuesto de rendimiento en §14.  
- **Sesión:** 15–20 min objetivo, 30 min máximo absoluto (anclado en permanencia real de grupos en bar).  
- **Estética:** 3D low-poly de geometría simple (primitivas y modelos de pocos polígonos, estilo flat-shaded), colores primarios por jugador (Rojo \#E5484D, Azul \#3B82F6, Amarillo \#FACC15, Verde \#22C55E — valores de referencia, ajustar tras prueba de contraste en el monitor real). Assets CC0 (p. ej. Kenney, ya en el repo como referencia) permitidos como base. Sin IP de terceros.  
- **Idioma:** los verbos imperativos y la UI usan español neutro; el diseño debe funcionar sin leer nada (P4).

### 1.4 No-objetivos (alcance excluido del prototipo)

Progresión persistente entre sesiones, cuentas de usuario, ranking online entre bares, la variante bartop de 1–2 jugadores, integración de pagos/premios del local, y modo torneo. Todos son fases posteriores; el prototipo valida únicamente el bucle núcleo.

---

## 2\. El bucle de juego y la máquina de estados de sesión

### 2.1 Diagrama de estados de sesión

La sesión es una máquina de estados finita (FSM) estricta. En código corresponde a `RoundPhaseMachine` (Barcade.Core), que ya existe como lógica pura y se extiende con los estados de tablero.

                    ┌────────────┐

                    │   ATTRACT   │◄────────────────────────┐

                    │ (loop demo) │                          │

                    └─────┬──────┘                           │

              moneda/crédito│                                │

                    ┌─────▼──────┐                           │

                    │    JOIN     │  cada jugador pulsa      │

                    │ (elección   │  botón → reclama color   │

                    │  de color)  │  timeout 30 s            │

                    └─────┬──────┘                           │

             ≥2 jugadores │ listos                           │

                    ┌─────▼──────┐                           │

              ┌────►│ BOARD\_MOVE  │ movimiento simultáneo    │

              │     └─────┬──────┘ (medidor \+ stop, §5.2)    │

              │     ┌─────▼──────┐                           │

              │     │ BOARD\_RESOLVE│ efectos de casilla      │

              │     └─────┬──────┘                           │

              │     ┌─────▼──────┐                           │

              │     │ MG\_INTRO    │ verbo imperativo (0.8 s) │

              │     └─────┬──────┘                           │

              │     ┌─────▼──────┐                           │

              │     │ MG\_PLAY     │ microjuego (3–5 s)       │

              │     └─────┬──────┘                           │

              │     ┌─────▼──────┐                           │

              │     │ MG\_RESULT   │ reparto (1.5 s)          │

              │     └─────┬──────┘                           │

              │     ┌─────▼──────┐                           │

              │     │ INTERMISSION│ respiro rítmico (2 s)    │

              │     └─────┬──────┘                           │

              │  ¿quedan   │                                 │

              └──rondas?───┤ no                              │

                    ┌─────▼──────┐                           │

                    │ FINAL\_WAGER │ apuesta al pozo (§6.2)   │

                    └─────┬──────┘                           │

                    ┌─────▼──────┐                           │

                    │ FINAL\_MG    │ microjuego clímax        │

                    └─────┬──────┘                           │

                    ┌─────▼──────┐                           │

                    │ GAME\_OVER   │ estrellas bonus \+ podio  │

                    └─────┬──────┘  (§6.3)                   │

                          └──────── timeout 20 s ────────────┘

**Invariantes de la FSM:**

- Ninguna transición depende de input bloqueante: todo estado tiene timeout. Un jugador que suelta la palanca y se va al baño jamás congela la partida.  
- `MG_PLAY` es el único estado donde las mecánicas leen input de juego; el resto de estados solo aceptan confirmaciones (tap) y navegación mínima.  
- El estado se serializa por tick (para replay determinista, §13).

### 2.2 Presupuesto temporal por ronda

| Sub-fase | Duración objetivo | Máximo |
| :---- | :---- | :---- |
| BOARD\_MOVE | 8 s | 12 s (timeout fuerza stop del medidor) |
| BOARD\_RESOLVE | 3–6 s (según efectos) | 10 s |
| MG\_INTRO | 0.8 s | fijo |
| MG\_PLAY | 3–5 s (según definición) | 8 s (mecánicas de supervivencia) |
| MG\_RESULT | 1.5 s | fijo |
| INTERMISSION | 2 s | fijo |
| **Total ronda** | **\~20–25 s** | 40 s |

Con 5–8 rondas \+ 1–2 fases especiales \+ final, la sesión aterriza en 12–18 min de juego efectivo. El presupuesto se instrumenta con telemetría (§15) y se ajusta con datos del piloto.

---

## 3\. Contrato de control universal e InputSnapshot

### 3.1 El contrato (capa de diseño)

El jugador aprende cuatro gestos una sola vez; todos los microjuegos los reutilizan sin re-explicación:

| Gesto | Semántica universal | Detección técnica |
| :---- | :---- | :---- |
| Palanca (8-way) | Posición / dirección / apuntar / elegir zona | Estado D-pad por tick |
| Botón **tap** | Acción instantánea (saltar, disparar, confirmar) | Flanco de subida, con ventana ≤ 150 ms hasta flanco de bajada |
| Botón **hold** | Cargar / sujetar / valor continuo | Flanco de subida \+ duración; el valor continuo \= f(t\_hold) |
| Botón **mash** | Fuerza / velocidad acumulada | Frecuencia de flancos en ventana deslizante de 500 ms |

**Regla de exclusión:** un microjuego usa **exactamente un** modo de botón (tap, hold o mash). Nunca dos modos en el mismo microjuego — la ambigüedad es el enemigo de la legibilidad en 5 segundos.

### 3.2 InputSnapshot (capa técnica)

El sistema ya existe en `Barcade.Core` como estructura pura. Especificación:

// Barcade.Core — sin dependencia de UnityEngine

public readonly struct InputSnapshot

{

    public readonly int Tick;                 // tick de simulación (60 Hz fijo)

    public readonly PlayerInput\[\] Players;    // longitud 4, índice \= puesto físico

}

public readonly struct PlayerInput

{

    public readonly Direction8 Stick;   // None, N, NE, E, SE, S, SW, W, NW

    public readonly bool Button;        // estado crudo del botón este tick

    // Derivados calculados por InputInterpreter (no almacenados):

    // ButtonPressedThisTick, ButtonReleasedThisTick,

    // HoldDurationTicks, MashFrequencyHz

}

**Decisiones técnicas:**

- **Muestreo a 60 Hz fijo** en el hilo de simulación, desacoplado del framerate de render. La simulación es determinista tick a tick (§13); el render interpola.  
- **Sin buffering de input entre microjuegos:** al entrar a `MG_INTRO` se descarta el estado de mash/hold acumulado. Evita que el aporreo de un microjuego "se cuele" en el siguiente.  
- **Debounce hardware:** los botones arcade baratos rebotan. Filtro de 8 ms (media jugada de tick) por software; los encoders Zero-Delay ya filtran parcialmente, pero no se confía en ello.  
- **Anti-fantasma de dirección:** con palancas digitales, transiciones N→E pasan por NE durante 1–2 ticks. Las mecánicas que discriminan 4 direcciones (¡IGUALA\!) colapsan las diagonales a la componente dominante más reciente.  
- **Detección de puesto muerto:** si un puesto no genera ningún flanco durante 45 s en estados de juego, se marca `Idle` y se excluye del reparto de la ronda (no elimina al jugador: puede volver pulsando).

### 3.3 Calibración de mash

El mash es sensible al hardware y al jugador. Parámetros:

- Frecuencia mínima registrable: 2 Hz (por debajo, cuenta como taps sueltos).  
- Frecuencia de saturación: 9 Hz (por encima no aporta más — evita ventaja desmedida y protege el botón físico).  
- Curva de respuesta: lineal entre 2 y 9 Hz, `fuerza = clamp01((f - 2) / 7)`.  
- Estos valores son parámetros de `GameTuning` (dato remoto, §11), no constantes de código: se recalibran con telemetría del piloto.

---

## 4\. Especificación técnica de las 9 mecánicas núcleo

Cada mecánica se especifica con: identificador, dinámica, mapeo de input, reglas, condición de victoria y desempate, parámetros expuestos a datos (los que varían entre microjuegos de la misma mecánica), casos borde, y criterios de aceptación (AC) verificables en test.

**Convención de identificadores:** `MECH_XX` para la mecánica (código), `mg_<mecanica>_<variante>` para cada microjuego concreto (dato).

**Distribución de dinámicas del set:** 5 competitivas FFA, 2 asimétricas 1v3, 2 cooperativas. La selección por ronda respeta cuotas (§9.2) para garantizar variedad emocional en cada sesión.

---

### MECH\_01 — ¡MANTÉN\! (Equilibrio)

**Dinámica:** competitiva FFA · **Input:** palanca (posicionar), botón sin uso (o dash de recuperación con cooldown 2 s, variante).

**Simulación.** Cada jugador controla un péndulo invertido 1-DOF:

θ'' \= (g/L)·sin(θ) \+ perturbación(t) − k·input\_palanca

perturbación(t) \= A(t)·ruido\_perlin(seed, t)   con A(t) \= A₀ \+ rampa·t

Fallo cuando |θ| \> θ\_max (default 35°). El jugador aplica torque correctivo con izquierda/derecha; arriba/abajo se ignoran.

**Victoria.** Último en pie. Si el tiempo agota con ≥2 en pie, gana el de menor |θ| medio acumulado (registrado por tick). Empate exacto → reparto del mismo puesto (no hay muerte súbita en microjuegos; el tablero absorbe empates).

**Parámetros (dato):** `gravityFactor`, `perturbAmplitude0`, `perturbRamp`, `torqueGain`, `thetaMax`, `duration` (5–8 s).

**Casos borde:** jugador Idle → su péndulo cae naturalmente (no se congela); entrada diagonal → se usa componente horizontal.

**AC:** (1) con input nulo, todo péndulo cae antes de `duration` en todas las semillas de test; (2) con corrección perfecta simulada, sobrevive el 100 %; (3) la simulación es idéntica dado (seed, inputs) — test de replay.

---

### MECH\_02 — ¡ESQUIVA\! (Dodge)

**Dinámica:** competitiva FFA / supervivencia · **Input:** palanca (mover en plano), botón tap (salto/dash corto, según variante). *Base de código existente; rama 3D con salto ya prototipada.*

**Simulación.** Arena rectangular normalizada \[0,1\]². Peligros con trayectorias **deterministas** (lineales o paramétricas simples) generados por un spawner con semilla. Colisión: círculo jugador (r=0.03) vs. AABB/círculo del peligro. Un golpe \= eliminado de la ronda (sin vidas: la ronda dura segundos).

**Victoria.** Ranking por tiempo de supervivencia (tick de eliminación). Supervivientes al agotar el tiempo comparten el 1er puesto por orden de menor daño rozado (near-miss no penaliza; es solo desempate estético — si no hay dato, comparten puesto).

**Parámetros:** `spawnRatePerSec` (rampa `spawnRate(t) = r₀·(1 + rampCoef·t)`), `hazardSpeed`, `hazardPattern` (enum: Rain, Sides, Cross, Homing\_soft), `jumpEnabled`, `duration`.

**Casos borde:** los 4 eliminados en el mismo tick → puesto compartido; `Homing_soft` limita giro a 30°/s para que siempre exista escape (garantía verificada por test de solver).

**AC:** (1) test de escapabilidad: para toda semilla del set de test, un bot óptimo sobrevive `duration` completo; (2) replay determinista; (3) sin peligros superpuestos en spawn (separación mínima 0.08).

---

### MECH\_03 — ¡CORRE\! (Endless run)

**Dinámica:** competitiva FFA / carrera · **Input:** botón mash (acelerar) \+ palanca arriba tap (saltar). *Esquema único: mash-para-correr, palanca-para-saltar. Nunca al revés.*

**Simulación.** Pista 1D por jugador (4 carriles paralelos en pantalla, sin colisión entre jugadores). Velocidad `v = v_base + v_gain·mashNorm`, con `mashNorm` de la curva de §3.3. Obstáculos deterministas por semilla, **idénticos en los 4 carriles** (equidad total: todos ven la misma pista). Chocar un obstáculo aplica stun de 0.6 s (no elimina).

**Victoria.** Mayor distancia al agotar el tiempo, o primero en cruzar meta (variante `raceToFinish`).

**Rubber-banding (P3):** el último recibe `v_base` \+8 %. Valor bajo a propósito: perceptible en resultado agregado, imperceptible en la sensación ("la ayuda que no se nota, no indigna").

**Parámetros:** `vBase`, `vGain`, `obstacleDensity`, `stunSeconds`, `raceToFinish` (bool), `rubberBandPct`, `duration`.

**AC:** (1) misma semilla → misma pista en los 4 carriles; (2) bot con mash constante 6 Hz y salto perfecto termina sin stun; (3) rubber-band nunca invierte por sí solo un resultado entre dos jugadores con mash idéntico (test estadístico sobre 1 000 semillas).

---

### MECH\_04 — ¡APUNTA\! (Puntería con carga)

**Dinámica:** competitiva FFA · **Input:** palanca (ángulo) \+ botón **hold** (cargar) y release (disparar). Máxima prioridad de prototipo.

**Simulación.** Cada jugador tiene una torreta en su esquina. La palanca fija el ángulo (8 direcciones discretas interpoladas a 0.25 s para sensación analógica). Mantener el botón carga potencia con **medidor oscilante**: `p(t) = 0.5·(1 + sin(ω·t_hold))`, ω tal que el ciclo completo dura 1.2 s. Soltar dispara un proyectil balístico `(ángulo, p)`. Objetivos aparecen en zona central (posiciones por semilla).

**Victoria.** Más impactos en `duration`; desempate por suma de precisión (distancia al centro del blanco).

**Parámetros:** `chargeCycleSec`, `targetCount`, `targetMoving` (bool \+ velocidad), `windAccel` (variante), `projectileSpeedMin/Max`, `duration`.

**Casos borde:** botón aún presionado al agotar tiempo → autodispara con la potencia actual (nadie se queda sin su tiro); dos proyectiles al mismo blanco en el mismo tick → puntúa el de mayor precisión, el otro pasa al siguiente blanco si existe.

**AC:** (1) hold de duración d produce siempre la misma potencia dado el mismo tick de inicio (determinismo); (2) todo blanco es alcanzable desde las 4 esquinas con alguna combinación (test de solver por semilla); (3) autodisparo verificado en timeout.

---

### MECH\_05 — ¡REACCIONA\! (Quick-draw)

**Dinámica:** competitiva / duelo · **Input:** botón tap único. Máxima prioridad de prototipo (mínimo código, máxima tensión).

**Reglas.** Cuenta de espera con duración aleatoria por semilla, `t_señal ∈ [1.5, 4.5] s` (distribución uniforme; nunca patrones aprendibles). A la señal, se registra la latencia de cada jugador (ticks desde señal a flanco). **Salida en falso** (pulsar antes): el jugador queda fuera de esa tanda y se muestra en rojo.

**Anti-spam:** pulsar repetidamente antes de la señal cuenta como una sola salida en falso (no acumula castigo, evita frustración).

**Variantes (dato):** `fakeouts` (0–2 amagos visuales antes de la señal real), `signalMode` (visual/audio/ambos), `colorFilter` (solo reacciona el color mostrado — añade discriminación), `rounds` (mejor de 1 o de 3 tandas dentro del microjuego).

**Victoria.** Menor latencia. Latencias \< 90 ms se consideran anticipación estadística y se tratan como salida en falso (el umbral humano de reacción visual ronda 150–250 ms; 90 ms da margen sin falsos positivos).

**AC:** (1) resolución de latencia \= 1 tick (16.6 ms); (2) dos flancos en el mismo tick → empate real, ambos primer puesto; (3) el umbral de anticipación descarta correctamente inputs pre-señal en test con bot de spam.

---

### MECH\_06 — ¡PERSIGUE\! / ¡ESCAPA\! (1 vs 3\)

**Dinámica:** asimétrica 1v3 · **Input:** palanca (mover) \+ botón tap (dash, cooldown 1.5 s).

**Reglas.** Arena con 2–3 obstáculos (cover). Rol solista rotativo y **forzado por el sistema** (round-robin sobre la sesión; nunca lo elige el juego dos veces seguidas para el mismo puesto). Dos sub-modos por dato:

- `soloHunts`: el solista atrapa (colisión) a los 3; cada atrapado suma para el solista. Ventaja del solista: \+15 % velocidad y dash de mayor alcance.  
- `soloFlees`: los 3 atrapan al solista; el solista gana si sobrevive `duration`. Misma ventaja de velocidad.

**Principio de balance (regla dura de diseño):** ambos bandos comparten el mismo verbo (atrapar/escapar) y el solista compensa el 1-contra-3 con ventaja de movilidad — nunca con un rol de reglas distintas. Objetivo de balance: winrate del solista 40–50 % medido en telemetría; fuera de ese rango, se ajusta `soloSpeedBonus` por dato remoto.

**Parámetros:** `soloSpeedBonus`, `dashCooldown`, `dashDistance`, `arenaLayout` (enum de 4 layouts), `mode`, `duration`.

**AC:** (1) rotación de solista verificada round-robin en test de sesión completa; (2) test de acorralamiento: 3 bots coordinados atrapan a un bot solista óptimo en \< duration en ≥ 40 % de semillas (garantiza que el solista no es imposible de cazar); (3) dash respeta cooldown exacto.

---

### MECH\_07 — ¡BOMBARDEA\! (1 vs 3, poder invertido)

**Dinámica:** asimétrica 1v3 · **Input solista:** palanca (mover mira) \+ botón tap (soltar peligro, cadencia limitada). **Input trío:** palanca (esquivar).

**Reglas.** El solista ve una mira sobre la arena; cada disparo marca la zona 0.7 s antes del impacto (telegraph — los de abajo siempre tienen ventana de reacción). Cadencia del solista: 1 disparo / 0.9 s. El solista gana puntos por impacto; el trío gana sobreviviendo.

**Balance:** radio de impacto y cadencia calibrados para que un trío atento sobreviva \~50 % de las veces. El telegraph es la garantía de justicia: **nunca hay muerte sin aviso visible** (P4).

**Parámetros:** `fireCooldown`, `telegraphSec`, `blastRadius`, `soloScorePerHit`, `duration`.

**AC:** (1) telegraph siempre ≥ 0.5 s renderizado antes de aplicar daño (test de timeline); (2) el solista no puede acumular disparos (cola máx \= 1); (3) winrate en banda 40–60 % con bots calibrados.

---

### MECH\_08 — ¡SUJETA\! / ¡SINCRONIZA\! (Cooperativa)

**Dinámica:** cooperativa 4J · **Input:** botón **hold** (sujetar) \+ palanca (posicionarse en variantes de plataforma).

**Reglas (modo base `holdTogether`).** Aparecen 4 interruptores, uno por color. El equipo debe lograr que los 4 estén sujetos **simultáneamente** durante `holdWindow` (default 1.5 s) dentro de `duration`. Soltar antes reinicia la ventana (no falla la ronda entera: puntuación positiva, ver §7.1). Marcador de progreso circular común, gigante, en el centro.

**Variante `infoAsym`:** solo un jugador (rotativo) ve la cuenta atrás de la ventana; los demás ven sus interruptores pero no el timing → fuerza comunicación verbal ("¡AHORA\!"). Este es el cuello de botella diseñado para producir el grito (P2).

**Puntuación cooperativa.** El equipo gana X monedas todos-o-ninguno según ventanas completadas. En cooperativas **no hay ranking interno**: reparto idéntico. (El ranking interno en coop destruye la cooperación; verificado en literatura de party games y en el pilar P2.)

**Parámetros:** `holdWindow`, `windowsToWin`, `mode` (holdTogether/infoAsym/platformSync), `duration`.

**AC:** (1) la ventana solo valida con los 4 holds solapados a nivel de tick; (2) un puesto Idle convierte el requisito en 3/3 automáticamente (nunca coop imposible por abandono); (3) reparto idéntico verificado.

---

### MECH\_09 — ¡IGUALA\! (Matching de color / Simon)

**Dinámica:** cooperativa (modo principal) o competitiva (variante) · **Input:** palanca (zona de color) \+ botón tap (confirmar).

**Reglas (modo coop `colorRelay`).** La pantalla emite símbolos de color en secuencia; **solo el jugador dueño del color** debe confirmar dentro de `reactWindow` (default 0.9 s, con rampa decreciente). Confirmación de un color ajeno \= fallo leve (−1 al progreso común, nunca eliminación). La secuencia acelera: `reactWindow(n) = max(0.45, 0.9 − 0.05·n)`.

**Variante competitiva `simonSolo`:** todos memorizan una secuencia de 3–5 símbolos y la reproducen con palanca+tap; gana el de mayor longitud correcta con menor tiempo.

**Diagonales:** se colapsan a la componente dominante (§3.2). Las 4 zonas de color se disponen en cruz (N/E/S/O) coincidiendo con el color del puesto físico más cercano, para mapeo espacial natural.

**Parámetros:** `sequenceLength`, `reactWindow0`, `windowDecay`, `mode`, `duration`.

**AC:** (1) las secuencias generadas nunca repiten el mismo color 3 veces seguidas (legibilidad); (2) la ventana nunca baja de 0.45 s (límite de reacción humana con margen); (3) fallo de color ajeno registra al autor correcto en telemetría (para el "¡fuiste tú\!" del grupo).

---

### 4.1 Tabla maestra del set

| ID | Verbo | Dinámica | Botón | Estado código | Prioridad |
| :---- | :---- | :---- | :---- | :---- | :---- |
| MECH\_01 | ¡MANTÉN\! | Comp. FFA | — /tap | Nuevo | Media |
| MECH\_02 | ¡ESQUIVA\! | Comp. FFA | tap | **Existe** (2D \+ rama 3D) | Alta |
| MECH\_03 | ¡CORRE\! | Comp. FFA | mash | Parcial | Media |
| MECH\_04 | ¡APUNTA\! | Comp. FFA | hold | Nuevo | **Máxima** |
| MECH\_05 | ¡REACCIONA\! | Comp. duelo | tap | Nuevo | **Máxima** |
| MECH\_06 | ¡PERSIGUE\! | Asim. 1v3 | tap | Nuevo | Alta |
| MECH\_07 | ¡BOMBARDEA\! | Asim. 1v3 | tap | Nuevo (deriva de 02\) | Media |
| MECH\_08 | ¡SUJETA\! | Coop. | hold | Nuevo | Alta |
| MECH\_09 | ¡IGUALA\! | Coop./Comp. | tap | Nuevo | Media |

**Banco de reserva** (post-prototipo): ¡ROBA\! (hot-potato), ¡RECOGE\!, ¡DEFIENDE\! (portero 1v3), ¡MEMORIZA\!, ¡GRITA\! (mash cómico).

---

## 5\. El juego de tablero: sistemas, economía y fórmulas

El tablero es el meta-juego que da memoria, contexto y remontada. Diseñado bajo la regla de **cero tiempo muerto**: nunca 3 personas mirando a 1\.

### 5.1 Topología

Tablero circular de **N \= 20 casillas** (parámetro de dato). Los 4 avatares comparten el anillo; pueden ocupar la misma casilla (se apilan visualmente en offset). El anillo circular (vs. malla) se elige por legibilidad: el progreso relativo se lee de un vistazo, sin pathfinding mental.

### 5.2 Movimiento simultáneo (sin turnos)

Todos se mueven a la vez con el **medidor de parada**:

1. Cada jugador ve un medidor propio que cicla 1→6 a 4 Hz (ciclo completo 1.5 s).  
2. Tap detiene el medidor → ese es su movimiento. Sin tap antes del timeout (8 s) → se detiene solo en el valor del tick de timeout.  
3. Los 4 avatares avanzan simultáneamente con animación de 1.5 s.

**Propiedad de diseño:** el medidor es *habilidad percibida \+ azar real*: a 4 Hz nadie fija el valor de forma fiable (el tiempo de reacción humano ≈ 1 frame del ciclo), pero la sensación de "yo lo paré" mantiene la agencia. La distribución resultante es ≈ uniforme (verificado por test estadístico, AC del sistema).

### 5.3 Tipos de casilla y distribución

| Casilla | Cuota en anillo | Efecto técnico |
| :---- | :---- | :---- |
| Moneda (+) | 6 | `+coins ~ U{3,6}` (semilla) |
| Moneda (−) | 2 | `−coins ~ U{2,4}`, nunca por debajo de 0 |
| **Inversión** | 3 | Depositar `d ∈ {0, 25%, 50%}` del saldo (elección con palanca en ≤4 s). El mayor acumulado es "dueño"; paga 1 estrella al dueño cada vez que el marcador global de rondas avanza 3\. Empate de depósito → dueño es el primero en depositar (timestamp de tick). |
| **Propiedad/Trampa** | 3 | Comprar casilla vacía por 8 monedas. Rival que cae: transfiere 4 monedas al dueño (animación de robo explícita — el robo debe VERSE para producir la reacción social, P2). |
| Evento | 3 | Dispara modificador de ronda (tabla §5.5) |
| **Estrella** | 1 (móvil) | Comprar estrella por 15 monedas. Tras compra, la casilla estrella se muda a otra posición (semilla). Solo hay una a la vez. |
| Cangrejo (arsenal) | 2 | Otorga 1 arma aleatoria (tabla §5.4) |

**Invariante económico:** todo efecto que quita monedas a un jugador se las da a otro jugador o a un pozo visible — nunca desaparecen "al banco" sin representación visual. El flujo de dinero es parte del espectáculo.

### 5.4 Arsenal de mentalidad de cangrejo

Las armas se usan desde el inventario (máx. 1 arma en mano; recoger otra la sustituye) en la fase BOARD\_RESOLVE, apuntando con palanca a un rival y confirmando con tap.

| Arma | Efecto | Restricción anti-abuso |
| :---- | :---- | :---- |
| Guante de boxeo | El objetivo pierde 50 % de monedas, que caen al suelo del tablero y se reparten entre quienes están a ≤2 casillas | Solo usable contra el jugador en 1er puesto (es un arma anti-líder por definición) |
| Imán | Roba 1 objeto o 6 monedas del objetivo | Alcance ≤ 4 casillas |
| Colmena | Área: todos en la casilla objetivo y adyacentes pierden 3 monedas hacia el lanzador | No afecta al lanzador |

**Regla de diseño:** el arsenal apunta estructuralmente al líder (restricciones de objetivo), pero la *decisión* de usarlo es del grupo — el sistema habilita la coalición, no la ejecuta (P5).

### 5.5 Eventos (modificadores de ronda)

Sorteo con semilla entre: `DobleVelocidad` (microjuego siguiente a 1.5×), `MuerteSúbita` (en el siguiente microjuego de supervivencia, un golpe elimina), `ModoPiñata` (todo daño derrama monedas al suelo), `LluviaDeMonedas` (+2 a todos), `Apagón` (el HUD de puntuaciones se oculta hasta el final — refuerza la semi-opacidad de §6.3).

### 5.6 Economía: análisis de flujo

Objetivo de balance macro por sesión de 7 rondas (valores iniciales, a calibrar en piloto):

- Ingreso medio por jugador: \~35 monedas (microjuegos \~60 %, casillas \~40 %).  
- Sumideros: estrella (15), propiedad (8), inversión (variable). Los sumideros deben absorber \~70 % del ingreso medio para que las decisiones de gasto duelan (una economía donde sobra dinero mata la tensión).  
- Estrellas esperadas por sesión: 2–4 en juego (1 comprable \+ 1–2 por inversión \+ 2–3 de bonificación final). La bonificación final pesa lo suficiente para voltear un resultado de 1 estrella de diferencia — nunca uno de 3 (la remontada debe ser posible, no garantizada; §6.3).

---

# PARTE II — SISTEMAS

## 6\. Sistema de puntuación: matemática de varianza y catch-up

### 6.1 Modelo formal

Sea `s_i` la habilidad latente del jugador i y `X_{i,r}` su resultado en la ronda r. Cada microjuego se diseña para que `P(gana el menos hábil en una ronda) ≥ 0.15` (varianza alta por ronda), pero `E[victorias en 7 rondas]` ordene por habilidad (Ley de los Grandes Números). La palanca de diseño para subir varianza por ronda es el componente de azar de cada mecánica (posiciones por semilla, medidor oscilante, señal aleatoria); la palanca para bajar la varianza de sesión es el número de rondas.

**Reparto por microjuego competitivo (dato `payoutTable`):** 1º \= 6 monedas, 2º \= 4, 3º \= 2, 4º \= 1\. Cooperativo: éxito \= 4 a todos, fallo \= 1 a todos (recompensa mínima siempre: el "0" absoluto desengancha).

### 6.2 Catch-up (tres mecanismos, todos acotados)

1. **Rubber-banding en mecánica** (solo MECH\_03 y variantes de carrera): \+8 % velocidad al último. Acotado a no invertir resultados entre iguales (AC de MECH\_03).  
2. **Ventaja escalonada:** en microjuegos de recolección/carrera, el último puesto del marcador global inicia con 0.5 s de ventaja. Anunciado en pantalla ("¡ventaja para AZUL\!") — la ayuda visible es percibida como regla, la oculta como trampa.  
3. **Apuesta final (`FINAL_WAGER`):** cada jugador apuesta obligatoriamente 25/50/75 % de sus monedas (elección con palanca, 5 s). El pozo se reparte según el resultado del microjuego final con tabla 50/30/15/5 %. Efecto matemático: comprime la distribución de monedas y permite volteretas de hasta \~1 estrella de valor.

### 6.3 Estrellas de bonificación (puntuación semi-opaca)

Al Game Over se revelan **2 estrellas bonus** sorteadas (semilla de sesión) de un pool de objetivos ocultos rastreados por telemetría interna de la partida:

| Estrella | Criterio (contador interno) |
| :---- | :---- |
| Estrella Kamikaze | Más veces eliminado en microjuegos |
| Estrella Cangreja | Más armas usadas |
| Estrella Zen | Menor |
| Estrella Gatillo | Mejor latencia media en ¡REACCIONA\! |
| Estrella Inversora | Más monedas depositadas en inversión |
| Estrella Fantasma | Menos monedas robadas por rivales |

Cada estrella bonus vale 1 estrella normal. **Regla de acotación:** el sorteo excluye combinaciones que darían la victoria a quien va último con diferencia ≥ 3 estrellas (la remontada absurda es el clímax; la remontada *imposible de justificar* rompe la percepción de justicia). El desempate final: estrellas → monedas → victorias de microjuego → empate declarado (se muestran ambos en el podio; el empate compartido es socialmente productivo en bar).

### 6.4 Anti-frustración estructural

- Nunca dos derrotas "a cero" consecutivas: si un jugador queda 4º dos rondas seguidas, la tercera ronda fuerza en el sorteo una mecánica de su mejor histórico en la sesión (bias suave de selección, no de resultado).  
- La aleatoriedad jamás se anida (P3): una decisión → como máximo una tirada. Prohibido "objeto aleatorio que además falla aleatoriamente".

---

## 7\. Fases especiales: especificación cooperativa y asimétrica

### 7.1 Fase cooperativa (1 por sesión, ronda 3 o 4\)

Sustituye el microjuego de la ronda por un escenario coop de 60–90 s construido con MECH\_08/MECH\_09 ampliadas. Principios técnicos:

- **Estadísticas uniformes:** los 4 avatares comparten exactamente los mismos parámetros de movimiento. La dificultad es 100 % arquitectura del nivel.  
- **Catálogo de violaciones ergonómicas** (elementos de nivel, combinables por dato): `distanciasEstiradas` (objetivo A y B en extremos), `interseccionForzada` (rutas óptimas cruzadas en X, con colisión entre jugadores activada SOLO en esta fase), `cuelloBotella` (pasillo de 1 unidad), `sueloDinamico` (a t=30 s el terreno se divide y las asignaciones deben renegociarse en voz alta).  
- **Puntuación positiva:** sin vidas ni eliminación. Contador de puntos por objetivos entregados; los fallos restan 1 y reproducen un sonido cómico (el fracaso debe dar risa, no vergüenza). Umbrales de recompensa: bronce/plata/oro → 2/4/6 monedas a todos.

### 7.2 Fase asimétrica "Jefe" (1 por sesión, ronda 5 o 6, mecanismo Crawl)

- Un jugador (el líder del marcador — castigo estructural suave al primero) inicia como **Jefe**: hitbox mayor, \+50 % vida (3 golpes), ataque de área con telegraph 0.6 s.  
- Los 3 restantes lo atacan (tap \= golpe cuerpo a cuerpo, palanca \= movimiento).  
- **Robo de cuerpo:** quien conecta el golpe final SE CONVIERTE en el Jefe con la vida restante reiniciada a 2 golpes, y el Jefe caído pasa al bando atacante. La fase dura 75 s; gana quien es Jefe al agotarse el tiempo (recompensa 6 monedas; atacantes 2).  
- **Consecuencia de diseño:** nadie quiere rematar demasiado pronto (te vuelves el objetivo) pero nadie quiere ceder el remate. La traición está estructuralmente incentivada — es la fase de mayor producción de griterío por segundo, según el pilar P2.  
- **AC:** el intercambio de roles se resuelve en el mismo tick del golpe final sin frame de invulnerabilidad ambigua; el telegraph del Jefe siempre precede al daño.

---

## 8\. UI/HUD: especificación de legibilidad y feedback

### 8.1 Retícula de pantalla (1920×1080)

┌─────────────────────────────────────────────────┐

│ P1 (Rojo)    ZONA DE ACCIÓN CENTRAL    P2 (Azul)│  esquinas: 260×140 px

│  ▣ score                                ▣ score │  cada una, anclada al

│                                                  │  puesto físico más

│           (el centro es sagrado:                 │  cercano del gabinete

│         SOLO acción, jamás UI fija)              │

│                                                  │

│ P3 (Amar.)                            P4 (Verde)│

│  ▣ score      \[VERBO IMPERATIVO\]        ▣ score │  verbo: centro, 180 pt,

└─────────────────────────────────────────────────┘  ≤ 0.8 s, luego fade

- **Mapeo físico-visual:** la esquina de cada jugador corresponde a la posición física de su palanca en el gabinete de mesa (calibrado en la instalación; parámetro `seatLayout`).  
- **Tipografía:** display geométrica bold; cuerpo mínimo en pantalla 42 pt (legible a 1.5 m). Sin texto correlativo: máximo una palabra por comunicación.  
- **Contraste:** todo elemento crítico cumple ratio ≥ 4.5:1 contra su fondo; los 4 colores de jugador se verifican también en simulación de deuteranopia/protanopia (el color nunca es el único canal: cada jugador tiene además una forma — círculo, cuadrado, triángulo, rombo).

### 8.2 Jerarquía de feedback (presupuesto de "juice")

| Nivel | Cuándo | Recursos permitidos | Prohibido |
| :---- | :---- | :---- | :---- |
| Bajo | Economía de fondo (moneda ganada) | Brillo del contador 150 ms, tick sonoro −18 dB | Partículas, shake |
| Medio | Intención activa (agarrar, apuntar) | Halo, escala 1.1×, sonido −12 dB | Shake, flash de pantalla |
| Alto | Clímax (victoria, eliminación, robo de cuerpo) | Screen shake ≤ 300 ms, flash 1 frame, explosión de partículas, sonido a 0 dB | Usarlo \> 3 veces/minuto (auditoría automática en test: si una sesión de test excede el presupuesto de eventos de nivel alto, falla el AC de ritmo) |

- **HUD contextual:** las barras (vida del Jefe, progreso coop) aparecen al ser relevantes y se desvanecen a los 2 s de inactividad. Cero elementos permanentes fuera de las 4 esquinas.  
- **Salud del monitor:** en ATTRACT, desplazamiento sutil de todos los elementos estáticos (±4 px, ciclo 60 s) para prevenir burn-in en operación continua.

### 8.3 Audio

- Mezcla para bar ruidoso: banda de 200 Hz–5 kHz priorizada (la que sobrevive al ruido ambiente), efectos críticos con transitorios fuertes.  
- El verbo imperativo siempre lleva stinger sonoro distintivo por mecánica (identificación auditiva sin mirar).  
- Volumen maestro configurable por el local (panel de servicio), con ducking automático de música bajo efectos.

---

## 9\. Ritmo, dificultad y estructura temporal de sesión

### 9.1 Rampa de dificultad global

Multiplicador de sesión `D(r) = 1 + 0.06·(r−1)` aplicado a velocidad/densidad de cada microjuego en la ronda r (ronda 7 ⇒ 1.36×). Cada mecánica declara qué parámetros escalan con D (campo `difficultyScaling` en su definición). El microjuego final usa `D_final = 1.5` fijo (el clímax debe sentirse más rápido que todo lo anterior).

**Nota realista:** el "microjuego de 5 segundos" se diseña a 3–5 s reales de juego activo. La percepción de brevedad es el objetivo; el número exacto lo fija cada definición.

### 9.2 Selector de microjuegos (cuotas y variedad)

El Sequencer (código existente, se extiende) sortea con semilla respetando restricciones:

- Cuota por sesión de 7 rondas: ≥ 1 asimétrica, exactamente 1 coop (fase especial), resto competitivas.  
- Nunca la misma mecánica dos rondas seguidas; nunca la misma variante dos veces por sesión.  
- Bias anti-frustración de §6.4.  
- El sorteo es función pura de (seed, historial) → testeable.

### 9.3 Timeline de sesión de referencia (7 rondas)

| Min | Contenido |
| :---- | :---- |
| 0:00–0:30 | ATTRACT→JOIN, elección de color |
| 0:30–3:00 | Rondas 1–2 (competitivas suaves: presentan MANTÉN/REACCIONA) |
| 3:00–5:30 | Rondas 3–4 (entra fase coop) |
| 5:30–9:00 | Rondas 5–6 (entra fase Jefe; el arsenal ya circula) |
| 9:00–11:00 | Ronda 7 \+ FINAL\_WAGER \+ microjuego clímax |
| 11:00–12:00 | GAME\_OVER, estrellas bonus, podio, invitación a revancha |

Duración efectiva \~12 min ⇒ margen para 15–20 min con pausas humanas reales (brindis, risas, "espera que pido otra"). El timeout de cada estado absorbe esas pausas sin romper el ritmo.

---

# PARTE III — INGENIERÍA

## 10\. Arquitectura de software y separación núcleo/motor

### 10.1 Principio rector

**Toda la lógica de juego vive en C\# puro sin dependencia de UnityEngine** (`Barcade.Core`). Unity es una capa de presentación e input. Esta separación ya existe en la base de código y es innegociable: habilita el runner `dotnet test` de \< 2 s (bucle interno de TDD), el determinismo de simulación y la portabilidad futura.

┌──────────────────────────────────────────────────────┐

│  Barcade.Framework (Unity, MonoBehaviours)            │

│  \- Bootstrap, escenas, render, audio                  │

│  \- InputAdapter: HID gamepad → InputSnapshot          │

│  \- HudController, feedback, partículas                │

│  \- AddressablesLoader (contenido remoto)              │

│  \- TelemetryTransport (buffer local \+ envío)          │

└───────────────▲──────────────────┬───────────────────┘

                │ InputSnapshot    │ RenderState (POCO)

┌───────────────┴──────────────────▼───────────────────┐

│  Barcade.Core (C\# puro, determinista, testeable)      │

│  \- SessionStateMachine (FSM de §2.1)                  │

│  \- Sequencer (selector con cuotas, §9.2)              │

│  \- RoundPhaseMachine                                  │

│  \- IMicrogame (contrato) \+ 9 implementaciones MECH\_XX │

│  \- BoardModel (anillo, casillas, economía §5)         │

│  \- ScoreModel (reparto, catch-up, estrellas §6)       │

│  \- SeededRandom (PCG32, §13)                          │

│  \- GameTuning (parámetros deserializados de dato)     │

└──────────────────────────────────────────────────────┘

### 10.2 Contrato de microjuego

public interface IMicrogame

{

    MicrogameId Id { get; }

    void Initialize(MicrogameDefinition def, SeededRandom rng,

                    PlayerRoster roster, float difficultyMult);

    // Avanza exactamente un tick de simulación (60 Hz). Puro: sin efectos externos.

    void Tick(in InputSnapshot input);

    bool IsFinished { get; }

    MicrogameResult GetResult();      // ranking o resultado coop

    RenderState GetRenderState();     // POCO consumido por Framework

}

**Reglas del contrato:** `Tick` no asigna memoria en el heap en régimen estable (cero GC en gameplay, §14); `GetRenderState` devuelve datos, jamás referencias a objetos de Unity; toda aleatoriedad pasa por el `rng` inyectado.

### 10.3 Flujo de un frame

1. Hilo de simulación: acumula tiempo real; por cada 16.6 ms ejecuta `Tick` (catch-up máx. 3 ticks/frame para absorber hipos).  
2. `RenderState` se publica por doble buffer (sin locks en el hot path).  
3. Unity interpola posiciones entre los dos últimos estados para render suave a la frecuencia que dé el hardware.

### 10.4 Capa de presentación 3D (Unity)

El proyecto es **Unity 3D con URP**. La separación núcleo/motor implica una regla de proyección explícita:

- **La simulación vive en espacio lógico normalizado** (\[0,1\]² para arenas planas; el tablero en coordenadas de anillo). `Barcade.Core` no conoce metros ni ejes de Unity.  
- **El `StagePresenter` (Framework) proyecta espacio lógico → mundo 3D.** Cada mecánica declara su `StageProfile` (dato): tipo de cámara (ortográfica cenital para ¡ESQUIVA\!/¡PERSIGUE\!, perspectiva baja para ¡CORRE\!, fija frontal para ¡APUNTA\!/¡REACCIONA\!), mapeo de plano lógico a plano del mundo, y set de prefabs 3D por `EntityKind`\+`VisualVariant`.  
- **Cámara por fase, no por frame:** la cámara se coloca en `MG_INTRO` y no se mueve durante `MG_PLAY` salvo shake de feedback (§8.2). Cámara móvil durante el juego \= ilegible en un bar (P4). Excepción única: travelling lateral suave en ¡CORRE\!.  
- **Los avatares 3D** son modelos low-poly con el color del jugador y su forma distintiva (círculo/cuadrado/triángulo/rombo como remate sobre la cabeza — el canal de forma de §8.1 en 3D). La rama Esquiva-3D existente (héroe/enemigos Kenney, suelo de mazmorra, salto) es la referencia de estilo y se conserva como base de MECH\_02.  
- **Física:** la resolución de colisiones de juego es de Core (determinista, §13). El motor de físicas de Unity solo se usa para cosmética sin consecuencias (escombros, rebotes de monedas visuales, ragdoll cómico al ser eliminado). Regla dura: **nada que afecte al resultado pasa por PhysX**, porque PhysX no es determinista entre ejecuciones.  
- **Iluminación:** una direccional \+ ambiente, sin sombras dinámicas de alta resolución (presupuesto §14); materiales unlit o simple-lit de URP. El estilo flat-shaded hace el juego legible y barato a la vez.

---

## 11\. Modelo de datos: MicrogameDefinition y pools de contenido

### 11.1 Esquema de definición (dato, no código)

Cada microjuego concreto es un asset de datos (ScriptableObject en editor, serializado a JSON para la capa remota):

{

  "schemaVersion": 2,

  "id": "mg\_apunta\_viento\_01",

  "mechanic": "MECH\_04",

  "displayVerb": "¡APUNTA\!",

  "dynamics": "competitive",          // competitive | asym1v3 | coop

  "duration": 5.0,

  "difficultyScaling": \["targetMoving.speed", "windAccel"\],

  "params": {

    "chargeCycleSec": 1.2,

    "targetCount": 3,

    "targetMoving": { "enabled": true, "speed": 0.15 },

    "windAccel": 0.05

  },

  "payoutTable": \[6, 4, 2, 1\],

  "assets": { "palette": "default", "sfxStinger": "stinger\_apunta" },

  "stageProfile": {                     // presentación 3D (§10.4) — consumido por Framework, ignorado por Core

    "camera": "frontFixed",             // topDownOrtho | frontFixed | runnerLateral | boardOverview

    "environment": "env\_range\_01",      // prefab de escenario low-poly

    "entityPrefabSet": "set\_apunta"     // mapeo EntityKind+VisualVariant → prefab

  },

  "minPlayers": 2,

  "tags": \["aim", "charge", "wind"\]

}

**Validación:** un validador (parte del pipeline de build y del runner de tests) rechaza definiciones con parámetros fuera de rango declarado por la mecánica, `duration` fuera de \[3, 8\], o `payoutTable` que viole el invariante de recompensa mínima (§6.1).

### 11.2 Pools

`MicrogamePool` es una lista ponderada de definiciones \+ reglas de cuota (§9.2). El pool activo del gabinete es dato remoto: cambiar la rotación de contenido de toda la flota \= publicar un pool nuevo.

### 11.3 GameTuning

Objeto único de parámetros globales (curva de mash, tabla de reparto por defecto, precios del tablero, rampa D(r), presupuesto de feedback). Igual que las definiciones: dato versionado, remoto, con validación de rangos. **Ningún número de balance vive hardcodeado.**

---

## 12\. Contenido remoto: pipeline de Addressables en dos capas

| Capa | Grupo | Ubicación | Contenido | Actualizable |
| :---- | :---- | :---- | :---- | :---- |
| Local | `Barcade-Core` | StreamingAssets (en binario) | Framework, 9 mecánicas, assets base, pool mínimo de emergencia | Solo con reinstalación |
| Remota | `Barcade-Microgames` | Servidor de contenido (HTTP) | Definiciones, pools, GameTuning, assets de variantes | Push a flota por red |

**Flujo operativo:** al arrancar (y cada N horas en ATTRACT), el gabinete consulta el catálogo remoto; si hay versión nueva, descarga bundles delta en segundo plano y activa el contenido en el siguiente ciclo de sesión (nunca a mitad de partida). **Modo degradado:** sin red, el gabinete opera indefinidamente con el último contenido cacheado o, en último término, con el pool mínimo local — un bar sin internet nunca tiene una máquina muerta.

**Versionado y rollback:** cada publicación lleva `contentVersion` monotónica; el gabinete conserva la versión anterior en caché y revierte automáticamente si la nueva falla la validación de esquema o el smoke test de carga.

**Seguridad mínima viable:** catálogo servido por HTTPS con checksum por bundle; el gabinete rechaza contenido cuyo hash no coincida. (Firma criptográfica completa: fase de flota, no de prototipo.)

---

## 13\. Determinismo, RNG con semilla y reproducibilidad

- **Generador:** PCG32 (ya implementado como `SeededRandom`). Prohibido `System.Random` y `UnityEngine.Random` en `Barcade.Core` (regla verificada por analizador estático en CI).  
- **Jerarquía de semillas:** `sessionSeed` (de reloj al iniciar sesión) → deriva `roundSeed(r)` → deriva streams independientes por subsistema (spawner, tablero, sorteos). Streams separados evitan que consumir azar en un subsistema altere otro.  
- **Punto flotante:** la simulación usa `float` con operaciones cuya reproducibilidad se garantiza *en la misma plataforma* (x86-64/.NET). Suficiente para replay y tests en el hardware objetivo; el determinismo cross-platform no es requisito.  
- **Replay:** grabar (sessionSeed \+ secuencia de InputSnapshot) reproduce la sesión completa bit a bit. Es la herramienta primaria de depuración de bugs de campo: un fallo en el bar se reproduce en el escritorio con el archivo de replay de telemetría.

---

## 14\. Presupuesto de rendimiento (hardware objetivo N100)

Objetivo: **1080p a 60 fps estables** en Intel N100 con gráficos integrados (UHD, \~24 EU). El estilo geométrico hace esto holgado si se respetan presupuestos:

| Recurso | Presupuesto | Justificación |
| :---- | :---- | :---- |
| Draw calls por frame | ≤ 150 (SRP Batcher activo) | iGPU modesta; low-poly con materiales compartidos batchea agresivamente; GPU instancing para peligros/proyectiles repetidos |
| Triángulos por frame | ≤ 300 k | Holgado para low-poly flat-shaded en UHD del N100; los modelos Kenney de referencia están muy por debajo |
| Sombras | Solo una direccional a 1024, o blob shadows | Las sombras dinámicas son el mayor coste oculto en iGPU; el estilo flat las necesita apenas |
| Post-proceso URP | Solo bloom ligero \+ vignette | Sin AO, sin motion blur, sin DoF: caros en iGPU y dañan la legibilidad (P4) |
| Partículas simultáneas | ≤ 600 (pico clímax) | Un solo sistema pooled |
| Tiempo de CPU simulación/tick | ≤ 2 ms | Deja margen al render en los E-cores |
| GC en gameplay | 0 asignaciones/tick en régimen | Pools para peligros, proyectiles, partículas; structs para estado |
| Carga de microjuego | ≤ 200 ms (pre-carga en INTERMISSION) | El siguiente microjuego se resuelve y precarga durante la intermisión — la transición nunca muestra loading |
| Memoria total | ≤ 2 GB proceso | Margen enorme en 16 GB; el techo protege contra leaks de larga operación |
| Arranque a ATTRACT | ≤ 40 s desde power-on | Modo kiosco Windows: autologin \+ launcher watchdog |

**Operación continua:** el proceso corre 8–12 h/día. Watchdog externo reinicia la app si no responde 10 s; reinicio programado diario en horario de cierre del local. Log rotativo local con techo de 200 MB.

---

## 15\. Telemetría y métricas de piloto

La telemetría es el instrumento que convierte el piloto en datos de negocio y balance. Buffer local (SQLite) con envío por lotes cuando hay red; nada se pierde sin conexión.

### 15.1 Eventos (esquema resumido)

| Evento | Campos clave | Pregunta que responde |
| :---- | :---- | :---- |
| `session_start/end` | ts, playerCount, duración, completada/abandonada | ¿Cuánto juegan? ¿Abandonan? ¿Dónde? |
| `mg_result` | mgId, ranking, latencias, D(r) | Balance por mecánica y variante |
| `mech_winrate_asym` | mgId, ganóSolista | Banda 40–60 % de las asimétricas |
| `board_action` | tipo (inversión/trampa/arma/estrella), actor, objetivo | ¿Se usa el arsenal? ¿Contra quién? |
| `bonus_star_flip` | ¿la bonificación cambió al ganador? | Frecuencia de remontada (objetivo: 20–35 % de sesiones) |
| `idle_seat` | puesto, duración | Puestos muertos, ergonomía del gabinete |
| `crash/watchdog` | stacktrace, replayRef | Estabilidad de campo, con replay adjunto |

### 15.2 KPIs del piloto

- **Sesiones/noche por máquina** y **tasa de revancha** (sesión nueva \< 3 min tras Game Over — el KPI que valida el rompehielos).  
- **Duración media de sesión** vs. objetivo 15–20 min (calibra §9).  
- **Winrate del solista** en asimétricas (banda 40–60 %).  
- **Tasa de remontada por bonificación** (banda 20–35 %).  
- **Distribución de victorias por puesto físico** (detecta sesgos de ergonomía o de hardware de un puesto concreto).

Privacidad: la telemetría no captura ningún dato personal — solo eventos de juego anónimos por sesión.

---

## 16\. Estrategia de testing

Tres anillos, del más rápido al más lento:

1. **Runner puro (`dotnet test` sobre Barcade.Core, \< 2 s).** Es el bucle de TDD. Cubre: FSM de sesión (transiciones y timeouts), cada mecánica contra sus AC (§4), economía del tablero (invariantes de flujo §5.3), ScoreModel (reparto, catch-up acotado, sorteo de estrellas con exclusión §6.3), Sequencer (cuotas y anti-repetición), replay determinista (mismo seed+inputs ⇒ mismo estado final, comparación por hash de estado).  
2. **Tests de Unity (EditMode/PlayMode).** Solo lo que depende del motor: adaptador de input HID, carga Addressables (incluido modo degradado y rollback), presupuesto de feedback de alto nivel (§8.2, auditoría automática), humo de escena completa.  
3. **Playtest instrumentado (el anillo que no se automatiza).** Sesiones con personas reales — idealmente en bar, con tragos — midiendo los KPIs de §15.2 más dos observacionales: *risas/gritos por sesión* (conteo simple por observador) y *tiempo hasta primera interacción verbal entre desconocidos*. El pilar P2 solo se valida aquí.

**Tests estadísticos** (en el anillo 1, sobre 1 000+ semillas): uniformidad del medidor de parada (§5.2), escapabilidad de ¡ESQUIVA\!, no-inversión del rubber-banding, banda de winrate de asimétricas con bots calibrados.

---

## 17\. Roadmap de implementación y criterios de aceptación

### Hito 1 — Núcleo de tensión (validar la premisa social con mínimo código)

MECH\_05 (¡REACCIONA\!) \+ MECH\_04 (¡APUNTA\!) \+ FSM de sesión mínima (JOIN → 5 microjuegos → podio simple, sin tablero). **AC de hito:** 4 personas juegan una sesión completa sin explicación verbal de nadie; los AC unitarios de ambas mecánicas en verde; replay determinista funcionando.

### Hito 2 — Set competitivo completo

Consolidar MECH\_02 (existente) y MECH\_03; añadir MECH\_01. Sequencer con cuotas y anti-repetición. ScoreModel con reparto y estrellas bonus (sin tablero aún). **AC:** sesión de 7 rondas variada; remontada por bonificación observable; presupuesto de rendimiento cumplido en N100 real.

### Hito 3 — Dinámicas sociales

MECH\_06, MECH\_07 (asimétricas) y MECH\_08, MECH\_09 (cooperativas). Fases especiales de §7. **AC:** winrate del solista en banda con bots; coop imposible-por-abandono verificado imposible; playtest interno produce comunicación verbal espontánea.

### Hito 4 — El tablero

BoardModel completo: anillo, casillas, economía, arsenal, eventos, FINAL\_WAGER. **AC:** invariantes económicos en verde sobre 1 000 semillas de sesión simulada por bots; sesión completa dentro del presupuesto temporal de §2.2.

### Hito 5 — Contenido remoto \+ telemetría \+ endurecimiento

Pipeline de dos capas con rollback, telemetría con buffer local, modo kiosco con watchdog, prevención de burn-in. **AC:** actualización de pool remoto aplicada sin reinicio del binario; corte de red simulado sin degradación de juego; 8 h de soak test sin leak (memoria plana) ni crash.

### Hito 6 — Piloto en bar

Instalación real. Se cierra con datos: KPIs de §15.2 recolectados durante ≥ 2 semanas. **Criterio de éxito del prototipo completo:** tasa de revancha ≥ 30 % y evidencia observacional de que la máquina produce interacción entre desconocidos. Si ambos se cumplen, el concepto está validado y todo lo demás es escala.

---

# APÉNDICES

## A. Glosario

**AC** — criterio de aceptación verificable en test. **Dinámica** — estructura social del microjuego (competitiva/coop/asimétrica). **Mecánica** — plantilla de código reutilizable (MECH\_XX). **Microjuego** — instancia parametrizada de una mecánica (dato). **Pool** — lista ponderada de microjuegos activa en la flota. **Telegraph** — aviso visual previo obligatorio a todo daño. **Tick** — paso de simulación a 60 Hz fijo.

## B. Tabla maestra de parámetros globales (GameTuning, valores iniciales)

| Parámetro | Valor inicial | Rango válido |
| :---- | :---- | :---- |
| tickRate | 60 Hz | fijo |
| mash.minHz / satHz | 2 / 9 | 1–4 / 6–12 |
| board.ringSize | 20 | 16–24 |
| board.starPrice | 15 | 10–25 |
| board.propertyPrice / toll | 8 / 4 | — |
| payout.default | \[6,4,2,1\] | suma 10–16 |
| coop.payout win/lose | 4 / 1 | — |
| difficulty.rampPerRound | 0.06 | 0.03–0.10 |
| catchup.rubberBandPct | 0.08 | 0–0.15 |
| catchup.headStartSec | 0.5 | 0–1.0 |
| wager.options | \[0.25, 0.5, 0.75\] | — |
| bonusStars.count | 2 | 1–3 |
| session.rounds | 7 | 5–8 |

## C. Eventos de telemetría — referencia de campos

Todos los eventos comparten cabecera: `{cabinetId, contentVersion, sessionId, ts, tick}`. Los cuerpos por evento según §15.1. Formato de transporte: JSON Lines comprimido por lote; almacenamiento local SQLite con retención 30 días.

---

*Fin del GDD Técnico v2.0. Documento vivo: los valores de la tabla B son hipótesis iniciales que la telemetría del piloto (§15) confirma o corrige. La regla final permanece: ningún número de balance sobrevive al contacto con una mesa real de bar sin ajustarse.*

## D. Anexo de implementación (para entrega a desarrollo agéntico)

Este anexo cierra la brecha entre especificación y código: qué existe en el repo, qué se crea, con qué contratos exactos, y en qué orden de tareas.

### D.1 Mapa de integración con el repositorio existente

| Elemento del GDD | Estado en repo | Acción | Ubicación |
| :---- | :---- | :---- | :---- |
| `SeededRandom` (PCG32) | **Existe** | Reusar sin cambios; añadir derivación de streams (§13) si falta | `Barcade/Assets/Barcade/Core/Runtime/` |
| `InputSnapshot` | **Existe** | Extender con derivados (hold/mash) vía `InputInterpreter` nuevo | Core/Runtime |
| `RoundPhaseMachine` | **Existe** | Extender a la FSM completa de §2.1 (estados de tablero y final). No reescribir: añadir estados | Core/Runtime |
| `Sequencer` | **Existe** | Añadir cuotas, anti-repetición y bias de §9.2/§6.4 | Core/Runtime |
| `ScoreModel` | **Existe** | Añadir estrellas bonus con exclusión (§6.3) y wager (§6.2) | Core/Runtime |
| `MicrogameDefinition` | **Existe** (ScriptableObject) | Migrar a esquema v2 de §11.1 (añadir `dynamics`, `difficultyScaling`, `payoutTable`, `minPlayers`) con migrador de assets | Core \+ Editor |
| MECH\_02 ¡ESQUIVA\! | **Existe** (`EsquivaMicrogame`) | Adaptar al contrato `IMicrogame` de §10.2 si difiere; conservar tests | Core/Runtime/Microgames |
| MECH\_03 ¡CORRE\! | **Parcial** | Completar contra la spec §4 | Core/Runtime/Microgames |
| MECH\_01, 04–09 | No existen | Crear, una tarea por mecánica, tests primero | Core/Runtime/Microgames |
| `BoardModel` | No existe | Crear (§5 completa) | Core/Runtime/Board (nuevo) |
| `SessionStateMachine` | No existe (envuelve RoundPhaseMachine) | Crear | Core/Runtime |
| Rama Esquiva-3D (demo, modelos Kenney, salto) | **Existe** | Es la base de estilo de la presentación 3D; generalizar su escena a `StagePresenter` \+ `StageProfile` (§10.4) | Framework |
| Adaptador HID → InputSnapshot | Parcial (input 4 jugadores existe) | Verificar contra §3.2 (debounce, 60 Hz, diagonales) | Framework |
| Pipeline remoto 2 capas | **Existe como prueba de arquitectura** | Endurecer: rollback, checksum, modo degradado (§12) | Framework \+ docs/remote-content.md |
| Telemetría | No existe | Crear buffer SQLite \+ esquema apéndice C | Framework (transporte) \+ Core (eventos) |

**Regla para el agente:** antes de crear cualquier clase, buscar en `Barcade/Assets/Barcade/Core/` si existe un equivalente. La convención del repo (C\# puro en Core, MonoBehaviours solo en Framework, tests en `fast-tests/` espejando Core) es obligatoria. Todo trabajo nuevo sigue el flujo TDD del repo: test en rojo primero.

### D.2 Contratos de tipos (POCOs de referencia)

// ——— Resultado de microjuego ———

public readonly struct MicrogameResult

{

    public readonly ResultKind Kind;          // Ranked | CoopSuccess | CoopFail

    public readonly PlayerRank\[\] Ranks;       // vacío en coop

    public readonly int CoopScore;            // 0 si ranked

}

public readonly struct PlayerRank

{

    public readonly int Seat;                 // 0..3

    public readonly int Place;                // 1..4; empates comparten Place

    public readonly int Metric;               // ticks de supervivencia, impactos, latencia… (para telemetría/desempate)

}

// ——— Roster ———

public readonly struct PlayerRoster

{

    public readonly SeatState\[\] Seats;        // longitud 4

}

public enum SeatState { Empty, Human, HumanIdle, Bot }

// ——— Estado de render (Core → Framework, POCO puro) ———

public sealed class RenderState              // instancia doble-buffer, campos mutables reutilizados (cero alloc)

{

    public int Tick;

    public RenderEntity\[\] Entities;           // pool fijo, Count activo

    public int EntityCount;

    public HudState Hud;                      // scores, verbo activo, medidores

    public FeedbackEvent\[\] Feedback;          // eventos de juice del tick (nivel Bajo/Medio/Alto)

    public int FeedbackCount;

}

public struct RenderEntity

{

    public EntityKind Kind;                   // PlayerAvatar, Hazard, Projectile, Target, BoardPawn, Pickup…

    public int OwnerSeat;                     // \-1 si neutral

    public float X, Y;                        // espacio lógico normalizado \[0,1\]² (Core no conoce el mundo 3D)

    public float Height;                      // altura lógica (salto/caída), 0 \= suelo

    public float Rotation, Scale;

    public byte VisualVariant;

    // La proyección lógico→3D la hace StagePresenter según el StageProfile

    // de la mecánica activa (§10.4). Core jamás emite coordenadas de mundo Unity.

}

// ——— Tablero ———

public interface IBoardModel

{

    void BeginMovePhase(SeededRandom rng);

    void TickMove(in InputSnapshot input);          // medidores de parada

    bool MoveFinished { get; }

    BoardResolution Resolve(SeededRandom rng);      // efectos de casilla, uso de armas

    BoardSnapshot GetSnapshot();                    // posiciones, saldos, dueños, armas

}

public readonly struct BoardResolution

{

    public readonly CoinDelta\[\] CoinFlows;          // origen→destino (invariante §5.3: siempre con destino visible)

    public readonly StarEvent\[\] Stars;

    public readonly RoundModifier Modifier;         // evento de §5.5 o None

}

Estos contratos son la referencia de firma; el agente puede ampliar campos internos pero **no** cambiar la semántica ni introducir tipos de UnityEngine en Core (regla verificada en CI por el analizador ya presente en el flujo del repo).

### D.3 Especificación de bots

Dos usos: rellenar puestos vacíos en partida real y ejecutar los tests estadísticos de balance. Un solo sistema, dos calibraciones.

**Arquitectura:** cada mecánica implementa `IBotPolicy { PlayerInput Decide(BotSkill skill, in BotView view, SeededRandom rng); }` donde `BotView` es la proyección del estado que un humano vería (nunca estado oculto: el bot no hace trampa).

**Modelo de habilidad (`BotSkill`):** tres parámetros humanizadores globales \+ overrides por mecánica:

- `reactionDelayTicks`: Novato \~ N(20, 5\) ticks (\~330 ms), Óptimo \= 6 ticks (100 ms).  
- `errorRate`: probabilidad por decisión de acción subóptima. Novato 0.25, Óptimo 0.0.  
- `mashHz`: Novato \~ N(4.5, 1), Óptimo 9 (saturación).

**Calibraciones estándar:** `Bot.Novato` (rellena puestos vacíos en producción — debe perder más que ganar pero no ser trivial: winrate objetivo 10–20 % contra humanos medios), `Bot.Medio`, `Bot.Optimo` (solo tests: escapabilidad, alcanzabilidad, techos de mecánica).

**Los tests estadísticos de §16 se definen así:** p. ej. banda asimétrica \= winrate del solista con 4×`Bot.Medio` sobre 1 000 semillas ∈ \[40 %, 60 %\]. Esto convierte "bots calibrados" en un criterio ejecutable.

### D.4 Formato de replay

Archivo binario versionado `.bcrp`:

Header  { magic "BCRP", formatVersion u16, contentVersion u32,

          sessionSeed u64, startUtc i64, seatStates u8\[4\], gameTuningHash u64 }

Body    { framecount u32; luego por tick: deltas de PlayerInput

          codificados RLE (stick u4 \+ button u1 por seat ⇒ 3 bytes/tick peor caso,

          típicamente \<0.5 bytes/tick con RLE) }

Footer  { finalStateHash u64, crc32 }

- `finalStateHash` \= hash FNV-1a del estado serializado de Core al Game Over. El test de replay verifica: reproducir el body sobre el mismo `contentVersion` ⇒ mismo `finalStateHash`.  
- Tamaño esperado: sesión de 12 min ≈ 43 200 ticks ⇒ \< 50 KB. Se adjunta automáticamente a eventos `crash` de telemetría.  
- Si `contentVersion` no coincide al reproducir, el runner lo reporta como *replay incompatible*, nunca como fallo de determinismo.

### D.5 Desglose en tareas (formato del repo, `tasks/`)

Mapeo de los hitos de §17 a tareas atómicas estilo TASK-XXX, cada una con su AC del GDD como definición de hecho. Orden \= dependencias.

| Tarea | Contenido | Depende de | AC (ref) |
| :---- | :---- | :---- | :---- |
| T-101 | `InputInterpreter` (tap/hold/mash, debounce, diagonales) \+ tests | — | §3.2/§3.3 |
| T-102 | Extender FSM a `SessionStateMachine` (estados §2.1, timeouts) | T-101 | §2.1 invariantes |
| T-103 | MECH\_05 ¡REACCIONA\! | T-101 | AC MECH\_05 |
| T-104 | MECH\_04 ¡APUNTA\! | T-101 | AC MECH\_04 |
| T-104b | `StagePresenter` 3D: proyección lógico→mundo, StageProfiles (4 cámaras), prefabs low-poly base | T-101 | §10.4 |
| T-105 | Podio simple \+ sesión mínima de 5 microjuegos (**Hito 1**) | T-102–104b | AC Hito 1 |
| T-106 | Migración `MicrogameDefinition` a esquema v2 \+ validador | — | §11.1 |
| T-107 | Adaptar MECH\_02 al contrato; completar MECH\_03; crear MECH\_01 | T-106 | AC respectivos |
| T-108 | Sequencer: cuotas, anti-repetición, bias | T-106 | §9.2/§6.4 |
| T-109 | ScoreModel: estrellas bonus \+ exclusión (**Hito 2**) | T-108 | §6.3 |
| T-110 | Sistema de bots (política \+ 3 calibraciones) \+ tests estadísticos | T-107 | D.3 |
| T-111 | MECH\_06 y MECH\_07 (asimétricas) | T-110 | bandas winrate |
| T-112 | MECH\_08 y MECH\_09 \+ fases especiales §7 (**Hito 3**) | T-110 | AC Hito 3 |
| T-113 | `BoardModel`: anillo, medidor de parada, casillas, economía | T-102 | invariantes §5.3 |
| T-114 | Arsenal \+ eventos \+ FINAL\_WAGER (**Hito 4**) | T-113, T-109 | AC Hito 4 |
| T-115 | Replay `.bcrp` \+ test de hash final | T-105 | D.4 |
| T-116 | Telemetría (SQLite \+ eventos apéndice C) | T-105 | §15.1 |
| T-117 | Remoto endurecido: rollback, checksum, degradado | T-106 | §12 |
| T-118 | Kiosco: watchdog, burn-in, soak 8 h (**Hito 5**) | T-116–117 | AC Hito 5 |
| T-119 | Instrumentación de piloto \+ panel de KPIs (**Hito 6**) | T-118 | §15.2 |

**Instrucción operativa para el agente por tarea:** (1) leer la sección del GDD referenciada; (2) escribir los tests del AC en `fast-tests/` en rojo; (3) implementar en Core hasta verde; (4) solo entonces tocar Framework si la tarea lo requiere; (5) correr el runner completo (\< 2 s) antes de cerrar.  
