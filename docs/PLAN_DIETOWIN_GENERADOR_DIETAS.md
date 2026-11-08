# Plan de Acción: Nivel DietoWin para el Generador de Dietas de DietoExpress

Este documento recoge el plan de evolución y auditoría técnica para elevar el generador automático de dietas de **DietoExpress** al estándar de calidad clínica, culinaria y rendimiento de **DietoWin** (referencia funcional del sector).

---

## Estado Inicial y Diagnóstico Comparativo

- **Puntuación estimada inicial:** ~70% respecto al estándar DietoWin.
- **Puntos fuertes conservados:** 
  - Seguridad clínica transversal en alérgenos e intolerancias (`DietaryRestrictionRules`, sin relajación silenciosa de restricciones).
  - Tolerancias y rebalanceos de macronutrientes.
- **Causas de la brecha funcional:**
  1. *Estructura Culinaria:* Las comidas principales se generaban como listas planas de alimentos ("bolsa de macros") en lugar de una arquitectura de platos culinarios comprensibles para el paciente (*1º Plato, 2º Plato, Guarnición, Postre*).
  2. *Rotación Semanal:* Las frecuencias de repetición usaban penalizaciones combinatorias que podían permitir alimentos similares en días consecutivos.
  3. *Eficiencia de Cálculo:* Búsqueda local combinatoria que evaluaba toda la semana repetidamente y obligaba a reintentos globales.

---

## Fases del Plan de Acción

### Paso 1: Estructura de Platos Culinarios Coherentes (1º Plato, 2º Plato, Guarnición, Postre)
- **Objetivo:** Organizar las comidas principales (comida y cena) y las colaciones (desayuno, media mañana, merienda) bajo un modelo culinario realista, asignando a cada componente su función gastronómica clara:
  - *Comida / Cena:* 
    - **Primer plato:** Hidrato o verdura principal (ej. Legumbre guisada, Arroz, Pasta, Verdura cocida/al vapor, Patata).
    - **Segundo plato:** Fuente proteica principal (ej. Pescado blanco/azul, Pollo, Pavo, Ternera, Huevo, Tofu).
    - **Guarnición:** Ensalada fresca o verdura salteada.
    - **Grasa culinaria:** Aceite de oliva virgen para aliño/cocinado.
    - **Postre opcional:** Fruta fresca de temporada.
  - *Desayunos y Colaciones:*
    - Base de hidrato de desayuno (pan integral, copos de avena), proteína ligera (lácteo, huevo, queso fresco), fruta o fruto seco.
- **Trazabilidad:** Cada `MealItemDto` conserva y expone su rol culinario sin romper compatibilidad con el frontend ni con la validación semántica.

### Paso 2: Matriz de Separación Temporal y Rotación Semanal Determinista
- **Objetivo:** Garantizar variedad natural sin depender de reintentos estocásticos:
  - Regla de no-consecutividad para legumbres (mínimo 48h de separación entre comidas con legumbre).
  - Rotación alternada entre pescado blanco, pescado azul, carne blanca, carne roja y huevos.
  - No repetición de la misma fruta o la misma verdura dentro de la misma jornada.
  - Asignación estacional y variedad visual del menú semanal.

### Paso 3: Sistema de Equivalencias por Intercambios Precalculados
- **Objetivo:** Cálculo calórico y de macronutrientes exacto y ultrarrápido:
  - Definición previa de porciones de intercambio por comida (ej. 3 intercambios de hidratos, 2.5 de proteínas, 2 de verduras, 1 de fruta, 1.5 de grasa).
  - Ajuste exacto de porciones estándar (125g yogur, raciones redondas de 100g, 150g, 200g para verduras y carnes, 60-80g para cereales en crudo / 150-200g cocido).
  - Reducción drástica del tiempo de generación (< 200 ms por dieta semanal) con 0 reintentos por desajuste semántico.

---

## Criterios de Finalización (DoD)
1. Los tres pasos deben estar implementados y cubiertos por pruebas unitarias y de integración.
2. 100% de tests del proyecto en verde (sin regresiones de seguridad clínica ni de exclusiones).
3. No hacer merge a `main` hasta que los tres puntos estén completados satisfactoriamente y aprobados explícitamente.

---

## Estado de implementación (motor determinista)

Medición sobre el catálogo BEDCA real (957 alimentos), endpoint `POST /api/dietas/generate`, semana de 5 comidas:

| | Antes | Ahora |
|---|---|---|
| Perfiles probados que generan dieta (11) | 0 de 11 (422 tras 3 intentos) | 11 de 11 |
| Tiempo por petición | 14–32 s | 30–70 ms (primera llamada tras arrancar ≈ 260 ms por JIT) |
| Desviación diaria de kcal | — (fallaba) | ≤ ±1,5 % (cetogénica ≈ −4 %) |
| Reintentos globales | 3 (con optimización de 5–7 s cada uno) | 0 en la medición; reintentos locales por día en el motor |

Arquitectura (`Anguloso.Server/Logica`):

- `FoodCulinaryClassifier` + tabla `food_culinary_profile` (rol, familia, popularidad, estado crudo/cocinado, porciones,
  etiquetas de alérgenos curadas). Lista blanca: lo no reconocido no entra; los datos incoherentes (p. ej. «Arroz, hervido»
  con kcal del crudo) se marcan con `excluded_reason`. `FoodCulinaryProfileSeeder` lo materializa al arrancar (idempotente).
- `DietMenuEngine`: esqueleto semanal de rotación (legumbre/arroz/pasta/patata; ave, pescado blanco/azul, carne roja, huevo),
  plantillas españolas (desayuno, media mañana, comida 1º/2º/guarnición/pan/postre, merienda, cena ligera), solver de
  porciones por descenso de coordenadas sobre la rejilla de raciones y reintento local por día.
- `PortionPolicy`: política de porciones compartida con el validador final (escala con la energía objetivo).
- Pesos en **crudo** para cereales, legumbres, carnes y pescados cuando el catálogo los guarda en crudo (como DietoWin).

Seguridad (sin relajar nada):

- Filtro único y precompilado (`DietaryRestrictionRules.ExclusionMatcher`); solo alimentos que lo superan llegan al motor.
- Vegano, vegetariano y alérgenos tienen **una única fuente**: `food_dietary_profile` (curación versionada en
  `FoodCuration/food-dietary-profiles.json`, sin columnas nulas). `food_culinary_profile` solo guarda rol, familia, porciones y
  etiquetas. Los alérgenos del perfil dietético **se suman** a los importados y al filtro de exclusiones, pero no cuentan como
  verificación: con alergias declaradas se sigue exigiendo `allergen_data_status = 'reported'`. Con intolerancias
  (gluten/lactosa/FODMAP) basta con que el alimento tenga perfil dietético.
- El validador final independiente (clínico + semántico) se mantiene.

Pendiente: revisión humana de las etiquetas curadas de `food_dietary_profile` antes de promoverlas a `foods.allergen_tags`,
retirada del código legado de `AdvancedDietOptimizerService.OptimizeWeeklyPlanAsync` (ya no se invoca; solo se usan las
recetas y `RegenerateMeal`), y migrar `DietRegenerationService` al mismo perfil culinario.
