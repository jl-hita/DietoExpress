import { describe, expect, it } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MacroChartPhase, MacroChartWeek, MacrocycleChartComponent } from './macrocycle-chart.component';

const phases: MacroChartPhase[] = [
  { id: 1, name: 'Base', sequenceNumber: 1, kind: 'accumulation', startDate: '2030-01-07', endDate: '2030-01-20' },
  { id: 2, name: 'Descarga', sequenceNumber: 2, kind: 'deload', startDate: '2030-01-21', endDate: '2030-01-27' }
];
const week = (phaseId: number, n: number, start: string, end: string, sets: number | null, rpe: number | null, isDeload = false): MacroChartWeek =>
  ({ phaseId, weekInPhase: n, startDate: start, endDate: end, sessions: 3, completedSessions: 0, plannedMinutes: 180, sets, rpe, isDeload });
const weeks: MacroChartWeek[] = [
  week(1, 1, '2030-01-07', '2030-01-13', 40, 7),
  week(1, 2, '2030-01-14', '2030-01-20', null, null),
  week(2, 1, '2030-01-21', '2030-01-27', 20, 5, true)
];

function create(inputPhases: MacroChartPhase[], inputWeeks: MacroChartWeek[]): ComponentFixture<MacrocycleChartComponent> {
  const fixture = TestBed.createComponent(MacrocycleChartComponent);
  fixture.componentRef.setInput('phases', inputPhases);
  fixture.componentRef.setInput('weeks', inputWeeks);
  fixture.detectChanges();
  return fixture;
}

describe('MacrocycleChartComponent', () => {
  it('dibuja una banda por fase y una zona interactiva por semana', () => {
    const el: HTMLElement = create(phases, weeks).nativeElement;
    expect(el.querySelectorAll('g.mc-phase').length).toBe(2);
    expect(el.querySelectorAll('rect.mc-hit').length).toBe(3);
  });

  it('no inventa datos: las semanas sin prescripción son N/D y no suman volumen', () => {
    const fixture = create(phases, weeks);
    const model = fixture.componentInstance.model()!;
    expect(model.totalSets).toBe(60);
    expect(model.columns[1].sets).toBeNull();
    expect(model.columns[1].rpeY).toBeNull();
    expect(model.deloadWeeks).toBe(1);
    expect((fixture.nativeElement as HTMLElement).querySelector('.mc-bar-empty')).not.toBeNull();
  });

  it('muestra un mensaje vacío si no hay fases ni semanas', () => {
    const el: HTMLElement = create([], []).nativeElement;
    expect(el.querySelector('svg')).toBeNull();
    expect(el.textContent).toContain('Define las fases del macrociclo');
  });
});
