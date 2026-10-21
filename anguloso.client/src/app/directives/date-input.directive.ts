import { Directive, ElementRef, forwardRef, HostBinding, HostListener, Renderer2 } from '@angular/core';
import { AbstractControl, ControlValueAccessor, NG_VALIDATORS, NG_VALUE_ACCESSOR, ValidationErrors, Validator } from '@angular/forms';

export function parseDateDisplay(value: string): string | null {
  const input = value.trim();
  if (!input) return '';
  const match = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(input);
  if (!match) return null;
  const day = Number(match[1]);
  const month = Number(match[2]);
  const year = Number(match[3]);
  if (year < 1 || month < 1 || month > 12 || day < 1) return null;
  const date = new Date(0);
  date.setUTCHours(0, 0, 0, 0);
  date.setUTCFullYear(year, month - 1, day);
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) return null;
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

export function formatDateDisplay(value: unknown): string {
  if (value == null || value === '') return '';
  if (value instanceof Date) {
    if (Number.isNaN(value.getTime())) return '';
    return `${String(value.getDate()).padStart(2, '0')}/${String(value.getMonth() + 1).padStart(2, '0')}/${String(value.getFullYear()).padStart(4, '0')}`;
  }
  const input = String(value).trim();
  const iso = /^(\d{4})-(\d{2})-(\d{2})(?:$|T)/.exec(input);
  if (iso) return `${iso[3]}/${iso[2]}/${iso[1]}`;
  const canonical = parseDateDisplay(input);
  if (canonical) {
    const [year, month, day] = canonical.split('-');
    return `${day}/${month}/${year}`;
  }
  return input;
}

@Directive({
  selector: 'input[appDateInput]',
  standalone: true,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => DateInputDirective), multi: true },
    { provide: NG_VALIDATORS, useExisting: forwardRef(() => DateInputDirective), multi: true }
  ]
})
export class DateInputDirective implements ControlValueAccessor, Validator {
  @HostBinding('attr.type') readonly inputType = 'text';
  @HostBinding('attr.inputmode') readonly inputMode = 'numeric';
  @HostBinding('attr.placeholder') readonly defaultPlaceholder = 'DD/MM/AAAA';
  @HostBinding('attr.autocomplete') readonly autocomplete = 'off';
  @HostBinding('attr.aria-invalid') get ariaInvalid(): string | null {
    return this.currentValue && parseDateDisplay(this.currentValue) === null ? 'true' : null;
  }

  private currentValue = '';
  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};
  private onValidatorChange: () => void = () => {};

  constructor(private readonly element: ElementRef<HTMLInputElement>, private readonly renderer: Renderer2) {}

  writeValue(value: unknown): void {
    this.currentValue = formatDateDisplay(value);
    this.renderer.setProperty(this.element.nativeElement, 'value', this.currentValue);
    this.onValidatorChange();
  }

  registerOnChange(fn: (value: string) => void): void { this.onChange = fn; }
  registerOnTouched(fn: () => void): void { this.onTouched = fn; }
  registerOnValidatorChange(fn: () => void): void { this.onValidatorChange = fn; }

  setDisabledState(disabled: boolean): void {
    this.renderer.setProperty(this.element.nativeElement, 'disabled', disabled);
  }

  validate(_: AbstractControl): ValidationErrors | null {
    return this.currentValue && parseDateDisplay(this.currentValue) === null
      ? { dateFormat: { requiredFormat: 'DD/MM/AAAA' } }
      : null;
  }

  @HostListener('input', ['$event.target.value'])
  handleInput(value: string): void {
    this.currentValue = value;
    const canonical = parseDateDisplay(value);
    if (canonical !== null) this.onChange(canonical);
    this.onValidatorChange();
  }

  @HostListener('blur')
  handleBlur(): void {
    this.onTouched();
    const canonical = parseDateDisplay(this.currentValue);
    if (canonical !== null) {
      this.currentValue = formatDateDisplay(canonical);
      this.renderer.setProperty(this.element.nativeElement, 'value', this.currentValue);
    }
    this.onValidatorChange();
  }
}
