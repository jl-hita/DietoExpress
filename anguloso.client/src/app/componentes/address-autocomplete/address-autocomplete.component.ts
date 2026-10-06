import { Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Subscription, of } from 'rxjs';
import { catchError, debounceTime, distinctUntilChanged, switchMap } from 'rxjs/operators';
import { AddressService } from '../../servicios/address.service';
import { AddressSuggestion } from '../../modelos/address';

@Component({
  selector: 'app-address-autocomplete',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatAutocompleteModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './address-autocomplete.component.html',
  styleUrl: './address-autocomplete.component.css'
})
/**
 * Campo reutilizable de dirección.
 * La consulta se limita a España y solo se realiza tras una pausa de escritura para reducir llamadas.
 */
export class AddressAutocompleteComponent implements OnInit, OnDestroy, OnChanges {
  @Input() label = 'Dirección';
  @Input() placeholder = 'Empieza a escribir una dirección';
  @Input() initialValue = '';
  @Output() suggestionSelected = new EventEmitter<AddressSuggestion>();
  @Output() valueChanged = new EventEmitter<string>();

  control = new FormControl('', { nonNullable: true });
  suggestions: AddressSuggestion[] = [];
  loading = false;
  private subscription?: Subscription;
  private valueSubscription?: Subscription;

  constructor(private readonly addressService: AddressService) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['initialValue']) {
      const value = this.initialValue ?? '';
      if (this.control.value !== value) {
        this.control.setValue(value, { emitEvent: false });
      }
    }
  }

  ngOnInit(): void {
    this.control.setValue(this.initialValue, { emitEvent: false });

    this.subscription = this.control.valueChanges.pipe(
      debounceTime(350),
      distinctUntilChanged(),
      switchMap(value => {
        const query = value.trim();
        if (query.length < 3) {
          this.loading = false;
          this.suggestions = [];
          return of({ suggestions: [] });
        }

        this.loading = true;
        return this.addressService.search(query).pipe(
          catchError(() => of({ suggestions: [] }))
        );
      })
    ).subscribe(result => {
      this.suggestions = result.suggestions;
      this.loading = false;
    });

    this.valueSubscription = this.control.valueChanges.subscribe(value => {
      this.valueChanged.emit(value);
    });
  }

  selectSuggestion(event: MatAutocompleteSelectedEvent): void {
    const suggestion = event.option.value as AddressSuggestion;
    this.control.setValue(suggestion.displayName);
    this.suggestionSelected.emit(suggestion);
  }

  displaySuggestion(suggestion: AddressSuggestion | string | null): string {
    if (typeof suggestion === 'string') {
      return suggestion;
    }
    return suggestion?.displayName ?? '';
  }

  ngOnDestroy(): void {
    this.subscription?.unsubscribe();
    this.valueSubscription?.unsubscribe();
  }
}
