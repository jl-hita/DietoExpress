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
