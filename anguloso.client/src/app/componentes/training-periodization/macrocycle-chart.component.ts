import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

export type MacroPhaseKind = 'accumulation' | 'intensification' | 'deload' | 'realization' | 'general';

export interface MacroChartPhase {
  id: number; name: string; sequenceNumber: number; kind: MacroPhaseKind; startDate: string; endDate: string;
}
export interface MacroChartWeek {
  phaseId: number; weekInPhase: number; startDate: string; endDate: string;
  sessions: number; completedSessions: number; plannedMinutes: number;
  sets: number | null; rpe: number | null; isDeload: boolean;
}
export interface MacroChartMesocycle { id: number; name: string; startDate: string; endDate: string; }

const KIND_META: Record<MacroPhaseKind, { label: string; color: string }> = {
  accumulation: { label: 'Acumulación', color: '#16845b' },
  intensification: { label: 'Intensificación', color: '#c17b17' },
  deload: { label: 'Descarga', color: '#2474c6' },
  realization: { label: 'Realización', color: '#8055b5' },
  general: { label: 'General', color: '#64748b' }
};

let chartUid = 0;

/**
 * Gráfica profesional del macrociclo: fases (bandas), mesociclos, volumen semanal (barras),
 * intensidad RPE (línea, eje derecho) y frecuencia. Solo representa datos reales: las semanas
 * sin prescripción se muestran como N/D y nunca se inventan valores.
 */
@Component({
  selector: 'app-macrocycle-chart',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (model(); as m) {
      <div class="mc-stats" role="list">
        <div role="listitem"><strong>{{ m.weeks.length }}</strong><span>semanas</span></div>
        <div role="listitem"><strong>{{ m.phases.length }}</strong><span>fases</span></div>
        <div role="listitem"><strong>{{ m.hasVolume ? m.totalSets : 'N/D' }}</strong><span>series totales</span></div>
        <div role="listitem"><strong>{{ m.hasVolume ? m.averageSets : 'N/D' }}</strong><span>series / semana</span></div>
        <div role="listitem"><strong>{{ m.peakLabel }}</strong><span>semana de mayor volumen</span></div>
        <div role="listitem"><strong>{{ m.deloadWeeks }}</strong><span>semanas de descarga</span></div>
      </div>

      <div class="mc-legend" aria-hidden="true">
        @for (kind of m.kindsInUse; track kind.kind) {
          <span><i class="mc-swatch" [style.background]="kind.color"></i>{{ kind.label }}</span>
        }
        @if (m.hasVolume) { <span><i class="mc-swatch mc-swatch-bar"></i>Volumen (series)</span> }
        @if (m.hasRpe) { <span><i class="mc-swatch mc-swatch-line"></i>Intensidad (RPE medio)</span> }
        @if (m.deloadWeeks) { <span><i class="mc-swatch mc-swatch-hatch"></i>Semana de descarga</span> }
        @if (m.todayX !== null) { <span><i class="mc-swatch mc-swatch-today"></i>Hoy</span> }
      </div>

      <div class="mc-scroll">
        <div class="mc-canvas" [style.min-width.px]="m.width" (mouseleave)="hover.set(null)">
          <svg [attr.viewBox]="'0 0 ' + m.width + ' ' + m.height" [attr.height]="m.height" width="100%" preserveAspectRatio="xMinYMin meet"
               role="img" [attr.aria-label]="m.ariaLabel" focusable="false">
            <defs>
              <pattern [attr.id]="uid + '-hatch'" width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
                <rect width="6" height="6" fill="#ffffff" fill-opacity=".55" />
                <rect width="2.4" height="6" fill="#2474c6" fill-opacity=".55" />
              </pattern>
            </defs>

            <!-- Bandas de fase -->
            @for (p of m.phaseBands; track p.id) {
              <rect [attr.x]="p.x" [attr.y]="m.plotTop" [attr.width]="p.width" [attr.height]="m.plotHeight" [attr.fill]="p.color" fill-opacity=".07" />
              @if (!$first) { <line [attr.x1]="p.x" [attr.x2]="p.x" [attr.y1]="m.phaseLaneY" [attr.y2]="m.plotBottom" class="mc-phase-divider" /> }
            }

            <!-- Rejilla y ejes -->
            @for (t of m.volumeTicks; track t.value) {
              <line [attr.x1]="m.padLeft" [attr.x2]="m.width - m.padRight" [attr.y1]="t.y" [attr.y2]="t.y" [class]="t.value === 0 ? 'mc-axis' : 'mc-grid'" />
              <text [attr.x]="m.padLeft - 8" [attr.y]="t.y + 4" text-anchor="end" class="mc-tick">{{ t.value }}</text>
            }
            <text [attr.x]="m.padLeft - 8" [attr.y]="m.plotTop - 10" text-anchor="end" class="mc-axis-title">Series</text>
            @if (m.hasRpe) {
              @for (t of m.rpeTicks; track t.value) {
                <text [attr.x]="m.width - m.padRight + 8" [attr.y]="t.y + 4" class="mc-tick mc-tick-rpe">{{ t.value }}</text>
              }
              <text [attr.x]="m.width - m.padRight + 8" [attr.y]="m.plotTop - 10" class="mc-axis-title mc-tick-rpe">RPE</text>
            }

            <!-- Cabecera de fases -->
            @for (p of m.phaseBands; track p.id) {
              <g class="mc-phase" [class.is-selected]="p.id === selectedPhaseId()" tabindex="0" role="button"
                 [attr.aria-label]="'Fase ' + p.sequenceNumber + ': ' + p.name + ', ' + p.kindLabel + ', ' + p.weekCount + ' semanas'"
                 (click)="phaseSelect.emit(p.id)" (keydown.enter)="phaseSelect.emit(p.id)" (keydown.space)="phaseSelect.emit(p.id); $event.preventDefault()">
                <rect [attr.x]="p.x + 1" [attr.y]="m.phaseLaneY" [attr.width]="p.width - 2" [attr.height]="m.phaseLaneH" rx="8" [attr.fill]="p.color" fill-opacity=".14" class="mc-phase-bg" />
                <rect [attr.x]="p.x + 1" [attr.y]="m.phaseLaneY" [attr.width]="p.width - 2" height="4" rx="2" [attr.fill]="p.color" />
                <svg [attr.x]="p.x + 8" [attr.y]="m.phaseLaneY" [attr.width]="p.width - 16" [attr.height]="m.phaseLaneH" overflow="hidden">
                  <text x="0" y="22" class="mc-phase-name">{{ p.sequenceNumber }}. {{ p.name }}</text>
                  <text x="0" y="35" class="mc-phase-meta">{{ p.kindLabel }} · {{ p.weekCount }} sem.</text>
                </svg>
              </g>
            }

            <!-- Mesociclos -->
            @if (m.mesoBands.length) {
              <text [attr.x]="m.padLeft - 8" [attr.y]="m.mesoLaneY + 15" text-anchor="end" class="mc-axis-title">Meso</text>
              @for (b of m.mesoBands; track b.id) {
                <g>
                  <rect [attr.x]="b.x + 1" [attr.y]="m.mesoLaneY" [attr.width]="b.width - 2" height="22" rx="6" class="mc-meso" />
                  <svg [attr.x]="b.x + 7" [attr.y]="m.mesoLaneY" [attr.width]="Math.max(0, b.width - 14)" height="22" overflow="hidden">
                    <text x="0" y="15" class="mc-meso-name">{{ b.name }}</text>
                  </svg>
                </g>
              }
            }

            <!-- Barras de volumen -->
            @for (w of m.columns; track w.index) {
              @if (w.sets === null) {
                <rect [attr.x]="w.barX" [attr.y]="m.plotBottom - 3" [attr.width]="w.barWidth" height="3" rx="1.5" class="mc-bar-empty" />
              } @else {
                <rect [attr.x]="w.barX" [attr.y]="w.barY" [attr.width]="w.barWidth" [attr.height]="w.barHeight" rx="3" [attr.fill]="w.color" [attr.fill-opacity]="hover() === null || hover() === w.index ? 1 : .55" />
                @if (w.isDeload) {
                  <rect [attr.x]="w.barX" [attr.y]="w.barY" [attr.width]="w.barWidth" [attr.height]="w.barHeight" rx="3" [attr.fill]="'url(#' + uid + '-hatch)'" />
                }
                @if (m.showValues) {
                  <text [attr.x]="w.cx" [attr.y]="w.valueInside ? w.barY + 14 : w.barY - 5" text-anchor="middle" class="mc-value" [class.is-inside]="w.valueInside">{{ w.sets }}</text>
                }
              }
            }

            <!-- Línea de intensidad -->
            @for (path of m.rpePaths; track $index) { <path [attr.d]="path" class="mc-rpe-line" /> }
            @for (w of m.columns; track w.index) {
              @if (w.rpeY !== null) { <circle [attr.cx]="w.cx" [attr.cy]="w.rpeY" r="3.6" class="mc-rpe-dot" /> }
            }

            <!-- Marcador de hoy -->
            @if (m.todayX !== null) {
              <line [attr.x1]="m.todayX" [attr.x2]="m.todayX" [attr.y1]="m.plotTop - 4" [attr.y2]="m.plotBottom" class="mc-today" />
              <text [attr.x]="m.todayX" [attr.y]="m.plotBottom + 62" text-anchor="middle" class="mc-today-label">Hoy</text>
            }

            <!-- Eje X -->
            @for (w of m.columns; track w.index) {
              @if (w.showLabel) {
                <text [attr.x]="w.cx" [attr.y]="m.plotBottom + 17" text-anchor="middle" class="mc-xlabel">S{{ w.index + 1 }}</text>
                @if (m.showDates) { <text [attr.x]="w.cx" [attr.y]="m.plotBottom + 30" text-anchor="middle" class="mc-xdate">{{ w.dateLabel }}</text> }
              }
            }
            <text [attr.x]="m.padLeft - 8" [attr.y]="m.plotBottom + 46" text-anchor="end" class="mc-axis-title">Sesiones</text>
            @for (w of m.columns; track w.index) {
              <text [attr.x]="w.cx" [attr.y]="m.plotBottom + 46" text-anchor="middle" class="mc-sessions" [class.is-done]="w.allDone">{{ w.sessions }}{{ w.allDone ? '✓' : '' }}</text>
            }

            <!-- Zonas de interacción -->
            @for (w of m.columns; track w.index) {
              <rect [attr.x]="w.x" [attr.y]="m.plotTop" [attr.width]="w.width" [attr.height]="m.plotHeight + 56" fill="transparent" class="mc-hit" tabindex="0"
                    [attr.title]="w.tooltipTitle" [attr.aria-label]="w.ariaLabel"
                    (mouseenter)="hover.set(w.index)" (focus)="hover.set(w.index)" (blur)="hover.set(null)" />
            }
            @if (hover() !== null && m.columns[hover()!]; as active) {
              <rect [attr.x]="active.x" [attr.y]="m.plotTop" [attr.width]="active.width" [attr.height]="m.plotHeight" class="mc-hover-band" pointer-events="none" />
            }
          </svg>

          @if (hover() !== null && m.columns[hover()!]; as tip) {
            <div class="mc-tip" role="status" [class.mc-tip-left]="tip.cx / m.width < .18" [class.mc-tip-right]="tip.cx / m.width > .82"
                 [style.left.%]="tip.cx / m.width * 100" [style.top.px]="m.plotTop + 4">
              <strong><i class="mc-swatch" [style.background]="tip.color"></i>Semana {{ tip.index + 1 }} · {{ tip.phaseName }}</strong>
              <small>{{ tip.dateRange }} · semana {{ tip.weekInPhase }} de la fase{{ tip.isDeload ? ' (descarga)' : '' }}</small>
              <dl>
                <dt>Volumen</dt><dd>{{ tip.sets === null ? 'N/D' : tip.sets + ' series' }}</dd>
                <dt>Intensidad</dt><dd>{{ tip.rpe === null ? 'N/D' : 'RPE ' + tip.rpeLabel }}</dd>
                <dt>Sesiones</dt><dd>{{ tip.sessions }} ({{ tip.completedSessions }} realizadas)</dd>
                <dt>Duración</dt><dd>{{ tip.plannedMinutes }} min</dd>
              </dl>
            </div>
          }
        </div>
      </div>

      <table class="mc-sr-only">
        <caption>Datos semanales del macrociclo</caption>
        <thead><tr><th>Semana</th><th>Fase</th><th>Series</th><th>RPE medio</th><th>Sesiones</th></tr></thead>
        <tbody>
          @for (w of m.columns; track w.index) {
            <tr><td>{{ w.index + 1 }}</td><td>{{ w.phaseName }}</td><td>{{ w.sets === null ? 'N/D' : w.sets }}</td><td>{{ w.rpe === null ? 'N/D' : w.rpeLabel }}</td><td>{{ w.sessions }}</td></tr>
          }
        </tbody>
      </table>
    } @else {
      <p class="mc-empty">Define las fases del macrociclo para ver su gráfica de carga.</p>
    }
  `,
  styles: [`
    :host { display: block; color: var(--text-color, #1f2937); }
    .mc-stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(118px, 1fr)); gap: 8px; margin-bottom: 12px; }
    .mc-stats div { padding: 9px 12px; border: 1px solid var(--border-color, #e2e8f0); border-radius: 12px; background: var(--surface-container, #f8fafc); }
    .mc-stats strong { display: block; font-size: 1.15rem; line-height: 1.2; font-variant-numeric: tabular-nums; }
    .mc-stats span { font-size: .72rem; color: var(--secondary-text-color, #64748b); }
    .mc-legend { display: flex; flex-wrap: wrap; gap: 6px 16px; margin: 0 0 8px; font-size: .75rem; color: var(--secondary-text-color, #64748b); }
    .mc-legend span { display: inline-flex; align-items: center; gap: 6px; }
    .mc-swatch { display: inline-block; width: 11px; height: 11px; border-radius: 3px; flex: none; }
    .mc-swatch-bar { background: #64748b; opacity: .7; }
    .mc-swatch-line { height: 3px; border-radius: 2px; background: #111827; width: 16px; }
    .mc-swatch-hatch { background: repeating-linear-gradient(45deg, #2474c6 0 2px, #fff 2px 5px); border: 1px solid #2474c6; }
    .mc-swatch-today { width: 0; height: 12px; border-radius: 0; border-left: 2px dashed #dc2626; }
    .mc-scroll { overflow-x: auto; padding-bottom: 4px; }
    .mc-canvas { position: relative; }
    svg { display: block; overflow: visible; font-family: inherit; }
    svg svg { overflow: hidden; }
    .mc-grid { stroke: var(--border-color, #e2e8f0); stroke-width: 1; stroke-dasharray: 3 4; }
    .mc-axis { stroke: var(--secondary-text-color, #94a3b8); stroke-width: 1.2; }
    .mc-phase-divider { stroke: var(--border-color, #cbd5e1); stroke-width: 1; stroke-dasharray: 2 3; }
    .mc-tick, .mc-xlabel, .mc-xdate, .mc-axis-title, .mc-sessions { fill: var(--secondary-text-color, #64748b); font-size: 11px; font-variant-numeric: tabular-nums; }
    .mc-tick-rpe { fill: #111827; }
    .mc-axis-title { font-size: 10px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; }
    .mc-xlabel { font-weight: 700; fill: var(--text-color, #334155); }
    .mc-xdate { font-size: 9.5px; }
    .mc-sessions { font-weight: 600; }
    .mc-sessions.is-done { fill: #16845b; }
    .mc-value.is-inside { fill: #fff; }
    .mc-value { fill: var(--text-color, #334155); font-size: 10.5px; font-weight: 700; font-variant-numeric: tabular-nums; }
    .mc-bar-empty { fill: none; stroke: var(--secondary-text-color, #94a3b8); stroke-dasharray: 3 2; }
    .mc-rpe-line { fill: none; stroke: #111827; stroke-width: 2.2; stroke-linejoin: round; stroke-linecap: round; }
    .mc-rpe-dot { fill: #fff; stroke: #111827; stroke-width: 2; }
    .mc-today { stroke: #dc2626; stroke-width: 1.6; stroke-dasharray: 5 4; }
    .mc-today-label { fill: #dc2626; font-size: 10.5px; font-weight: 700; }
    .mc-phase { cursor: pointer; outline: none; }
    .mc-phase-name { fill: var(--text-color, #1f2937); font-size: 12px; font-weight: 700; }
    .mc-phase-meta { fill: var(--secondary-text-color, #64748b); font-size: 10.5px; }
    .mc-phase:hover .mc-phase-bg, .mc-phase:focus-visible .mc-phase-bg { fill-opacity: .26; }
    .mc-phase.is-selected .mc-phase-bg { fill-opacity: .3; stroke: var(--text-color, #1f2937); stroke-width: 1.4; }
    .mc-meso { fill: var(--surface-container, #eef2f7); stroke: var(--border-color, #cbd5e1); }
    .mc-meso-name { fill: var(--text-color, #334155); font-size: 11px; font-weight: 600; }
    .mc-hit { outline: none; cursor: crosshair; }
    .mc-hit:focus-visible { stroke: var(--primary-color, #0d766e); stroke-width: 2; }
    .mc-hover-band { fill: var(--text-color, #0f172a); fill-opacity: .05; }
    .mc-tip { position: absolute; z-index: 3; min-width: 210px; max-width: 280px; transform: translateX(-50%); padding: 10px 12px; border-radius: 12px;
      background: #0f172a; color: #f8fafc; box-shadow: 0 12px 28px rgba(15, 23, 42, .28); pointer-events: none; font-size: .78rem; }
    .mc-tip-left { transform: translateX(-8px); }
    .mc-tip-right { transform: translateX(calc(-100% + 8px)); }
    .mc-tip strong { display: flex; align-items: center; gap: 7px; font-size: .82rem; }
    .mc-tip small { display: block; margin: 3px 0 8px; color: #cbd5e1; }
    .mc-tip dl { display: grid; grid-template-columns: auto 1fr; gap: 3px 14px; margin: 0; }
    .mc-tip dt { color: #94a3b8; }
    .mc-tip dd { margin: 0; text-align: right; font-weight: 650; font-variant-numeric: tabular-nums; }
    .mc-empty { margin: 0; color: var(--secondary-text-color, #64748b); }
    .mc-sr-only { position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0; overflow: hidden; clip: rect(0 0 0 0); border: 0; white-space: nowrap; }
    @media (prefers-reduced-motion: no-preference) { .mc-phase-bg { transition: fill-opacity .15s ease; } }
  `]
})
export class MacrocycleChartComponent {
  readonly Math = Math;
  readonly uid = `mc-${++chartUid}`;

  readonly phases = input.required<MacroChartPhase[]>();
  readonly weeks = input.required<MacroChartWeek[]>();
  readonly mesocycles = input<MacroChartMesocycle[]>([]);
  readonly selectedPhaseId = input<number | null>(null);
  readonly phaseSelect = output<number>();

  readonly hover = signal<number | null>(null);

  readonly model = computed(() => {
    const phases = this.phases();
    const weeks = this.weeks();
    if (!phases.length || !weeks.length) return null;

    const phaseById = new Map(phases.map(phase => [phase.id, phase]));
    const sets = weeks.map(week => week.sets);
    const known = sets.filter((value): value is number => value !== null);
    const hasVolume = known.some(value => value > 0);
    const hasRpe = weeks.some(week => week.rpe !== null);
    const maxSets = Math.max(0, ...known);
    const volumeStep = [1, 2, 5, 10, 20, 25, 50, 100, 200].find(step => Math.ceil(Math.max(maxSets, 4) / step) <= 5) ?? 200;
    const volumeMax = Math.max(4, Math.ceil(maxSets / volumeStep) * volumeStep);

    const count = weeks.length;
    const padLeft = 72;
    const padRight = hasRpe ? 46 : 18;
    const colW = Math.min(60, Math.max(26, (940 - padLeft - padRight) / count));
    const width = Math.round(padLeft + padRight + count * colW);
    const hasMeso = this.mesocycles().length > 0;
    const phaseLaneY = 4, phaseLaneH = 44;
    const mesoLaneY = phaseLaneY + phaseLaneH + 6;
    const plotTop = hasMeso ? mesoLaneY + 22 + 30 : phaseLaneY + phaseLaneH + 30;
    const plotHeight = 200;
    const plotBottom = plotTop + plotHeight;
    const height = plotBottom + 70;
    const yVolume = (value: number) => plotBottom - value / volumeMax * plotHeight;
    const yRpe = (value: number) => plotBottom - Math.max(0, Math.min(10, value)) / 10 * plotHeight;
    const labelStep = count > 52 ? 4 : count > 26 ? 2 : 1;
    const showDates = colW >= 34 || labelStep > 1;
    const showValues = colW >= 30 && count <= 30;

    const columns = weeks.map((week, index) => {
      const phase = phaseById.get(week.phaseId);
      const kind: MacroPhaseKind = week.isDeload ? 'deload' : (phase?.kind ?? 'general');
      const x = padLeft + index * colW;
      const cx = x + colW / 2;
      const barWidth = Math.max(10, Math.min(34, colW * .58));
      const barHeight = week.sets === null ? 0 : Math.max(week.sets > 0 ? 2 : 0, week.sets / volumeMax * plotHeight);
      const phaseColor = KIND_META[phase?.kind ?? 'general'].color;
      const rpeLabel = week.rpe === null ? 'N/D' : (Math.round(week.rpe * 10) / 10).toLocaleString('es-ES');
      const dateRange = `${this.shortDate(week.startDate)} – ${this.shortDate(week.endDate)}`;
      const phaseName = phase ? `Fase ${phase.sequenceNumber} · ${phase.name}` : 'Sin fase';
      const summary = `${phaseName} · semana ${index + 1}: ${week.sets === null ? 'sin datos de volumen' : week.sets + ' series'}, ${week.sessions} sesiones`;
      return {
        index, x, cx, width: colW, barWidth, barX: cx - barWidth / 2, barY: plotBottom - barHeight, barHeight,
        color: kind === 'deload' && phase?.kind !== 'deload' ? KIND_META.deload.color : phaseColor,
        sets: week.sets, valueInside: barHeight > 26, rpe: week.rpe, rpeLabel, rpeY: week.rpe === null ? null : yRpe(week.rpe),
        sessions: week.sessions, completedSessions: week.completedSessions, plannedMinutes: week.plannedMinutes,
        allDone: week.sessions > 0 && week.completedSessions >= week.sessions,
        isDeload: week.isDeload, phaseName, weekInPhase: week.weekInPhase, dateRange,
        dateLabel: this.shortDate(week.startDate), showLabel: index % labelStep === 0,
        tooltipTitle: summary, ariaLabel: summary
      };
    });

    const phaseBands = phases.flatMap(phase => {
      const indexes = columns.filter(column => weeks[column.index].phaseId === phase.id).map(column => column.index);
      if (!indexes.length) return [];
      const first = indexes[0], last = indexes[indexes.length - 1];
      const meta = KIND_META[phase.kind];
      return [{
        id: phase.id, name: phase.name, sequenceNumber: phase.sequenceNumber, color: meta.color, kindLabel: meta.label,
        x: padLeft + first * colW, width: (last - first + 1) * colW, weekCount: last - first + 1
      }];
    });

    const mesoBands = this.mesocycles().flatMap(meso => {
      const start = meso.startDate.slice(0, 10), end = (meso.endDate || meso.startDate).slice(0, 10);
      const first = weeks.findIndex(week => week.endDate.slice(0, 10) >= start && week.startDate.slice(0, 10) <= end);
      if (first < 0) return [];
      let last = first;
      weeks.forEach((week, index) => { if (week.startDate.slice(0, 10) <= end && week.endDate.slice(0, 10) >= start) last = Math.max(last, index); });
      return [{ id: meso.id, name: meso.name, x: padLeft + first * colW, width: (last - first + 1) * colW }];
    });

    const rpePaths: string[] = [];
    let segment: string[] = [];
    for (const column of columns) {
      if (column.rpeY === null) { if (segment.length) rpePaths.push(segment.join(' ')); segment = []; continue; }
      segment.push(`${segment.length ? 'L' : 'M'}${column.cx.toFixed(1)} ${column.rpeY.toFixed(1)}`);
    }
    if (segment.length) rpePaths.push(segment.join(' '));

    const today = new Date();
    const todayIso = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
    const todayIndex = weeks.findIndex(week => todayIso >= week.startDate.slice(0, 10) && todayIso <= week.endDate.slice(0, 10));
    let todayX: number | null = null;
    if (todayIndex >= 0) {
      const start = Date.parse(weeks[todayIndex].startDate.slice(0, 10) + 'T00:00:00Z');
      const days = (Date.parse(todayIso + 'T00:00:00Z') - start) / 86400000;
      todayX = padLeft + (todayIndex + Math.min(1, (days + .5) / 7)) * colW;
    }

    const peakIndex = hasVolume ? sets.findIndex(value => value === maxSets) : -1;
    const kindsInUse = [...new Set(phases.map(phase => phase.kind))].map(kind => ({ kind, ...KIND_META[kind] }));
    const totalSets = known.reduce((total, value) => total + value, 0);

    return {
      weeks, phases, width, height, padLeft, padRight, plotTop, plotBottom, plotHeight, phaseLaneY, phaseLaneH, mesoLaneY,
      columns, phaseBands, mesoBands, rpePaths, todayX, hasVolume, hasRpe, showDates, showValues, kindsInUse,
      totalSets, averageSets: known.length ? Math.round(totalSets / known.length) : 0,
      deloadWeeks: weeks.filter(week => week.isDeload).length,
      peakLabel: peakIndex >= 0 ? `S${peakIndex + 1} · ${maxSets}` : 'N/D',
      volumeTicks: Array.from({ length: volumeMax / volumeStep + 1 }, (_, i) => ({ value: i * volumeStep, y: yVolume(i * volumeStep) })),
      rpeTicks: [0, 5, 10].map(value => ({ value, y: yRpe(value) })),
      ariaLabel: `Gráfica del macrociclo: ${count} semanas en ${phases.length} fases. ${hasVolume ? `Volumen total ${totalSets} series.` : 'Sin datos de volumen todavía.'}`
    };
  });

  private shortDate(iso: string): string {
    const [, month, day] = iso.slice(0, 10).split('-');
    return day && month ? `${day}/${month}` : iso;
  }
}
